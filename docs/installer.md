# 安装、卸载与分发

Wiina 提供两种分发形式，二者使用**同一份载荷**（`artifacts/wiina-1.3.5-win-x64/`）：

| 形式 | 产物 | 卸载方式 |
|---|---|---|
| 便携 zip | `artifacts/wiina-1.3.5-win-x64.zip` | 同目录的 `Uninstall Wiina.exe` |
| 按用户安装包 | `artifacts/Wiina-Setup-1.3.5-x64.exe` | 设置 → 应用 → Wiina → 卸载（Inno Setup 卸载器） |

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
- 产物名：`artifacts/Wiina-Setup-1.3.5-x64.exe`（`OutputBaseFilename=Wiina-Setup-{#MyAppVersion}-x64`）。

### 卸载：关联一律移除，只问配置

1.3.2 起，卸载只询问一件事：

> 是否保留配置文件？
> “是”（推荐）：保留 `%LOCALAPPDATA%\Wiina` 中的设置、播放历史、插件和日志，重新安装后可以继续使用。
> “否”：同时删除这个配置文件夹（不会删除你的媒体文件；Wiina 注册的文件关联始终会被移除）。

- 默认按钮语义是**保留**（`MB_YESNO`，`IDYES` = 保留）；**静默卸载一律保留**
  （`UninstallSilent` 时不询问，`KeepUserData := True`）。
- **文件关联始终移除**（`HKCU` 与 `HKLM` 都清），无论配置是否保留：残留的
  `Wiina.Video` / `Wiina.Audio` 会指向一个即将消失的目录。
- 清理范围（只删 Wiina 自己的项）：
  - 24 个受支持扩展名的 `Software\Classes\.<ext>\OpenWithProgids` 中
    `Wiina.Video` / `Wiina.Audio` 值（HKCU + HKLM）；
  - `Software\Classes\Wiina.Video`、`Wiina.Audio`（含子键，两个 hive）；
  - `Software\RegisteredApplications` 的 `Wiina` 值（两个 hive）；
  - `HKCU\Software\Wiina`（含子键）；
  - **仅当选择“否”**：`%LOCALAPPDATA%\Wiina`（配置、日志、mpv 配置、缩略图缓存、崩溃日志）。
- **不会触碰任何媒体文件**，也不会删除其他播放器的注册项。
- 扩展名清单在 `installer/wiina.iss` 的 `OpenWithExtensions` 中镜像
  `src/Wiina.Core/Files/FileAssociationCatalog.cs`；改扩展名时两处要同步。

### 管理员权限到底在哪一层

- **安装包本身不需要管理员**（`PrivilegesRequired=lowest`，按用户装到
  `%LOCALAPPDATA%\Programs\Wiina`，不弹 UAC、不写 HKLM）。
- **卸载时的提权分两种**：
  - 已安装副本走 Inno 卸载器：1.3.2 起它在 `InitializeUninstall` 阶段用 `runas` 重启自身并退出旧实例，
    因此**启动即弹一次 UAC**，随后直接删 `HKLM` 中的 Wiina 关联；不再派生提权 PowerShell 子进程。
    用户拒绝 UAC 时卸载继续（仅清 HKCU），日志记录剩下的机器级项；静默卸载不会弹 UAC。
  - 便携 zip 里的 `Uninstall Wiina.exe` 的 manifest 是 `requireAdministrator`，
    **启动即请求管理员**，因此能直接清 `HKLM` 和其他已加载用户的 hive。
- 两条卸载路径都**先结束占用安装目录的 Wiina 进程**：便携卸载器会弹「是否强制结束」，
  `--silent` 时直接强制结束；Inno 侧在安装/升级（`PrepareToInstall`）和卸载
  （`CurUninstallStepChanged`）时按安装目录路径过滤后 `Stop-Process -Force`，
  两次重试避开「进程刚启动」的窗口。其他目录里的便携副本不受影响。

### 原地升级（1.3.0 起）

- 固定 `AppId` + `UsePreviousAppDir=yes` + `UsePreviousTasks=yes`：升级覆盖同一个安装，
  沿用上次目录与快捷方式/任务选择，不产生第二份卸载项。
- 升级不会删除 `%LOCALAPPDATA%\Wiina`，也不重置文件关联；配置在升级后继续可用。
- `CloseApplications=yes` 让 Inno 的 Restart Manager 先请求优雅关闭，上面的显式
  `Stop-Process` 兜底处理 mpv 全屏等不响应的情况。

## 便携卸载器（`Uninstall Wiina.exe`）

便携版没有安装器，因此 zip 里附带一个独立的、单文件裁剪的 `src/Wiina.Uninstall`
（manifest `requireAdministrator`，见上）：

1. 启动即请求管理员权限，确认卸载（默认“否”，因为会删除文件）；
2. 若检测到占用安装目录的 Wiina 进程，提示是否强制结束（`--silent` 直接强制）；
3. **总是**删除 HKCU / HKLM（及其他已加载用户 hive）里 Wiina 自己的关联项与
   `HKCU\Software\Wiina`；
4. 询问是否保留配置（默认“是”，与安装包一致；`--silent` 保留）；
5. 用一个分离的批处理文件在本进程退出后删除便携文件夹，`rmdir` 最多重试约 30 秒
   （覆盖 exe 映像锁尚未释放的窗口）。

命令行开关（用于静默部署与打包冒烟测试；未知开关会被忽略）：

| 开关 | 作用 |
|---|---|
| `--silent`, `-s` | 不弹任何消息框；失败用退出码报告 |
| `--keep-config` | 保留配置（静默模式下也是默认） |
| `--remove-config` | 删除 `%LOCALAPPDATA%\Wiina` |
| `--keep-associations` / `--remove-associations` | **已废弃、无效果**：关联现在总是移除；保留是为了旧快捷方式/流水线不报错 |
| `--no-self-delete` | 不删除便携文件夹（文件被占用/测试时） |
| `--dry-run` | 只把计划写入 `%TEMP%` 日志，不改任何东西 |

退出码：`0` = 完成或取消；`1` = 无法结束进程或删除文件夹；`2` = 未以管理员启动
（例如用 `dotnet Wiina.Uninstall.dll` 绕过 manifest）。
自删除前会做安全检查：拒绝把驱动器根目录或用户已知文件夹（桌面、文档、AppData 等）
当作 Wiina 目录递归删除；`--dry-run` 会报告该检查结果。

**安装版不包含** `Uninstall Wiina.exe`；升级/修复通过重新运行安装包完成。
## 构建与发布

```powershell
# 1) 便携 zip（内部先发布卸载器，再构建/校验 Wiina.dll 版本并打包）
pwsh -File scripts/package.ps1 -Version 1.3.5

# 2) 安装包（在便携 zip 之后；没有 ISCC 时脚本会警告并跳过）
pwsh -File scripts/release.ps1 -Version 1.3.5

# 只要便携 zip，跳过安装包
pwsh -File scripts/release.ps1 -Version 1.3.5 -NoInstaller
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

## 升级与保留数据

- 安装包是**就地升级**：Inno 的 `CloseApplications=yes` 会在需要时关闭旧进程，
  `RestartApplications=no` 不强行重启；升级不会改动 `%LOCALAPPDATA%\Wiina`。
- 文件关联（HKCU 或管理员模式的 HKLM）在升级后保持；安装包不会静默抢占默认程序。
- 卸载时**关联一律删除**；只有明确回答「否」（不保留配置文件）才删除 `%LOCALAPPDATA%\Wiina`。

## 导出 mpv.net 配置

`resources/mpvnet/` 提供一份给 mpv.net 用户的手工配置：`wiina.conf`（mpv 选项 +
`[wiina]` profile）与 `wiina-input.conf`（键位）。把 `wiina.conf` 合并进 mpv.net 的
`mpv.conf`（或整体替换），并把 `wiina-input.conf` 放到同一目录；这两个文件不随安装包
写入系统，也不会被 Wiina 自动安装。详见 `resources/mpvnet/README.md`。

## 已知限制

- 两个可执行文件都**未签名**，SmartScreen 可能提示；没有自动更新，也没有 MSIX/Store 版本。
- 只支持按用户安装；不提供所有用户（HKLM）安装、不写系统级 PATH/文件关联。
- 文件关联仍由应用内的“设置 → 文件关联”注册，安装包不会静默抢占默认程序；
  Windows 仍要求在“默认应用”中确认。
- 便携卸载器删除自身目录依赖批处理重试；若其他进程占用文件，会以退出码 1 结束并提示
  手动删除。卸载器始终以管理员启动（便携版 manifest 强制；安装版在需要清 HKLM 时提权）。
- 卸载只删除 Wiina 自己的键；其他播放器残留在同一扩展名下的 `OpenWithProgids` 值不受影响。

- **未签名**：安装包没有代码签名，SmartScreen / 杀毒启发式可能提示；继续执行与 SHA-256 校验步骤见 README 顶部。安装包本身仍是按用户安装，与「文件关联 → 管理员一键关联」的机器级注册、以及卸载器的提权是两件事。