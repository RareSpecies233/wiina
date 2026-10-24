# IINA 快捷键默认值 / 编辑器 UX 参考（Windows 复刻版）

> 用途：为 Wiina 快捷键编辑器（v1.3.0 重建）提供 IINA 的事实基线。
> 取证来源：`C:\Users\zheng\Desktop\wiina\repo\iina`（IINA develop 分支）。
> 阅读约定：`⌘` = macOS Meta，迁移到 Windows 一律映射为 `Ctrl`；`⌥` = Alt。
> 所有条目均带 `文件:行` 证据；不含猜测。

> **1.3.1 更新**：设置里的 IINA 预设已移除，只保留 Wiina（默认，来自 `wiina.txt`）与 mpv 两套；旧 `IINA`
> 值在加载时迁移到 Wiina。本文件保留为编辑器 UX 的历史调研与偏差记录，不再是当前内置配置。

---

## 0. 结论先行（给编辑器实现者）

1. **IINA 的"默认键位"就是一份 mpv `input.conf` 文件**：`repo/iina/config/iina-default-input.conf`（126 行）。
   IINA 不把这些绑定编译进代码，而是首次启动时写出一份可编辑的 conf；编辑器改的也是这份文件。
2. **`#@iina` 前缀 = IINA 私有命令**；其余行是标准 mpv 命令。解析器把 `#@iina` 行还原成
   `@iina <cmd>` 形式交给播放器（`repo/iina/KeyMapping.swift:11-12`、`repo/iina/KeyBindingTranslator.swift:125`）。
3. **IINA 编辑器没有冲突检测**。全仓库 `conflict` 只命中插件菜单（`repo/iina/MenuController.swift:687` "Conflicting key shortcuts…" 属于 JS 插件），
   快捷键行本身只校验"键非空 + 动作非空"（`repo/iina/KeyRecordViewController.swift:133`）。
   冲突语义由 mpv 决定 = **后写的行覆盖前面的行**。Wiina 已有的 `KeyBindings.FindConflict`
   （`src/Wiina.Core/Input/KeyBindings.cs:392`）是**超出 IINA 的能力，应保留而不是砍掉**。
4. **IINA 没有"恢复默认"按钮、没有导出菜单**。恢复默认 = 切回内置预设（只读），导出 = "Show the Config File in Finder"
   （`repo/iina/SettingsPageKeyBindings.swift:301-313`）。内置预设共 5 套：IINA / mpv / VLC / Movist / Movist v2
   （`SettingsPageKeyBindings.swift:90-93`、`PrefKeyBindingViewController.swift:34-37`）。
5. **Wiina 现有 `IINA` 预设与 IINA 真实默认值有 6 处语义偏差**（详见 §4.2），
   最大的是 `P`（IINA = 播放/暂停，Wiina = 下一个）、`C`（IINA = 字幕延迟归零，Wiina = 下一章）、
   `Shift+Left/Right`（IINA = 按句跳字幕，Wiina = 跳 1 秒）、`J/K`（IINA 根本没有这两个绑定）。

---

## 1. IINA 默认键位全表（`config/iina-default-input.conf`）

### 1.1 IINA 私有命令（`#@iina` 开头）

| 行 | 键 | 命令 | 含义 |
|---|---|---|---|
| 4 | `Shift+⌘+V` | `video-panel` | 视频轨面板 |
| 5 | `Shift+⌘+A` | `audio-panel` | 音轨面板 |
| 6 | `Shift+⌘+S` | `sub-panel` | 字幕轨面板 |
| 28 | `Shift+⌘+P` | `playlist-panel` | 播放列表面板 |
| 32 | `Shift+⌘+C` | `chapter-panel` | 章节面板 |
| 33/34 | `Shift+⌘+>` / `<` | `add chapter ±1` | 增删章节标记 |
| 45 | `⌘+3` | `fit-to-screen` | 画面适配窗口 |
| 47/48 | `⌘+-` / `⌘+=` | `smaller-window` / `bigger-window` | 窗口缩放 |
| 50 | `Ctrl+⌘+P` | `toggle-pip` | 画中画 |
| 51 | `Shift+⌘+R` | `show-current-file-in-finder` | 在访达中显示 |
| 55 | `Alt+⌘+M` | `toggle-music-mode` | 音乐模式 |
| 56 | `Shift+⌘+T` | `live-text` | 直播文字（OCR 弹幕） |
| 71 | `⌘+D` | `find-online-subs` | 在线字幕搜索 |

### 1.2 mpv 标准命令

| 行 | 键 | 命令 | 含义 |
|---|---|---|---|
| 8 | `Ctrl+⌘+V` | `cycle video` | 循环切视频轨 |
| 9 | `Ctrl+⌘+S` | `cycle sub` | 循环切字幕轨 |
| 10 | `Ctrl+⌘+A` | `cycle audio` | 循环切音轨 |
| 12 | `SPACE` | `cycle pause` | 播放/暂停 |
| 13 | `⌘+.` | `stop` | 停止 |
| 15/16 | `RIGHT` / `LEFT` | `seek ±5` | 快进/快退 5 s |
| 17/18 | `Alt+RIGHT` / `Alt+LEFT` | `frame-step` / `frame-back-step` | 逐帧 |
| 20/21 | `Shift+LEFT` / `Shift+RIGHT` | `sub-seek -1` / `+1` | **按句**跳字幕 |
| 23 | `⌘+S` | `screenshot` | 截图 |
| 25 | `⌘+L` | `ab-loop` | A-B 循环 |
| 26 | `⌘+Shift+L` | `cycle-values loop "inf" "no"` | 无限循环开关 |
| 29/30 | `⌘+RIGHT` / `⌘+LEFT` | `playlist-next` / `-prev` | 播放列表切换 |
| 36/37 | `⌘+[` / `⌘+]` | `multiply speed 0.5` / `2.0` | 速度 ×0.5 / ×2 |
| 38/39 | `Alt+⌘+[` / `]` | `×1/1.1` / `×1.1` | 速度微调 ±10 % |
| 40 | `⌘+\` | `set speed 1.0` | 速度复位 |
| 42-44 | `⌘+0` / `⌘+1` / `⌘+2` | `window-scale 0.5/1/2` | 窗口 0.5×/1×/2× |
| 52 | `Ctrl+⌘+F` | `cycle fullscreen` | 全屏 |
| 53 | `Ctrl+⌘+T` | `cycle ontop` | 置顶 |
| 58-61 | `UP`/`DOWN`、`Alt+UP`/`DOWN` | `add volume ±5`、`±1` | 音量 |
| 63 | `⌘+/` | `cycle mute` | 静音 |
| 65-69 | `Shift+(`/`)`、`Alt+Shift+(`/`)`、`Shift+_` | `audio-delay ±0.5/±0.1`、`0` | 音频延迟 |
| 73-77 | `Z`/`X`、`Alt+Z`/`X`、`C` | `sub-delay ∓0.5`、`∓0.1`、`0` | 字幕延迟 |
| 79/80 | `ESC` / `ENTER` | `set fullscreen no` / `yes` | 进退全屏 |

### 1.3 mpv 原生单键（文件后半段"Alternative mpv key bindings"）

| 行 | 键 | 命令 | 含义 |
|---|---|---|---|
| 85 | `q` | `quit` | 退出 |
| 87 | `p` | `cycle pause` | 播放/暂停 |
| 89/90 | `.` / `,` | `frame-step` / `frame-back-step` | 逐帧 |
| 91/92 | `o` / `P` | `show-progress` | 显示进度 |
| 94 | `m` | `cycle mute` | 静音 |
| 96/97 | `Shift+PGUP`/`PGDWN` | `seek ±600` | 跳 10 分钟 |
| 99/100 | `G` / `F` | `add sub-scale ±0.1` | 字幕字号 |
| 102-104 | `r` / `R` / `t` | `add sub-pos -1/+1/+1` | 字幕位置 |
| 105 | `v` | `cycle sub-visibility` | 字幕显隐 |
| 106 | `Alt+v` | `cycle secondary-sub-visibility` | 次字幕显隐 |
| 108 | `f` | `cycle fullscreen` | 全屏 |
| 110 | `E` | `cycle edition` | 切换版本 |
| 112-126 | `POWER`/`PLAY`/`PAUSE`/`PLAYPAUSE`/`PLAYONLY`/`PAUSEONLY`/`STOP`/`FORWARD`/`REWIND`/`NEXT`/`PREV`/`VOLUME_UP`/`VOLUME_DOWN`/`MUTE`/`CLOSE_WIN` | — | 媒体键与遥控器键 |

> 注意：IINA 的两段绑定**同时生效**，后面的行覆盖前面的同键行（mpv 语义，文件内注释自述"customized a little bit from the mpv one"）。

---

## 2. 键位书写规则（编辑器必须实现的规范化）

来自 `repo/iina/KeyCodeHelper.swift`：

- **修饰键顺序**：`modifiersInOrder` 固定为 `Ctrl, Alt, Shift, Meta`（`:15-27`），生成时按此顺序拼
  （`mpvKeyCode(from:)` `:330-341`）。
- **大小写自带 Shift 时省略修饰键**：字符本身是大写时移除 `SHIFT+`（`:311-316`）。
- **`#` 与空格是 conf 保留字符**：`#` → `SHARP`，单独空格 → `SPACE`（`escapeReservedMpvKeys` `:292-302`）。
- **规范化示例**（源码注释原文，`:350-356`）：
  `Shift+-` → `_`；`Shift+=` → `PLUS`；`Meta+Alt+X` → `Alt+Meta+X`；`meta+shift+k` → `Meta+K`；
  `esc` → `ESC`；`CTRL+SHIFT+SHIFT+SHIFT+x` → `Ctrl+X`。
- **字符白名单**：只允许单 code point 且不在 control/format/separator/PUA/unassigned 类别（`isAllowedInConfFile` `:264-290`）。
- **`+` 本身**写作 `PLUS`（`:359-363`）。
- **媒体键**用 `PLAY`/`PREV`/`NEXT` 等名字（`KeyRecordViewController.swift:110-122` 的下拉框）。

> Windows 差异：`Win` 键会被系统抢占（Win+L/D/E…），编辑器应**拒绝**纯 `Win+X` 组合或给出警告；
> `Meta` 统一显示为 `Ctrl`，`⌘+.` 这类"标点 + 修饰键"在 Windows 上仍然可用。

---

## 3. IINA 快捷键编辑器 UX（`SettingsPageKeyBindings.swift`，619 行）

### 3.1 页面结构（单页，无弹窗列表）

```
[ 配置集下拉 ▾ ]  [ 搜索框................ ] [ + ]   [ 删除配置集 ]
┌──────────────────────────────────────────────────────────┐
│ 动作（显示名）                            键（右对齐）  [✎][✕] │
│ ...                                                       │
└──────────────────────────────────────────────────────────┘
```

- 页面标题 `SettingsItem.General(title: .text_KeyBindingSet)`（`:54`）。
- 搜索框 `NSSearchField` + 添加按钮同一行（`:140-141`、`:195-202`）；
  过滤谓词同时匹配 **可读动作名** 与 **原始命令行**
  （`readableAction contains[cd]` OR `prettyCommand contains[cd]`）。
- 表格行：左列动作名，右列键位；**行内 `✎` / `✕` 按钮只在选中行出现**（`:536-548`）。
- 配置集下拉：内置 5 套（只读，带锁图标）+ 用户集（`:266-271`）；
  菜单里还有 `New Key Binding Set` / `Create an Empty Set` / `Duplicate Current Set` / `Import an Existing Config File…` / `Show the Config File in Finder`（`:301-313`）。

### 3.2 只读预设的处理（值得照搬的细节）

- `isCurrentConfigEditable()` = 当前配置集不在 `defaultConfigs` 里（`:474-476`）。
- 不可编辑时：**添加按钮变成一张"锁 + 问号"图，点击弹出 `duplicate_config` 提示**（`:548`、`:425-427`）；
  删除按钮禁用（`:453`）；对锁定集做编辑/删除 → 同样的 `duplicate_config` 弹窗（`:382`、`:411`）。
- 即：**"要改内置预设？先复制成你自己的集"**，而不是静默写穿内置文件。

### 3.3 录制面板（`showKeyBindingPanel`，`:431-449`）

- `NSAlert` + `KeyRecordViewController` 作为 `accessoryView`；提示文案 "Press any key to record."。
- `initialFirstResponder = keyRecordView`（`:439`）→ **弹窗一出现就处于录制态**。
- OK 按钮的 enabled 绑定到控制器的 `ready`（`:441`）；`ready = !keyCode.isEmpty && !action.isEmpty`
  （`KeyRecordViewController.swift:23,133`）→ **键或动作任一为空都不能提交**。
- 录制区（`KeyRecordView.swift`）：`acceptsFirstResponder = true`（`:37-39`），
  `keyDown` 里取 `charactersIgnoringModifiers + modifierFlags` 交给 delegate（`:41-45`）；
  `becomeFirstResponder` 时背景变亮作为"正在录制"的视觉反馈（`:51-63`）——**没有倒计时/无转圈动画**。
- 动作字段：一个 `NSRuleEditor`（单层规则，`nestingMode = .single`，`:55-58`）从
  `KeyBindingDataLoader.load()` 的命令树里逐级选（`KeyBindingDataLoader.swift:19-70`），
  选中即生成命令行文本写回文本框（`:126-130`）；用户也可以直接手打命令。
- 媒体键：独立下拉框直接填 `PLAY`/`PREV`/`NEXT`（`:110-122`）。
- 提交时对键做 `escapeReservedMpvKeys`（`:445`）。

### 3.4 持久化

- 保存即重写 conf 文件（`KeyMapping.generateInputConf`，`KeyMapping.swift:178-181`：首行 `# Generated by IINA`）；
  同时把内存映射推给播放器（`setKeybindingsForPlayerCore()` `:478-480`）。
- 导入 = 选一个 `.conf` 复制进用户配置目录（`:344-345`）；**没有导出命令**。
- 删除用户集有确认；重名新建会提示"文件已存在"并定位到文件（`:460-472`）。

### 3.5 明确没有的能力

| 能力 | IINA 现状 |
|---|---|
| 冲突检测/冲突列表 | 无（只有插件冲突菜单，`MenuController.swift:687`） |
| 一键恢复默认 | 无（切回内置预设即可） |
| 导出 | 无（"Show the Config File in Finder" 代替） |
| 搜索 | 有（动作名 + 原始命令） |
| 分类 | 无（表格是扁平列表；分类只存在于动作选择器的命令树里） |
| 多键同功能 | 有（conf 里同一命令多行） |
| 单动作重置 | 无 |

---

## 4. 映射到 Wiina（推荐实现）

Wiina 已有的动作目录：`src/Wiina.Core/Input/KeyBindings.cs:33-89`（`PlayerActionCatalog`，55 个动作、7 个分类：
播放/跳转/音量/字幕/文件/窗口/界面/模式）。
已有能力：`FindConflict`（`:392`）、`SetBinding`（`:424`）、`ClearAction`（`:451`）、`ResetAction`（`:463`）、
`ExportJson`（`:505`）、`TryImportJson`（`:520`）、`KeyBindingProfiles`（Default / IINA / Custom，`:127-137`）、
录制 + 冲突弹窗（`src/Wiina.App/Controls/KeyBindingsEditor.xaml.cs:277-393`）。

### 4.1 映射表（IINA 默认 → Wiina 动作，Windows 键位）

`⌘`→`Ctrl`。优先级列：**P0 = 必须与 IINA 一致**，P1 = 建议补，P2 = 可选。

| IINA 键（macOS） | IINA 命令 | Wiina 动作 | Windows 键 | 优先级 |
|---|---|---|---|---|
| `SPACE` | cycle pause | `PlayPause` | `Space` | P0 ✅已有 |
| `⌘+.` | stop | `Stop` | `Ctrl+.` | P0 |
| `RIGHT` / `LEFT` | seek ±5 | `SeekForward5` / `SeekBack5` | `Right` / `Left` | P0 ✅ |
| `Alt+RIGHT` / `Alt+LEFT` | frame step | `FrameNext` / `FramePrevious` | `Alt+Right` / `Alt+Left` | P0 ✅ |
| `Shift+LEFT/RIGHT` | **sub-seek ∓1** | `SubtitleSeekPrevious` / `SubtitleSeekNext` | `Shift+Left` / `Shift+Right` | P0 ❌ 当前映射到 Seek ±1 s |
| `⌘+S` | screenshot | `Screenshot` | `Ctrl+S` | P0 ❌ 当前是裸 `S` |
| `⌘+L` | ab-loop | `AbLoop` | `Ctrl+L` | P0 |
| `⌘+Shift+L` | loop inf toggle | `LoopMode` | `Ctrl+Shift+L` | P1 |
| `⌘+RIGHT/LEFT` | playlist next/prev | `PlaylistNext` / `PlaylistPrevious` | `Ctrl+Right` / `Ctrl+Left` | P0 ✅ |
| `⌘+[` / `⌘+]` | speed ×0.5 / ×2 | `SpeedHalf` / `SpeedDouble` | `[` / `]`（或 `Ctrl+[`） | P0 |
| `Alt+⌘+[` / `]` | speed ÷1.1 / ×1.1 | *缺动作* | — | P2（建议新增） |
| `⌘+\` | speed 1.0 | `SpeedReset` | `Ctrl+\`（保留 `Backspace` 兼容） | P0 |
| `⌘+0/1/2` | window-scale | `WindowHalf` / `WindowNormal` / `WindowDouble` | `Ctrl+0/1/2` | P1 |
| `⌘+-` / `⌘+=` | smaller/bigger window | `WindowShrink` / `WindowGrow` | `Ctrl+-` / `Ctrl+=` | P1 |
| `⌘+3` | fit-to-screen | *缺动作*（现有 `WindowNormal` 近似） | — | P2 |
| `Ctrl+⌘+F` | cycle fullscreen | `ToggleFullscreen` | `F11` 或 `Ctrl+F` | P1 |
| `ESC` / `ENTER` | fullscreen no / yes | `ExitFullscreen` / `ToggleFullscreen` | `Esc` / `Enter` | P1 |
| `Ctrl+⌘+T` | cycle ontop | *缺动作*（标题栏 Pin 只有鼠标） | — | P2 |
| `UP` / `DOWN` | volume ±5 | `VolumeUp5` / `VolumeDown5` | `Up` / `Down` | P0 ✅ |
| `Alt+UP/DOWN` | volume ±1 | `VolumeUp1` / `VolumeDown1` | `Alt+Up` / `Alt+Down` | P1 |
| `⌘+/` | cycle mute | `Mute` | `Ctrl+/`（另留 `M`） | P1 |
| `Z` / `X` | sub-delay ∓0.5 | `SubtitleDelayDecrease` / `SubtitleDelayIncrease` | `Z` / `X` | P0 ❌ 当前用 `J`/`K` |
| `Alt+Z` / `Alt+X` | sub-delay ∓0.1 | 同上（步长不同） | `Alt+Z` / `Alt+X` | P2（步长 0.5 vs 0.1 需决定） |
| `C` | sub-delay = 0 | `SubtitleDelayReset` | `C` | P0 ❌ 当前 `C` = 下一章 |
| `Shift+(` / `)` | audio-delay ±0.5 | *缺动作* | — | P2（建议新增） |
| `q` | quit | `Quit` | `Q` | P0 ✅ |
| `p` | cycle pause | `PlayPause` | `P` | P0 ❌ 当前 `P` = 下一首 |
| `m` | cycle mute | `Mute` | `M` | P0 ✅ |
| `f` | cycle fullscreen | `ToggleFullscreen` | `F` | P0 |
| `v` | sub-visibility | `SubtitleVisibility` | `V` | P0 ✅ |
| `G` / `F` | sub-scale ±0.1 | `SubtitleScaleIncrease` / `Decrease` | `G` / `F`（与 `f` 冲突，需定夺） | P1 |
| `r` / `R` / `t` | sub-pos | `SubtitlePositionDown` / `Up` | `R` / `T` | P1 |
| `o` / `P` | show-progress | *缺动作* | — | P2 |
| `Shift+PGUP/PGDN` | seek ±600 | *缺动作*（只有 ±5 / ±1） | — | P1（建议 `SeekForward60`/`Back60`） |
| `.` / `,` | frame step | `FrameNext` / `FramePrevious` | `.` / `,` | P2 |
| `Alt+v` | secondary sub | *缺动作* | — | P2 |
| `E` | cycle edition | *缺动作* | — | P2 |
| `Ctrl+⌘+V/S/A` | cycle video/sub/audio | *缺动作*（无轨道切换） | — | P1（建议新增三条） |
| 媒体键 `PLAY`/`NEXT`/… | — | 由 `WM_APPCOMMAND` 统一处理 | — | 已有独立通道 |

### 4.2 Wiina 现有 `IINA` 预设的 6 处偏差（`KeyBindings.cs:270-304`）

| 现绑定 | 问题 | 建议 |
|---|---|---|
| `PlayerActions.SeekBack1/Forward1` ← `Shift+Left/Right`（`:276-277`） | IINA 这两个键是**按句跳字幕** | 改成 `SubtitleSeekPrevious/Next` |
| `PlayerActions.PlaylistNext` ← `P`（`:288`） | IINA `p` = 播放/暂停；`P` = show-progress | 改为 `PlayPause` |
| `PlayerActions.ChapterNext` ← `C`（`:289`） | IINA `c` = 字幕延迟归零 | 改为 `SubtitleDelayReset` |
| `PlayerActions.Mute` ← `A`（`:285`，注释称"A is the audio mute key in IINA"） | IINA `a` 未绑定；`Ctrl+⌘+A` 是切音轨 | 删除该行（或新增 `AudioTrackNext`） |
| `SubtitleDelayDecrease/Increase` ← `J`/`K`（`:294-295`） | IINA 用 `Z`/`X` | 改为 `Z`/`X` |
| `Screenshot` ← 裸 `S`（`:286`）；`SpeedReset` ← `Backspace`（`:293`）；`ToggleFullscreen` ← `F`（`:280`） | 与 IINA 的 `⌘+S` / `⌘+\` / `f` 不完全一致 | `Ctrl+S` / `Ctrl+\` / 保留 `F` 亦可（IINA 也有 `f`） |

另外 `Default` 预设里 `Ctrl+Shift+P` 与 `Ctrl+P` 同时绑 `PictureInPicture`（`:237-238`），
`Q` 与 `Ctrl+Q` 同绑 `Quit`（`:253-254`）—— 这是"一键多绑定"，IINA 也这么做，保留即可。

### 4.3 编辑器功能对齐表

| 能力 | IINA | Wiina 现状 | 建议 |
|---|---|---|---|
| 配置集下拉 | 5 内置 + 用户集 | Default / IINA / 自定义（`KeyBindingsEditor.xaml.cs:87-125`） | 增加 mpv / VLC / Movist 预设（数据可从 `repo/iina/config/*.conf` 转写） |
| 内置集只读 | ✅ 锁 + duplicate 提示 | ⚠️ 可编辑，改动直接写 settings.json | **对标 IINA**：只读 + "复制为自定义集" |
| 搜索 | ✅ 动作名 + 原始命令 | ❌ 无 | P0 加 `AutoSuggestBox`，谓词同为"动作名 ∥ 命令文本" |
| 分类分组 | ❌ 扁平表 | ✅ `PlayerActionCatalog.Categories` | 用 `ListView` 分组头（WinUI `CollectionViewSource`） |
| 录制面板 | ✅ NSAlert + KeyRecordView | ✅ 录制 + 提示文本（`:277-292`） | 加"按 Esc 取消录制"（IINA 靠 Cancel 按钮） |
| 冲突 | ❌ 无 | ✅ `FindConflict` + 弹窗（`:383-393`） | 保留；再加"冲突一览"更佳 |
| 单动作重置 | ❌ | ✅ `ResetAction`（`:463`） | 保留 |
| JSON 导入/导出 | 仅导入 conf | ✅ `ExportJson` / `TryImportJson` | 保留；可加"导入 mpv .conf"（需解析器） |
| 危险键拦截 | 部分（`#`/空格转义） | `KeyChord` 规范化 | 增加 `Win+` 组合拦截与"已被系统占用"提示 |

### 4.4 给 editor 实现者的最小行动清单

1. **P0** 修正 `CreateIinaDefaults()` 的 6 处偏差（§4.2），并加一个单元测试：
   `CreateIinaDefaults()` 与 `repo/iina/config/iina-default-input.conf` 的人工对照表逐条比对
   （测试放 `tests/Wiina.Core.Tests`，纯数据断言即可）。
2. **P0** 加搜索框（动作名 + 键位文本双向匹配），输入即过滤，空查询显示全部。
3. **P1** 按 `Category` 分组显示；分组头用 `CategoryOf()` 的中文名。
4. **P1** 锁住内置预设：选中 `Default`/`IINA` 时录制与删除禁用，点击弹"先复制为自定义集"。
5. **P2** 导入 mpv `input.conf`：解析 `#@iina` 与 `Ctrl+Alt+X key` 两种行，未知命令进"未映射命令"只读区。

---

## 5. 证据索引

| 主题 | 文件:行 |
|---|---|
| 默认键位表 | `repo/iina/config/iina-default-input.conf:1-126` |
| 5 套内置预设 | `repo/iina/SettingsPageKeyBindings.swift:90-93`、`repo/iina/PrefKeyBindingViewController.swift:34-37` |
| `#@iina` 前缀解析 | `repo/iina/KeyMapping.swift:11-12`、`repo/iina/KeyBindingTranslator.swift:125,164`、`SettingsPageKeyBindings.swift:387-388` |
| 编辑器页面/搜索/添加 | `SettingsPageKeyBindings.swift:54,140-141,195-202` |
| 配置集菜单 | `SettingsPageKeyBindings.swift:266-271,301-313` |
| 只读预设语义 | `SettingsPageKeyBindings.swift:382,411,425-427,453-455,474-476,548` |
| 录制弹窗 | `SettingsPageKeyBindings.swift:431-449`、`KeyRecordViewController.swift:18,23,110-122,126-134`、`KeyRecordView.swift:37-63` |
| 命令树选择器 | `repo/iina/KeyBindingDataLoader.swift:19-70` |
| conf 生成/序列化 | `repo/iina/KeyMapping.swift:178-181` |
| 键位规范化 | `repo/iina/KeyCodeHelper.swift:15-27,264-302,304-345,350-356,358-436` |
| 无冲突检测 | 全仓库 `conflict` 仅 `MenuController.swift:687`（插件） |
| Wiina 动作目录 | `src/Wiina.Core/Input/KeyBindings.cs:33-89` |
| Wiina 默认/IINA 预设 | `src/Wiina.Core/Input/KeyBindings.cs:202-263 / 270-304` |
| Wiina 冲突/导入导出 | `src/Wiina.Core/Input/KeyBindings.cs:392,424,451,463,505,520` |
| Wiina 录制/冲突弹窗 | `src/Wiina.App/Controls/KeyBindingsEditor.xaml.cs:277-393` |
