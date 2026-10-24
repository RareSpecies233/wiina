# 安装、卸载与分发

Wiina 提供两种分发形式，二者使用**同一份载荷**（`artifacts/wiina-1.2.7-win-x64/`）：

| 形式 | 产物 | 卸载方式 |
|---|---|---|
| 便携 zip | `artifacts/wiina-1.2.7-win-x64.zip` | 同目录的 `Uninstall Wiina.exe` |
| 按用户安装包 | `artifacts/Wiina-Setup-1.2.7-x64.exe` | 设置 → 应用 → Wiina → 卸载（Inno Setup 卸载器） |

> 产物的字节数与 SHA-256 以每次实际构建为准（`scripts/release.ps1` 会在结束时打印）；
> 本文不记录具体哈希，避免复制过期数值。

## 安装包（Inno Setup，按用户）

`installer/wiina.iss` 是唯一的安装脚本，由 `scripts/release.ps1` 在便携 zip 之后编译：

- **按用户安装**：`PrivilegesRequired=lowest`，不弹 UAC，不写 HKLM；默认目录
  `%LOCALAPPDATA%\Programs\Wiina`（`{autopf}` 在按用户模式下解析到 Local AppData）。
- **固定 AppId**：`{9C1A5E1D-7A5B-4E1F-9C3D-2B6A8F4E7D10}`，保证升级覆盖同一安装、卸载项稳定。
- **语言**：简体中文（`installer/ChineseSimplified.isl`，Inno Setup 官方翻译，UTF-8 BOM）与
  English；向导按 Windows UI 语言选择并保留语言下拉。
- **载荷**：`artifacts/publish/`（由 `release.ps1` 从刚校验过的便携 zip 解压），排除
  `Uninstall Wiina.exe`、`*.pdb`、`*.xml`——安装版用 Inno 自己的卸载器，不重复放便携卸载器。
- **快捷方式**：开始菜单必选，桌面快捷方式可选（默认不勾）。
- **安装后**：可直接启动 Wiina；应用数据仍写入 `%LOCALAPPDATA%\Wiina`，与便携版共用。
- 产物名：`artifacts/Wiina-Setup-1.2.7-x64.exe`（`OutputBaseFilename=Wiina-Setup-{#MyAppVersion}-x64`）。

### 卸载：是否保留配置文件？

卸载时（非静默、且检测到用户数据）弹出中文提示：

> 是否保留配置文件？
> “是”（推荐）：保留 `%LOCALAPPDATA%\Wiina` 中的配置、日志，以及 Wiina 注册的文件关联。
> “否”：同时删除这个配置文件夹和 Wiina 的注册表项（永远不会删除你的媒体文件）。

- 默认按钮语义是**保留**（`MB_YESNO`，`IDYES` = 保留）。
- **静默卸载一律保留**（`UninstallSilent` 时不询问，`KeepUserData := True`）。
- 选择“否”时执行 `DeleteUserData`，只删除：
  - 24 个受支持扩展名的 `HKCU\Software\Classes\.<ext>\OpenWithProgids` 中的
    `Wiina.Video` / `Wiina.Audio` 值；
  - `HKCU\Software\Classes\Wiina.Video`、`Wiina.Audio`（含子键）；
  - `HKCU\Software\RegisteredApplications` 的 `Wiina` 值；
  - `HKCU\Software\Wiina`（含子键）；
  - `%LOCALAPPDATA%\Wiina`（配置、日志、mpv 配置、插件空目录、崩溃日志）。
- **不会触碰任何媒体文件**，也不会删除其他播放器的注册项。
- 扩展名清单在 `installer/wiina.iss` 的 `OpenWithExtensions` 中镜像
  `src/Wiina.Core/Files/FileAssociationCatalog.cs`；改扩展名时两处要同步。

## 便携卸载器（`Uninstall Wiina.exe`）

便携版没有安装器，因此 zip 里附带一个独立的、单文件裁剪的 `src/Wiina.Uninstall`：

1. 确认卸载（默认“否”，因为会删除文件）；
2. 询问是否保留配置（默认“是”，与安装包一致）；
3. 若检测到 Wiina 的文件关联，询问是否一并移除；
4. 按选择删除 `%LOCALAPPDATA%\Wiina` 与 Wiina 自己的 HKCU 关联项（永不触及媒体）；
5. 用一个分离的批处理文件在本进程退出后删除便携文件夹，`rmdir` 最多重试约 30 秒
   （覆盖 exe 映像锁尚未释放的窗口）。

命令行开关（用于静默部署与打包冒烟测试；未知开关会被忽略）：

| 开关 | 作用 |
|---|---|
| `--silent`, `-s` | 不弹任何消息框；失败用退出码报告 |
| `--keep-config` | 保留配置（静默模式下也是默认） |
| `--remove-config` | 删除 `%LOCALAPPDATA%\Wiina` |
| `--keep-associations` | 保留文件关联 |
| `--remove-associations` | 删除 Wiina 自己的关联项 |
| `--no-self-delete` | 不删除便携文件夹（文件被占用/测试时） |
| `--dry-run` | 只把计划写入 `%TEMP%` 日志，不改任何东西 |

退出码：`0` = 完成或取消；`1` = 无法自动删除文件夹（提示用户手动删除）。
自删除前会做安全检查：拒绝把驱动器根目录或用户已知文件夹（桌面、文档、AppData 等）
当作 Wiina 目录递归删除；`--dry-run` 会报告该检查结果。

**安装版不包含** `Uninstall Wiina.exe`；升级/修复通过重新运行安装包完成。

## 构建与发布

```powershell
# 1) 便携 zip（内部先发布卸载器，再构建/校验 Wiina.dll 版本并打包）
pwsh -File scripts/package.ps1 -Version 1.2.7

# 2) 安装包（在便携 zip 之后；没有 ISCC 时脚本会警告并跳过）
pwsh -File scripts/release.ps1 -Version 1.2.7

# 只要便携 zip，跳过安装包
pwsh -File scripts/release.ps1 -Version 1.2.7 -NoInstaller
```

`release.ps1` 的安装包流程：

1. 同步版本到 `Directory.Build.props`、`Branding.Version`、
   `PackageManifest.Version`/`ArchiveFileName`、`app.manifest`、
   `scripts/package.ps1` 默认值与 `installer/wiina.iss` 的 `MyAppVersion`；
2. 调用 `package.ps1` 生成并校验便携 zip（含 `Uninstall Wiina.exe` 条目检查）；
3. 解压 zip 到 `artifacts/publish`，编译 `installer/wiina.iss`；
4. 打印两个产物的路径、大小与 SHA-256。

ISCC 查找顺序：`PATH` 上的 `iscc` → `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`
→ `%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe` → `%ProgramFiles%\Inno Setup 6\ISCC.exe`。
未安装 Inno Setup 时只产出便携 zip，不影响发布。

## 已知限制

- 两个可执行文件都**未签名**，SmartScreen 可能提示；没有自动更新，也没有 MSIX/Store 版本。
- 只支持按用户安装；不提供所有用户（HKLM）安装、不写系统级 PATH/文件关联。
- 文件关联仍由应用内的“设置 → 文件关联”注册，安装包不会静默抢占默认程序；
  Windows 仍要求在“默认应用”中确认。
- 便携卸载器删除自身目录依赖批处理重试；若其他进程占用文件，会以退出码 1 结束并提示
  手动删除。
- 卸载只删除 Wiina 自己的键；其他播放器残留在同一扩展名下的 `OpenWithProgids` 值不受影响。

- **未签名**：安装包没有代码签名，SmartScreen / 杀毒启发式可能提示；继续执行与 SHA-256 校验步骤见 README 顶部。安装包本身仍是按用户安装，与「文件关联 → 管理员一键关联」
  的机器级注册是两件事。
