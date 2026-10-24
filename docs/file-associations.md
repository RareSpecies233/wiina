# 文件关联（按用户 / 管理员注册）

Wiina 是 **unpackaged**（非 MSIX）应用，因此不能使用 MSIX 清单里的
`uap:FileTypeAssociation`。v1.2.3 起，设置中的第 12 页「文件关联」把 Wiina 登记为常见视频/音频格式的
**可用处理程序**，并让它出现在 Windows 的“默认应用”里。v1.2.7 起提供两种注册范围：

- **按用户（默认，无需管理员）**：只写 `HKCU\Software\Classes` 下 Wiina 自己的项，不影响其他用户与系统默认。
- **按机器（管理员，1.2.7 新增）**：经 UAC 提权后写 `HKLM\Software\Classes`，让本机所有用户都能看到 Wiina，
  并尽力清除对应扩展名的 `UserChoice`。

两种方式都**不会静默抢占默认程序**：Windows 仍要求用户在“默认应用”中确认，这是系统设计。

## 注册了什么

所有键都相对 `HKEY_CURRENT_USER`：

| 位置 | 内容 |
|---|---|
| `Software\Classes\Wiina.Video` | 默认值「Wiina 视频」、`DefaultIcon`、`shell\open\command` = `"<Wiina.exe>" "%1"`、`Application\ApplicationName` |
| `Software\Classes\Wiina.Audio` | 同上，名称「Wiina 音频」 |
| `Software\Classes\.<ext>\OpenWithProgids` | 以 ProgID 为值名的空二进制值（“用以下程序打开”候选） |
| `Software\Wiina\Capabilities` | `ApplicationName`、`ApplicationDescription`、`ApplicationIcon` |
| `Software\Wiina\Capabilities\FileAssociations` | `.mp4` → `Wiina.Video`、`.mp3` → `Wiina.Audio` 等 |
| `Software\RegisteredApplications` | `Wiina` → `Software\Wiina\Capabilities`（“默认应用”读取此形状） |

注册计划由 `src/Wiina.Core/Files/FileAssociationCatalog.cs` 生成（`BuildPlan`），
只做注册表写入的是 `src/Wiina.App/Services/FileAssociationService.cs`。把计划放在 Core
是为了让测试能在不碰真实注册表的前提下断言完整覆盖。

## 支持的扩展名

- **视频（15）**：mp4、mkv、avi、mov、wmv、flv、webm、m4v、ts、m2ts、mpg、mpeg、3gp、ogv、rmvb。
- **音频（9）**：mp3、flac、aac、wav、m4a、ogg、opus、wma、ape。

两份清单是显式白名单；新增格式时同时改 `FileAssociationCatalog` 与
`FileAssociationCatalogTests`，不要在设置页硬编码。

## 设置页操作

设置 → 文件关联：

| 行 | 行为 |
|---|---|
| 当前关联状态 | 分别检查 `Wiina.Video` / `Wiina.Audio` 的 `shell\open\command` 是否指向 Wiina |
| 关联视频格式 | 注册视频 ProgID、`OpenWithProgids` 与 Capabilities |
| 关联音频格式 | 同上（音频） |
| 关联全部支持格式 | 同时注册视频与音频；重复执行是幂等的 |
| 取消 Wiina 关联 | 只删除 Wiina 自己写入的 ProgID、`OpenWithProgids` 值、`Software\Wiina` 与 `RegisteredApplications\Wiina` |
| 在默认应用中确认… | 打开 `ms-settings:defaultapps` |
| 管理员一键关联… | 1.2.7 新增：UAC 提权后写 HKLM，并尽力清除 `UserChoice` |

写入完成后调用 `SHChangeNotify(SHCNE_ASSOCCHANGED)` 通知资源管理器，因此“打开方式”
列表通常无需重启即可看到 Wiina。

## 管理员模式（HKLM，1.2.7）

设置 → 文件关联 → **管理员一键关联…**：

1. 用 `runas` 启动 `Wiina.exe --associate-elevated-video --associate-elevated-audio`；
   用户取消 UAC 时视为取消（错误 1223），提示后刷新状态，不进入错误循环。
2. 在提权进程中调用 `FileAssociationService.RegisterElevated`，写
   `HKLM\Software\Classes` 下的 `Wiina.Video` / `Wiina.Audio`、`.ext\OpenWithProgids`、
   `HKLM\Software\Wiina\Capabilities` 与 `RegisteredApplications\Wiina`。
3. 尽力删除对应扩展名的 `UserChoice`（Windows 保护该键时删除会失败，但不影响上面两步），
   然后调用 `SHChangeNotify(SHCNE_ASSOCCHANGED)`。

机器级注册对所有用户生效，也会在系统“默认应用”里出现 Wiina；真正设为默认仍需用户确认。
卸载/取消关联会同时清理 HKCU 与 HKLM 中 Wiina 自己的项，不触碰其他播放器。

### 卸载时（1.3.1 起，1.3.2 改为启动即提权）

- **安装包仍按用户安装**（`PrivilegesRequired=lowest`，不弹 UAC）；**卸载器**才需要管理员：
  1.3.2 起 Inno 卸载器在 `InitializeUninstall` 用 `runas` 重启自身并退出旧实例，**启动即弹一次 UAC**，
  随后直接清理 `HKCU` 与 `HKLM` 中 Wiina 自己的关联；不再派生提权 PowerShell 子进程。
  便携 `Uninstall Wiina.exe` 的 manifest 是 `requireAdministrator`，同样启动即请求管理员。
- 用户拒绝 UAC 时卸载继续（只清 HKCU），日志记录剩余机器级项；静默卸载不弹 UAC，仅清 HKCU。
- 卸载器不再询问是否保留关联：**关联一律删除**，只询问「是否保留配置文件？」（默认保留，静默卸载也保留）。
- 若检测到 Wiina 正在运行，Inno 侧先经 `PrepareToInstall`/`CurUninstallStepChanged` 结束占用安装目录的进程；
  便携卸载器用 `ProcessGuard` 提示是否强制结束，`--silent` 时直接结束。

## 为什么 Windows 还要确认

Windows 10/11 不允许应用通过注册表**直接**成为某扩展名的默认程序（否则任何安装包都能
静默劫持文件打开）。注册只能做到：

1. 让 Wiina 出现在“打开方式”里；
2. 让 Wiina 出现在“默认应用”的可选列表中。

把默认处理程序真正设为 Wiina 的那一步，必须由用户在系统设置里点选，或用
“打开方式 → 始终使用此应用”。Wiina 的设置页只提供跳转，不会也无法代替这次确认。
Windows 功能更新、用户手动更改或其他播放器安装都可能重置默认程序，这是正常现象。

## 如何取消

- 设置 → 文件关联 → **取消 Wiina 关联**：删除 Wiina 自己的 ProgID、`OpenWithProgids`
  值、`Software\Wiina` 与 `RegisteredApplications\Wiina`。
- `OpenWithProgids` 的父键 `.mp4` 等**只在为空**（除 `OpenWithProgids` 外没有其他值/子键）
  时才删除，避免破坏系统默认或其他应用的注册。
- 若某个默认程序已被用户设为 Wiina，取消关联后 Windows 会保留“当前默认”，需要到
  “默认应用”里另行改回；这不是 Wiina 的残留。

## 约束与边界

- **按用户模式**：只影响当前用户，切换 Windows 用户后需要在该用户下重新关联；**管理员模式**写入 HKLM 后对所有用户可见。
- **便携版可移动**：`shell\open\command` 写入的是当前 `Wiina.exe` 的绝对路径
  （`Environment.ProcessPath`）。移动解压目录后，旧路径失效，需要在设置页重新执行关联。
- **没有 MSIX 身份**：因此不能使用 `uap:FileTypeAssociation`，也不能出现在 Store 的
  “默认应用”推荐位。
- **默认不写 `HKLM`**：按用户模式不需要管理员权限；只有显式点击「管理员一键关联…」并通过 UAC 才会写机器级键。
- **不做文件类型图标替换**：`DefaultIcon` 指向 `Wiina.exe,0`，不注册自定义图标资源。

## 验证

`FileAssociationCatalogTests`（`tests/Wiina.Core.Tests`）覆盖扩展名清单、注册计划的键值
形状、`OpenWithProgids` 二进制标记，以及取消关联只涉及自有 ProgID 的白名单。整套测试
在 Release 下共 711 项通过（见 [v1.2.7 验收](acceptance/v1.2.7.md)）。