$ErrorActionPreference='Stop'
$patches=@{
'src\Wiina.App\Views\FiltersWindow.xaml.cs'=@(
@('Title = "Wiina 滤镜与均衡器";','Title = Loc.T("filters.windowTitle");'),
@('SetStatus("就绪：所有更改即时写入 mpv。");','SetStatus(Loc.T("filters.ready"));'),
@('TitleText.Text = audio ? "音频滤镜与均衡器" : "视频滤镜";','TitleText.Text = audio ? Loc.T("filters.audioTitle") : Loc.T("filters.videoTitle");'),
@('filter.Enabled ? string.Empty : "已停用"))','filter.Enabled ? string.Empty : Loc.T("filters.disabled")))'),
@('VideoCountText.Text = $"{_videoRows.Count} 个";','VideoCountText.Text = Loc.F("filters.count", _videoRows.Count);'),
@('Text = "选择一个滤镜以编辑参数。",','Text = Loc.T("filters.pickFilter"),'),
@('Header = "像素格式",','Header = Loc.T("filters.paramFormat"),'),
@('Header = "启用此滤镜",','Header = Loc.T("filters.enableFilter"),'),
@('OnContent = "启用",','OnContent = Loc.T("filters.enable"),'),
@('OffContent = "停用",','OffContent = Loc.T("filters.disable"),'),
@('row.State = filter.Enabled ? string.Empty : "已停用";','row.State = filter.Enabled ? string.Empty : Loc.T("filters.disabled");'),
@('AudioCountText.Text = $"{_audioRows.Count} 个";','AudioCountText.Text = Loc.F("filters.count", _audioRows.Count);'),
@('Text = "选择一个附加音频滤镜以编辑参数。",','Text = Loc.T("filters.pickAudioFilter"),'),
@('new FilterParameter("I", "目标响度"','new FilterParameter("I", Loc.T("filters.paramLoudness")'),
@('new FilterParameter("TP", "真峰值"','new FilterParameter("TP", Loc.T("filters.paramTruePeak")'),
@('new FilterParameter("LRA", "响度范围"','new FilterParameter("LRA", Loc.T("filters.paramLoudnessRange")'),
@('new FilterParameter("f", "帧长"','new FilterParameter("f", Loc.T("filters.paramFrameLength")'),
@('new FilterParameter("g", "高斯窗口"','new FilterParameter("g", Loc.T("filters.paramGaussianWindow")'),
@('new FilterParameter("p", "峰值"','new FilterParameter("p", Loc.T("filters.paramPeak")'),
@('new FilterParameter("m", "最大增益"','new FilterParameter("m", Loc.T("filters.paramMaxGain")'),
@('SetStatus("请输入预设名称。", error: true);','SetStatus(Loc.T("filters.enterPreset"), error: true);'),
@('SetStatus($"已保存视频滤镜预设“{name}”。");','SetStatus(Loc.F("filters.videoPresetSaved", name));'),
@('SetStatus("请选择一个视频滤镜预设。", error: true);','SetStatus(Loc.T("filters.pickVideoPreset"), error: true);'),
@('SetStatus($"已载入视频滤镜预设“{name}”。");','SetStatus(Loc.F("filters.videoPresetLoaded", name));'),
@('SetStatus($"已删除视频滤镜预设“{name}”。");','SetStatus(Loc.F("filters.videoPresetDeleted", name));'),
@('SetStatus($"已保存音频预设“{name}”。");','SetStatus(Loc.F("filters.audioPresetSaved", name));'),
@('SetStatus("请选择一个音频预设。", error: true);','SetStatus(Loc.T("filters.pickAudioPreset"), error: true);'),
@('SetStatus($"已载入音频预设“{name}”。");','SetStatus(Loc.F("filters.audioPresetLoaded", name));'),
@('SetStatus($"已删除音频预设“{name}”。");','SetStatus(Loc.F("filters.audioPresetDeleted", name));'),
@('string summary = $"已应用：视频 {videoCount} 项 · 均衡器{(_settings.EqualizerEnabled ? "开启" : "关闭")} · 附加音频 {audioCount} 项";','string summary = Loc.F("filters.appliedSummary", videoCount, _settings.EqualizerEnabled ? Loc.T("filters.on") : Loc.T("filters.off"), audioCount);')
);
'src\Wiina.App\Views\WelcomeWindow.xaml.cs'=@(
@('Title = "欢迎使用 Wiina";','Title = Loc.T("welcome.title");'),
@('VersionText.Text = "版本 " + Branding.Version;','VersionText.Text = Loc.F("welcome.version", Branding.Version);'),
@('ResumePosition.Text = $"上次看到 {TimeSpan.FromSeconds(_resume.Position):mm\\:ss} / {TimeSpan.FromSeconds(_resume.Duration):mm\\:ss}";','ResumePosition.Text = Loc.F("welcome.resume", TimeSpan.FromSeconds(_resume.Position).ToString("mm\\:ss"), TimeSpan.FromSeconds(_resume.Duration).ToString("mm\\:ss"));'),
@('"清除最近记录",','Loc.T("welcome.clearRecentTitle"),'),
@('"将从欢迎窗口和“打开最近”菜单中移除全部记录。",','Loc.T("welcome.clearRecentBody"),'),
@('"清除"))','Loc.T("welcome.clear")))'),
@('e.DragUIOverride.Caption = "用 Wiina 打开";','e.DragUIOverride.Caption = Loc.T("welcome.dragCaption");')
);
'src\Wiina.App\Views\AboutWindow.xaml.cs'=@(
@('Title = "关于 Wiina";','Title = Loc.T("about.title");'),
@('VersionText.Text = $"版本 {Branding.Version} · {Branding.Tagline}";','VersionText.Text = Loc.F("about.version", Branding.Version, Branding.Tagline);'),
@('Add("许可证", "GNU GPL-3.0-only（含无担保声明）");','Add(Loc.T("about.license"), Loc.T("about.licenseValue"));'),
@('Add("播放引擎", $"mpv / libmpv（客户端 API {MpvRuntime.ClientApiVersion}）");','Add(Loc.T("about.engine"), $"mpv / libmpv (client API {MpvRuntime.ClientApiVersion})");'),
@('Add("界面", $"WinUI 3 / Windows App SDK {WinUiVersion()}");','Add(Loc.T("about.ui"), $"WinUI 3 / Windows App SDK {WinUiVersion()}");'),
@('Add("运行时", $".NET {Environment.Version} · {Environment.OSVersion.VersionString}");','Add(Loc.T("about.runtime"), $".NET {Environment.Version} · {Environment.OSVersion.VersionString}");'),
@('Add("项目主页", Branding.ProjectUrl);','Add(Loc.T("about.homepage"), Branding.ProjectUrl);'),
@('Add("声明", Branding.Disclaimer);','Add(Loc.T("about.disclaimer"), Branding.Disclaimer);'),
@('Add("字体/图标", "Segoe UI · Segoe Fluent Icons（Microsoft 许可，未使用 SF Symbols）");','Add(Loc.T("about.fonts"), Loc.T("about.fontsValue"));'),
@('Add("说明", "Wiina 为原创实现；未复制 IINA 的源码、图片或应用图标。");','Add(Loc.T("about.note"), Loc.T("about.noteValue"));'),
@('return assembly.GetName().Version?.ToString(3) ?? "未知";','return assembly.GetName().Version?.ToString(3) ?? Loc.T("about.unknown");'),
@('return "未知";','return Loc.T("about.unknown");')
);
'src\Wiina.App\Views\LogViewerWindow.xaml.cs'=@(
@('Title = "Wiina 日志";','Title = Loc.T("log.title");'),
@('FilterBox.ItemsSource = new[] { "全部", "跟踪日志", "崩溃日志" };','FilterBox.ItemsSource = new[] { Loc.T("log.filterAll"), Loc.T("log.filterTrace"), Loc.T("log.filterCrash") };'),
@('_ => $"== 跟踪日志：{trace} =={Environment.NewLine}{ReadTail(trace)}{Environment.NewLine}"','_ => Loc.F("log.traceHeader", trace) + $"{Environment.NewLine}{ReadTail(trace)}{Environment.NewLine}"'),
@('+ $"== 崩溃日志：{crash} =={Environment.NewLine}{ReadTail(crash)}",','+ Loc.F("log.crashHeader", crash) + $"{Environment.NewLine}{ReadTail(crash)}",'),
@('return "（文件不存在）";','return Loc.T("log.missing");'),
@('return start > 0 ? "…（已截断较早内容）\\n" + text : text;','return start > 0 ? Loc.T("log.truncated") + "\\n" + text : text;'),
@('return "读取失败：" + ex.Message;','return Loc.F("log.readFailed", ex.Message);')
);
'src\Wiina.App\Views\InspectorWindow.xaml.cs'=@(
@('Title = "Wiina 检查器";','Title = Loc.T("inspector.title");'),
@('TitleText.Text = "检查器 — 没有活动的播放窗口";','TitleText.Text = Loc.T("inspector.noWindow");'),
@('Text = "打开一个播放窗口后，这里会显示文件、视频、音频、字幕与播放状态的技术属性。",','Text = Loc.T("inspector.noWindowBody"),'),
@('TitleText.Text = "检查器 — " + (window.CurrentTitle ?? "未命名");','TitleText.Text = Loc.T("inspector.title") + " — " + (window.CurrentTitle ?? Loc.T("inspector.untitled"));'),
@('Text = "当前没有媒体，或播放引擎尚未启动。",','Text = Loc.T("inspector.noMedia"),')
);
'src\Wiina.App\Controls\QuickSettingsView.xaml.cs'=@(
@('ThemeBox.ItemsSource = new[] { "跟随系统", "深色", "浅色" };','ThemeBox.ItemsSource = new[] { Loc.T("quick.themeSystem"), Loc.T("quick.themeDark"), Loc.T("quick.themeLight") };'),
@('LayoutBox.ItemsSource = new[] { "Edge-to-edge（覆盖视频）", "Docked（视频让位）" };','LayoutBox.ItemsSource = new[] { Loc.T("quick.layoutEdge"), Loc.T("quick.layoutDocked") };'),
@('OscPositionBox.ItemsSource = new[] { "浮动", "底部", "顶部" };','OscPositionBox.ItemsSource = new[] { Loc.T("quick.oscFloating"), Loc.T("quick.oscBottom"), Loc.T("quick.oscTop") };'),
@('OsdPositionBox.ItemsSource = new[] { "右下", "左下", "右上" };','OsdPositionBox.ItemsSource = new[] { Loc.T("quick.osdBottomRight"), Loc.T("quick.osdBottomLeft"), Loc.T("quick.osdTopRight") };'),
@('SidebarSideBox.ItemsSource = new[] { "左侧（leading）", "右侧（trailing）" };','SidebarSideBox.ItemsSource = new[] { Loc.T("quick.sidebarLeft"), Loc.T("quick.sidebarRight") };'),
@('? "libmpv 未报告音频输出设备。"','? Loc.T("quick.noAudioDevices")'),
@(': $"共 {devices.Count} 个设备；选择后立即切换。";',': Loc.F("quick.audioDevices", devices.Count);'),
@('new(-2, "自动（默认）"),','new(-2, Loc.T("quick.autoDefault")),'),
@('new(-1, "关闭") };','new(-1, Loc.T("menu.off")) };'),
@('var choices = new List<TrackChoice> { new(-1, "关闭") };','var choices = new List<TrackChoice> { new(-1, Loc.T("menu.off")) };'),
@('AudioDelayText.Text = $"{AudioDelaySlider.Value:+0.0;-0.0;0.0} 秒";','AudioDelayText.Text = Loc.F("quick.seconds", $"{AudioDelaySlider.Value:+0.0;-0.0;0.0}");'),
@('SubtitleDelayText.Text = $"{SubtitleDelaySlider.Value:+0.0;-0.0;0.0} 秒";','SubtitleDelayText.Text = Loc.F("quick.seconds", $"{SubtitleDelaySlider.Value:+0.0;-0.0;0.0}");')
);
'src\Wiina.App\Controls\PlaylistView.xaml.cs'=@(
@('? "0 项"','? Loc.T("playlist.items0")'),
@(': $"{items.Count} 项 · 第 {Math.Clamp(currentIndex + 1, 0, items.Count)} 项";',': Loc.F("playlist.items", items.Count, Math.Clamp(currentIndex + 1, 0, items.Count));'),
@(': $"{_rows.Count} 项 · 第 {Math.Clamp(currentIndex + 1, 0, _rows.Count)} 项";',': Loc.F("playlist.items", _rows.Count, Math.Clamp(currentIndex + 1, 0, _rows.Count));'),
@('Add("播放", () => PlayRequested?.Invoke(this, row.Index));','Add(Loc.T("playlist.play"), () => PlayRequested?.Invoke(this, row.Index));'),
@('Add("上移", () => MoveFirstSelected(-1));','Add(Loc.T("playlist.moveUp"), () => MoveFirstSelected(-1));'),
@('Add("下移", () => MoveFirstSelected(1));','Add(Loc.T("playlist.moveDown"), () => MoveFirstSelected(1));'),
@('Add("移除", RemoveSelected);','Add(Loc.T("playlist.remove"), RemoveSelected);'),
@('Add("复制路径", () => CopyPath(row.Path));','Add(Loc.T("playlist.copyPath"), () => CopyPath(row.Path));')
);
'src\Wiina.App\Controls\ChaptersView.xaml.cs'=@(
@('CountText.Text = $"{chapters.Count} 个";','CountText.Text = Loc.F("chapters.count", chapters.Count);')
);
'src\Wiina.App\Controls\MiniPlayerView.xaml.cs'=@(
@('VolumeText.Text = muted ? "静音" : $"{Math.Round(VolumeSlider.Value)}%";','VolumeText.Text = muted ? Loc.T("mini.muted") : $"{Math.Round(VolumeSlider.Value)}%";'),
@('VolumeText.Text = _muted ? "静音" : $"{Math.Round(clamped)}%";','VolumeText.Text = _muted ? Loc.T("mini.muted") : $"{Math.Round(clamped)}%";')
);
'src\Wiina.App\Controls\SidebarHost.xaml.cs'=@(
@('ToolTipService.SetToolTip(CloseButton, "关闭侧边栏");','ToolTipService.SetToolTip(CloseButton, Loc.T("sidebar.close"));')
);
'src\Wiina.App\PipWindow.xaml.cs'=@(
@('AppWindow.Title = "画中画 — Wiina";','AppWindow.Title = Loc.T("pip.title");'),
@('Title = "画中画 — Wiina";','Title = Loc.T("pip.title");'),
@('string resolved = string.IsNullOrWhiteSpace(title) ? "画中画 — Wiina" : title;','string resolved = string.IsNullOrWhiteSpace(title) ? Loc.T("pip.title") : title;')
)
}
foreach($f in $patches.Keys){
  $s=[IO.File]::ReadAllText($f)
  foreach($pr in $patches[$f]){ if(-not $s.Contains($pr[0])){ throw "$f missing $($pr[0])" }; $s=$s.Replace($pr[0],$pr[1]) }
  if($s -notmatch 'using Wiina\.App\.Localization;'){ $s=$s -replace '(using Wiina\.App\.Services;)', "using Wiina.App.Localization;`r`n`$1"; if($s -notmatch 'using Wiina\.App\.Localization;'){ $s=$s -replace '(using Wiina\.Core[^;]*;)', "`$1`r`nusing Wiina.App.Localization;" } }
  if($s -notmatch 'using Wiina\.App\.Localization;'){ $s="using Wiina.App.Localization;`r`n"+$s }
  [IO.File]::WriteAllText($f,$s,(New-Object Text.UTF8Encoding($false)))
}
'views patched'