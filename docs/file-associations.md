# 文件关联（按用户注册）

Wiina 是 **unpackaged**（非 MSIX）应用，因此不能使用 MSIX 清单里的
`uap:FileTypeAssociation`。v1.2.3 起，设置中的第 12 页「文件关联」改为**只写当前用户**
（`HKCU`）的注册表项，把 Wiina 登记为常见视频/音频格式的**可用处理程序**，并出现在
Windows 的“默认应用”里。

- 不需要管理员权限，不写 `HKLM`，不修改其他播放器的注册。
- **不会静默抢占默认程序**：Windows 仍要求用户在“默认应用”中确认，这是系统设计。

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

写入完成后调用 `SHChangeNotify(SHCNE_ASSOCCHANGED)` 通知资源管理器，因此“打开方式”
列表通常无需重启即可看到 Wiina。

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

- **仅当前用户**：切换 Windows 用户后需要在该用户下重新关联。
- **便携版可移动**：`shell\open\command` 写入的是当前 `Wiina.exe` 的绝对路径
  （`Environment.ProcessPath`）。移动解压目录后，旧路径失效，需要在设置页重新执行关联。
- **没有 MSIX 身份**：因此不能使用 `uap:FileTypeAssociation`，也不能出现在 Store 的
  “默认应用”推荐位。
- **不写 `HKLM`**：不需要管理员权限，也不会影响系统级默认。
- **不做文件类型图标替换**：`DefaultIcon` 指向 `Wiina.exe,0`，不注册自定义图标资源。

## 验证

`FileAssociationCatalogTests`（`tests/Wiina.Core.Tests`）覆盖扩展名清单、注册计划的键值
形状、`OpenWithProgids` 二进制标记，以及取消关联只涉及自有 ProgID 的白名单。整套测试
在 Release 下共 622 项通过（见 [v1.2.3 验收](acceptance/v1.2.3.md)）。
