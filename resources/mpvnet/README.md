# mpv.net 兼容配置（wiina.conf）

Wiina 的默认画质与键位，导出成 mpv.net 可直接使用的两份文件：

| 文件 | 放置位置 | 作用 |
|---|---|---|
| `wiina.conf` | mpv.net 配置目录，重命名为 `mpv.conf`（或把 `[wiina]` 段合并进现有配置） | 渲染、画质、字幕、截图、播放行为 |
| `wiina-input.conf` | 与 `mpv.conf` 同目录 | 键位绑定（Wiina 默认键位） |

配置目录：

- 便携版：`mpvnet.exe` 同级的 `config\` 目录
- 安装版：`%APPDATA%\mpv.net\`

`wiina.conf` 里的 `input-conf=~~/wiina-input.conf` 会从 mpv 配置目录加载键位表。

## 与 Wiina 的差异

- `Shift+Alt+V/A/S/P/C` 五个“侧边栏面板”命令是 Wiina 自己的 UI 命令，stock mpv / mpv.net 没有对应面板，已在键位表中注释保留；需要时改成 mpv.net 的脚本绑定。
- Wiina 使用 `d3d11-output-mode=composition` 是因为视频要叠在 WinUI 的 SwapChainPanel 上；mpv.net 是独立窗口，因此只保留 `vo=gpu-next` + `gpu-api=d3d11` + `gpu-context=d3d11`，避免多一次无谓的合成。
- Wiina 默认关闭 `deband`；需要去色带时把 `wiina.conf` 里的 `deband=no` 改成 `yes`。
- 截图目录、缓存目录等机器相关路径没有写死，沿用 mpv.net 自身设置。
