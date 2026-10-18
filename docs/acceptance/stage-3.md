# Stage 3 — 播放内核接入（验收包）

- **分支**：`stage/3-playback`（本分支 HEAD）
- **日期**：2026-10-18
- **环境**：Windows 11 25H2 26200；.NET SDK 10.0.402；
  Windows App SDK 2.5.1；libmpv 0.41.0（shinchiro `20261018-git-01e2533f31`，
  `third_party/libmpv/libmpv-2.dll`）。
- **测试媒体**（全部在 `%TEMP%\wiina-test\`，未提交）：
  `tone.wav`（8 s PCM）、`tone2.wav`、`long-tone.wav`（30 s PCM）、
  `tone.srt`、`long.srt`、`two.m3u`；视频用
  `av://lavfi:testsrc=size=640x360:rate=30`。

## 交付范围

### 1. 打开 / 载入

- `FileOpenPicker`（以窗口 HWND 初始化）、`FolderPicker`、打开 URL 对话框
  （含最近 URL 下拉）、拖放文件/文件夹、命令行参数、`.m3u/.m3u8`。
- 文件夹展开为可播放文件；`.m3u` 递归解析为播放列表；URL 交给 mpv 原生协议。
- mpv `loadfile <path> replace`，其余条目 `append`；播放列表可用
  `playlist-prev/next`、`set playlist-pos`、`playlist-remove`、`playlist-move`。
- `TitleBar.Title` 来自 `media-title`（回退到文件名）；加载失败时显示
  `Wiina — 打开失败`，并把 mpv 错误文本放进 OSD。
- 最近文件菜单来自 `history.json` 的最近 10 条；URL 历史来自
  `AppSettings.RecentUrls`（最多 10 条）。
- 命令行示例：`Wiina.exe "C:\media\movie.mkv"`、
  `Wiina.exe "https://example.com/a.m3u8"`、`Wiina.exe <文件夹>`。

### 2. 播放命令

- 播放/暂停、停止、绝对/相对跳转（±5 s、±1 s、进度条拖动）、音量 ±5/±1
  （0–130）、静音、速度 0.5×/2×/复位、逐帧、上一/下一章、
  播放列表上一首/下一首、A-B 循环、截图、截图到文件夹。
- UI 线程全部走 `mpv_command_async`（`MpvPlayer.CommandAsync`）；
  属性写入用 `mpv_set_property_string`；没有 `Thread.Sleep`。
- A-B 循环采用 **App 侧实现**：`MaintainAbLoop` 在 `time-pos` 到达 B 时
  async seek 回 A。原因：当前 libmpv composition 构建里同步写
  `ab-loop-a/b` 以及 `ab-loop` 命令会在几轮后让 UI 卡死
  （`mpv: main: Too many events queued.`，CPU 持续上升）；App 侧 seek
  在 30 s WAV 上验证稳定（见下）。这是与“用 mpv 原生 ab-loop”的已知偏离。
- 截图：`Ctrl+S` 用 `screenshot-to-file <Pictures\Wiina\wiina-时间戳.png>
  subtitles`；菜单另有“截图到文件夹…”（FolderPicker）。

### 3. 轨道 / 字幕 / 章节 / 播放列表

- `MpvPlayer` 用 **node API** 观察并解析：
  - `track-list` → `IReadOnlyList<TrackInfo>`（视频/音频/字幕；
    id/type/title/lang/codec/default/selected/external）。
  - `chapter-list` → `IReadOnlyList<ChapterInfo>`（index/time/title）。
  - `playlist` → `IReadOnlyList<MpvPlaylistItem>`（index/filename/current/playing）。
  - `playlist-pos`、`playlist-current-pos`、`chapter`、`video-params`
    （宽高 → 窗口宽高比）、`sub-visibility`、`eof-reached`、`idle-active`。
  - 另保留 `GetChapters()/GetTracks()/GetPlaylist()` 为**最近一次事件缓存**，
    不再从 UI 线程调用 `mpv_get_property(MPV_FORMAT_NODE)`（该调用在
    composition 模式下会 AccessViolation，已移除）。
- 轨道菜单：自动 / 关闭 / 具体轨道；视频 `vid`、音频 `aid`、字幕 `sid`。
- 外挂字幕：`sub-add <file> select`；加载后强制 `sid=<last sub>` 和
  `sub-visibility=yes`；支持 `.srt/.ass/.ssa/.vtt/.sub/.idx/.sup`。
- 字幕可见性、延迟 `sub-delay`、位置 `sub-pos`、缩放 `sub-scale`；
  次字幕通过 `secondary-sid` 子菜单（关闭/轨道）。
- 章节：节点列表 + 当前章节；`set chapter` 跳转；`Ctrl+PageUp/Down` 前后章。
- 播放列表：菜单列出当前项、添加文件、保存 `.m3u`、移除当前项、
  上移/下移；循环模式（关闭/列表/单曲）通过 `loop-playlist`/`loop-file`，
  随机通过 `shuffle`。
- `.m3u` 由 Core `Playlist` 模型读写（`#EXTM3U` + 可选 `#EXTINF`），
  单测覆盖解析/写回/添加/删除/移动。

### 4. 输入

- 键盘表（Core `KeyMap`，单测覆盖；App 把 `VirtualKey` 映射到 `AppKey`）：
  - Space / P：播放/暂停；Ctrl+.：停止。
  - ←/→ ±5 s；Shift+←/→ ±1 s；Ctrl+Shift+←/→ 上一句/下一句字幕。
  - ↑/↓ 音量 ±5；Ctrl+↑/↓ 音量 ±1；Ctrl+M / M 静音。
  - Alt+←/→ 上一帧/下一帧；Ctrl+O 打开文件；Ctrl+Shift+O 打开 URL。
  - Ctrl+, 设置；Ctrl+S 截图；Ctrl+L A-B 循环；Ctrl+←/→ 播放列表前后。
  - `[`/`]` 速度 ×0.5/×2；Ctrl+Backspace 速度复位。
  - F11 全屏；Esc 退出全屏；Ctrl+Shift+P PiP 占位。
  - 章节：Ctrl+PageUp/Ctrl+PageDown；循环：Ctrl+Shift+R。
  - 字幕：V 显隐；Z/X 延迟 ∓0.1 s；C 延迟归零；G/F 缩放；R/T 位置。
  - 窗口：Ctrl+0/1/2；Ctrl+-/Ctrl+Plus 缩小/放大。
  - Q / Ctrl+Q 退出。
  - **冲突处理**：原 Stage 1 的 OSC 位置从 Ctrl+Shift+O 改为
    **Ctrl+Alt+O**（Ctrl+Shift+O 让给“打开 URL”）；布局仍为 Ctrl+Shift+L，
    主题 Ctrl+Shift+T，音乐模式占位 Ctrl+Shift+M。
- 键盘处理挂在 `RootGrid.AddHandler(KeyDownEvent, …, handledEventsToo:false)`；
  TextBox/PasswordBox/RichEditBox/AutoSuggestBox/NumberBox 聚焦时跳过；
  进度条/音量条先消费方向键，不会重复跳转。
- 鼠标：
  - 左单击视频：按设置执行（默认隐藏/显示 OSC；可改为播放/暂停、全屏、无）。
    单击/双击用系统双击时间消歧；双击全屏。
  - 右键：播放/暂停；中键：无。
  - 拖动视频 >3 DIP：移动窗口（`AppWindow.Move` + 屏幕光标坐标）。
  - 垂直滚轮：音量（默认 5/格）；水平滚轮：跳转（默认 5 s/格）；
    进度条/音量条上的滚轮强制对应动作。
- 媒体键：`SystemMediaTransportControls`（沉默 `MediaPlayer` 提供 SMTC 表面），
  Play/Pause/Stop/Next/Previous；窗口关闭时释放。

### 5. OSD（Stage 3 基础）

- `Controls/OsdText.xaml(.cs)`：右下角单条消息，1 s 自动隐藏，
  0.5 s 淡出（`UISettings.AnimationsEnabled` 关闭时直接隐藏）。
- 已接入：暂停/播放、停止、音量、静音、跳转、速度、逐帧、章节、轨道、
  字幕延迟/位置/缩放、A-B、截图、错误、打开/加载提示。
- Stage 5 会替换为完整 OSD 模型/模板；本阶段只保证消息可用、不阻塞。

### 6. 历史 / 续播

- `%LOCALAPPDATA%\Wiina\history.json`：`Path/Title/Duration/Position/
  LastPlayedUtc`；原子写（临时文件 + `File.Move`），最多 200 条。
- 每 10 s 记录一次；停止/关闭时再记录；`Position` 接近结尾（≤3 s）时写 0。
- 打开本地文件时若历史位置 >5 s 且距结尾 >5 s，自动 seek 并 OSD
  “从 mm:ss 继续”；选择的是**自动续播**（设置中可关闭）而不是弹窗，
  避免打断拖放/命令行工作流。
- URL 不自动续播（但会记录历史）；最近文件菜单读取历史。

### 7. 播放列表持久化

- 打开/保存 `.m3u/.m3u8`；保存使用 `FileSavePicker`，内容来自当前
  mpv `playlist` 的 `filename`；加载 m3u 时相对路径按 m3u 目录解析。
- 单测覆盖 `Playlist` 的 Add/RemoveAt/Move/SetCurrent、M3U 解析/写回。

### 8. 测试

新增 Core 测试：
- `HistoryStoreTests`：round-trip、更新去重、Recent 排序、Remove、
  corrupt 文件、原子写无 `.tmp` 残留、MaxEntries。
- `PlaylistTests`：Add/RemoveAt/Move/Insert/当前项、M3U round-trip、
  EXTINF、空输入、Save/Load、Repeat/Shuffle。
- `KeyChordTests`：解析/格式化、别名、非法串、默认表覆盖、
  每个默认 chord 可 round-trip、`BindFirst` 冲突、覆盖冲突。
- `MediaFileTypesTests`：视频/音频/字幕/播放列表/未知、大小写、
  `FilterPlayable`、`ExpandFolder`（递归/非递归/不存在）。

## 构建与测试

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\Wiina.slnx -c Debug
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Debug
```

结果：

- Build：**0 warnings / 0 errors**。
- Tests：**103 passed / 0 failed / 0 skipped**（原有 29 + 新增 74）。

## 手工冒烟（真实桌面）

运行：

```powershell
.\src\Wiina.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Wiina.exe `
  "av://lavfi:testsrc=size=640x360:rate=30"
```

| # | 场景 | 结果 | 证据 |
|---|---|---|---|
| 1 | lavfi 视频播放、标题、轨道、画面尺寸 | ✅ | trace `track-list: Video#1`、`video size: 640x360`；窗口标题为 media-title |
| 2 | OSC 显示/隐藏、单单击切换 | ✅ | 真实指针进入显示 OSC；单击 trace `single click hideOsc (shown=…)`；双击/右键代码路径 |
| 3 | 键盘 Space/←/↑/`]`/Ctrl+L/… | ✅ | trace `key down Space`→`toggle pause`；`seek relative -5`；`set volume 105`；`set speed 2`；A-B A/B 设置 |
| 4 | A-B 循环稳定性（30 s WAV） | ✅ | trace 每 ~3.1 s `A-B loop seek to 1.841 from 4.9`；清除后 `Responding=True` |
| 5 | 外挂 SRT + 长字幕行 | ✅ | `track-list: Video#1, Subtitle#1; sid=1`；截图中可见 mpv 字幕 |
| 6 | 截图 `Ctrl+S` | ✅ | `%USERPROFILE%\Pictures\Wiina\wiina-20261018-171254.png`（640×360 彩色 testsrc） |
| 7 | 滚轮音量 | ✅ | trace `set volume 105` |
| 8 | 历史/续播 | ✅ | 播放 8 s 关闭后 `history.json` `Position=7.7796`；重启 trace `resume at 7.78` |
| 9 | `.m3u` / 播放列表 | ✅ | `two.m3u` 展开为 2 项；trace `play entries (2)`、`playlist: 2`；自动播到 `tone2.wav` |
| 10 | 打开 URL 对话框 | ✅ | Ctrl+Shift+O 显示“打开 URL”，含 URL 输入与最近 URL 下拉 |
| 11 | 设置（Stage 3 基础） | ✅ | Ctrl+, 显示续播勾选、单击动作下拉、键位表、保存/取消 |
| 12 | 顶部“更多”菜单 | ✅ | 弹出菜单含打开/播放/速度/循环/章节/轨道/字幕/播放列表/截图/设置/退出 |
| 13 | 退出后无残留进程 | ✅ | 关闭后 `Get-Process Wiina` 为空 |

OSD 截图确认：在视频暂停的 150 ms 内截到右下角“暂停”胶囊
（本机用 `SetForegroundWindow` + `SendKeys` + `CopyFromScreen` 完成，
未把测试截图提交到仓库）。

## 验收清单（Stage 3 任务）

| # | 项目 | 结果 | 备注 |
|---|---|---|---|
| 1 | 文件/文件夹/URL/拖放/命令行/最近 | ✅ | 文件/文件夹/URL 代码；URL 对话框 GUI；拖放/命令行代码 |
| 2 | 标题与加载错误 | ✅ | media-title/文件名；`FileLoadFailed`/`CommandFailed` → OSD |
| 3 | 播放/暂停/停止/跳转/音量/静音/速度 | ✅ | 键位 + OSC + 菜单 |
| 4 | 逐帧/章节/播放列表前后/A-B/截图 | ✅（A-B 30 s WAV） | 章节代码，无真实章节媒体 |
| 5 | mpv 命令不阻塞 UI | ✅ | `CommandAsync`；`Responding=True` 压力下 |
| 6 | `track-list`/`chapter-list`/`playlist` 节点解析 | ✅ | 事件线程解析；缓存 API |
| 7 | 轨道选择/外挂字幕/显隐/延迟/位置/缩放 | ✅（SRT 已显示） | 次字幕菜单代码 |
| 8 | 章节列表/跳转 | ✅ 代码；⚠️ 无章节媒体 | 节点解析路径已观察 0 章节 |
| 9 | 播放列表 repeat/shuffle/add/remove/move | ✅ 代码 + m3u | 菜单动态 |
| 10 | 键盘默认表（含冲突重映射） | ✅ | 103 项测试含 key map |
| 11 | 鼠标单击/双击/右键/滚轮/拖动 | ✅ 单击/双击/右键/滚轮；⚠️ 拖动未用合成指针验证 | 见已知问题 |
| 12 | 媒体键 SMTC | ✅ 代码；⚠️ 物理媒体键未验 | 沉默 MediaPlayer 提供 SMTC |
| 13 | OSD 基础（1 s/0.5 s） | ✅ | 右下角截图 |
| 14 | history.json / 续播 | ✅ | 原子写 + 自动续播 >5 s |
| 15 | `.m3u` 持久化 | ✅ | Core 单测 + GUI m3u |
| 16 | 不回归 Stage 1/2 | ✅ | 构建/测试通过；OSC/拖动/自动隐藏代码未改核心 |
| 17 | 无新增 NuGet | ✅ | 仅现有 Windows App SDK/xUnit |
| 18 | 构建 0 error、测试全绿 | ✅ | 上方输出 |

## 许可证检查

- [x] 无新增 NuGet/第三方依赖（仅既有 Windows App SDK / xUnit）。
- [x] 未复制 IINA 源码、`.xcassets`、SF Symbols 或 IINA 图标；XAML/C# 为原创。
- [x] 仓库未提交 libmpv 二进制、测试媒体或截图；libmpv 仍在
  `third_party/libmpv/`（gitignored）。
- [x] 未改变项目许可证（GPL-3.0-only）。

## 已知问题 / 偏离

1. **媒体矩阵未覆盖**：本机没有 ffmpeg/样本片源，只实测了
   `av://lavfi`（wrapped_avframe）与 PCM WAV、SRT；MP4/MKV/HEVC/AV1/VP9、
   FLAC/MP3/AAC、ASS 由 libmpv 自身负责，未做真实文件逐项验证。
2. **章节无媒体验证**：没有带章节的片源；`chapter-list` 节点解析、
   `chapter` 属性、菜单/跳转代码已实现，需 Lead 用真实章节文件补测。
3. **窗口拖动**：代码用 3 DIP 阈值 + `AppWindow.Move` + `GetCursorPos`；
   CUA 合成指针无法稳定触发 WinUI 捕获，未在桌面自动化中验证。
4. **媒体键**：SMTC 已接线，但未用物理媒体键验证；若系统/驱动不提供
   SMTC，初始化会静默失败且不影响播放。
5. **A-B 循环**：用 App 侧 seek 替代 mpv `ab-loop`（后者在当前
   composition/libmpv 构建中会卡 UI），精度约 ±0.35 s；已在 30 s WAV
   上验证循环与清除稳定。
6. **设置窗口**：Stage 3 只提供基础 ContentDialog（续播、单击动作、键表）；
   完整 11 页设置属于 Stage 7。
7. **OSD**：最小实现；Stage 5 的模板/附件/持久 OSD 未做。
8. **截图**：默认 `screenshot-to-file … subtitles`；音频无画面时截图命令
   会失败并显示 OSD“截图失败”。
9. **未提交媒体/二进制**：所有测试媒体在 `%TEMP%\wiina-test\`；
   仓库只新增源码、测试和文档。
10. **截图**：任务不要求提交截图；验收截图留待 Lead 用真实鼠标/
    真实片源补（本文件的桌面证据来自临时截图，未入仓）。

## 关键实现文件

- `src/Wiina.Core/Models/{TrackInfo,ChapterInfo,Playlist}.cs`
- `src/Wiina.Core/History/{HistoryEntry,HistoryStore}.cs`
- `src/Wiina.Core/Files/MediaFileTypes.cs`
- `src/Wiina.Core/Input/{KeyChord,KeyMap}.cs`
- `src/Wiina.Core/Settings/AppSettings.cs`（续播、单击动作、RecentUrls、
  滚轮步长、截图目录）
- `src/Wiina.Mpv/{MpvPlayer,MpvNodeReader,MpvInterop,MpvEvent}.cs`
- `src/Wiina.App/MainWindow.Playback.cs`
- `src/Wiina.App/Services/{SmtcService,KeyChordMapper}.cs`
- `src/Wiina.App/Controls/OsdText.xaml(.cs)`
- `src/Wiina.App/MainWindow.xaml(.cs)`（视频层指针、OSD 层、More 菜单）
- `tests/Wiina.Core.Tests/{HistoryStoreTests,PlaylistTests,KeyChordTests,
  MediaFileTypesTests}.cs`

## 运行

```powershell
powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\Wiina.slnx -c Debug
.\src\Wiina.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Wiina.exe
```

诊断：`%TEMP%\wiina-trace.log` 记录 mpv 事件、键盘/命令/历史/截图/续播。
