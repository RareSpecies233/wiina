$ErrorActionPreference = 'Stop'
$p = 'C:\Users\zheng\Desktop\wiina\src\Wiina.App\Views\SettingsWindow.xaml.cs'
$s = [System.IO.File]::ReadAllText($p)
function Rep([string]$a, [string]$b) {
    if (-not $script:s.Contains($a)) { throw "missing pattern: $a" }
    $script:s = $script:s.Replace($a, $b)
}

Rep 'using Wiina.App.Interop;
using Wiina.App.Services;' 'using Wiina.App.Interop;
using Wiina.App.Localization;
using Wiina.App.Services;'
Rep 'using Wiina.Core.Input;
using Wiina.Core.Models;' 'using Wiina.Core.Input;
using Wiina.Core.Localization;
using Wiina.Core.Models;'

Rep '        Title = "Wiina 设置";
        _currentPage = page;' '        Title = Loc.T("ui.wiinaSettings");
        _currentPage = page;'

Rep '        App.SettingsChanged += OnSettingsChanged;
        Closed += OnClosed;' '        App.SettingsChanged += OnSettingsChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;'

Rep '            content.Children.Add(new TextBlock
            {
                Text = page.Title,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button' '            string pageTitle = SettingsLocalization.PageTitle(page, Loc.CurrentLanguage);
            content.Children.Add(new TextBlock
            {
                Text = pageTitle,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button'
Rep '            SettingsPageId id = page.Id;
            button.Click += (_, _) => Navigate(id);' '            SettingsPageId id = page.Id;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, pageTitle);
            button.Click += (_, _) => Navigate(id);'

Rep '        SettingsPageInfo info = SettingsCatalog.Page(page);
        PageTitle.Text = info.Title;
        PageSummary.Text = info.Summary;' '        SettingsPageInfo info = SettingsCatalog.Page(page);
        PageTitle.Text = SettingsLocalization.PageTitle(info, Loc.CurrentLanguage);
        PageSummary.Text = SettingsLocalization.PageSummary(info, Loc.CurrentLanguage);'

Rep '        var text = new TextBlock
        {
            Text = section,
            FontSize = 12,' '        var text = new TextBlock
        {
            Text = SettingsLocalization.Section(section, Loc.CurrentLanguage),
            FontSize = 12,'

Rep '            content.Children.Add(new TextBlock
            {
                Text = section,
                FontSize = 11,
                Opacity = 0.75,' '            content.Children.Add(new TextBlock
            {
                Text = SettingsLocalization.Section(section, Loc.CurrentLanguage),
                FontSize = 11,
                Opacity = 0.75,'
Rep '            string target = section;
            button.Click += (_, _) => ScrollToSection(target);' '            string target = section;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                button,
                SettingsLocalization.Section(section, Loc.CurrentLanguage));
            button.Click += (_, _) => ScrollToSection(target);'

Rep '        textStack.Children.Add(new TextBlock
        {
            Text = row.Title,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        string description = row.Description;
        if (!row.IsAvailable)
        {
            description += $"\n不可用：{row.UnavailableReason}";
        }' '        string rowTitle = SettingsLocalization.Title(row, Loc.CurrentLanguage);
        textStack.Children.Add(new TextBlock
        {
            Text = rowTitle,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        string description = SettingsLocalization.Description(row, Loc.CurrentLanguage);
        if (!row.IsAvailable)
        {
            description += "\n" + Loc.F(
                "settings.unavailable",
                SettingsLocalization.UnavailableReason(row.UnavailableReason, Loc.CurrentLanguage));
        }'

Rep '                    ItemsSource = row.Choices,
                    SelectedIndex = Math.Clamp(' '                    ItemsSource = SettingsLocalization.Choices(row, Loc.CurrentLanguage),
                    SelectedIndex = Math.Clamp('

Rep '                    Content = "打开按键绑定",' '                    Content = Loc.T("settings.openKeyBindings"),'
Rep '                    Text = "（未安装插件）",' '                    Text = Loc.T("settings.noPlugins"),'
Rep '                    Content = "安装插件…（Stage 9）",' '                    Content = Loc.T("settings.installPlugin"),'

Rep '    private static string ActionVerb(string? actionId) => actionId switch
    {
        "choose-screenshot-folder" => "选择…",
        "open-data-folder" or "open-config-folder" or "open-log-folder" or
            "open-plugin-folder" => "打开",
        "reset-video" or "reset-audio" or "reset-subtitles" or "reset-control" or
            "reset-all-settings" => "重置",
        "clear-history" or "clear-thumbnails" => "清空",
        "export-diagnostics" or "open-keybindings" => actionId == "export-diagnostics"
            ? "导出"
            : "打开",
        "install-plugin" => "安装",
        "open-url-dialog" or "open-log-viewer" or "open-inspector" or "open-about" or
            "open-video-filters" or "open-audio-filters" => "打开",
        "reset-equalizer" => "重置",
        "toggle-technical-osd" => "切换",
        _ => "执行",
    };' '    private static string ActionVerb(string? actionId) => actionId switch
    {
        "choose-screenshot-folder" => Loc.T("settings.choose"),
        "open-data-folder" or "open-config-folder" or "open-log-folder" or
            "open-plugin-folder" => Loc.T("settings.open"),
        "reset-video" or "reset-audio" or "reset-subtitles" or "reset-control" or
            "reset-all-settings" => Loc.T("settings.reset"),
        "clear-history" or "clear-thumbnails" => Loc.T("settings.clear"),
        "export-diagnostics" or "open-keybindings" => actionId == "export-diagnostics"
            ? Loc.T("settings.export")
            : Loc.T("settings.open"),
        "install-plugin" => Loc.T("settings.install"),
        "open-url-dialog" or "open-log-viewer" or "open-inspector" or "open-about" or
            "open-video-filters" or "open-audio-filters" => Loc.T("settings.open"),
        "reset-equalizer" => Loc.T("settings.reset"),
        "toggle-technical-osd" => Loc.T("settings.toggle"),
        _ => Loc.T("settings.run"),
    };'

Rep '        "advanced.renderer" => App.ActiveWindow?.GetRendererInfo() ?? "没有活动的播放窗口",' '        "advanced.renderer" => App.ActiveWindow?.GetRendererInfo() ?? Loc.T("settings.noActiveWindow"),'

Rep '            StatusText.Text = "正在编辑…";' '            StatusText.Text = Loc.T("settings.editing");'
Rep '            StatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";' '            StatusText.Text = Loc.F("settings.saved", DateTime.Now.ToString("HH:mm:ss"));'
Rep '                _bindingStatus.Text = "按键绑定已保存并立即生效。";' '                _bindingStatus.Text = Loc.T("keybindings.saved");'
Rep '            StatusText.Text = "保存失败：" + ex.Message;' '            StatusText.Text = Loc.F("settings.saveFailed", ex.Message);'

Rep '            SettingsCatalog.SearchIndex.Search(query, 12);' '            SettingsCatalog.SearchIndex.Search(query, 12, Loc.CurrentLanguage);'

Rep '                    StatusText.Text = "诊断信息已导出：" + path;' '                    StatusText.Text = Loc.F("settings.exported", path);'
Rep '                            "清空播放历史",
                            "将删除最近文件列表与断点续播记录。此操作不可撤销。",
                            "清空"))' '                            Loc.T("settings.clearHistoryTitle"),
                            Loc.T("settings.clearHistoryBody"),
                            Loc.T("settings.clear")))'
Rep '                        StatusText.Text = "播放历史已清空。";' '                        StatusText.Text = Loc.T("settings.historyCleared");'
Rep '                    StatusText.Text = "缩略图缓存已清空。";' '                    StatusText.Text = Loc.T("settings.thumbCacheCleared");'
Rep '                            "重置所有设置",
                            "所有页面恢复默认值（按键绑定、窗口位置与播放历史不受影响）。",
                            "重置"))' '                            Loc.T("settings.resetAllTitle"),
                            Loc.T("settings.resetAllBody"),
                            Loc.T("settings.reset")))'
Rep '                        StatusText.Text = "设置已恢复默认值。";' '                        StatusText.Text = Loc.T("settings.defaultsRestored");'
Rep '            StatusText.Text = "操作失败：" + ex.Message;' '            StatusText.Text = Loc.F("settings.actionFailed", ex.Message);'

Rep '        AddToolbarButton(recordRow, "录制", StartRecording);
        AddToolbarButton(recordRow, "清除", ClearSelectedBinding);
        AddToolbarButton(recordRow, "重置此项", ResetSelectedAction);
        AddToolbarButton(recordRow, "重置全部", ResetAllBindings);' '        AddToolbarButton(recordRow, Loc.T("keybindings.record"), StartRecording);
        AddToolbarButton(recordRow, Loc.T("keybindings.clear"), ClearSelectedBinding);
        AddToolbarButton(recordRow, Loc.T("keybindings.resetOne"), ResetSelectedAction);
        AddToolbarButton(recordRow, Loc.T("keybindings.resetAll"), ResetAllBindings);'
Rep '        AddToolbarButton(ioRow, "导入…", () => _ = ImportBindingsAsync());
        AddToolbarButton(ioRow, "导出…", () => _ = ExportBindingsAsync());' '        AddToolbarButton(ioRow, Loc.T("keybindings.import"), () => _ = ImportBindingsAsync());
        AddToolbarButton(ioRow, Loc.T("keybindings.export"), () => _ = ExportBindingsAsync());'
Rep '            Text = "选择一个动作，然后点击“录制”并按下组合键。Esc 取消，Backspace 清除。",' '            Text = Loc.T("keybindings.hint"),'
Rep 'sectionContent.Children.Add(new TextBlock { Text = "快捷键", FontSize = 11, Opacity = 0.75 });' 'sectionContent.Children.Add(new TextBlock
        {
            Text = Loc.T("keybindings.section"),
            FontSize = 11,
            Opacity = 0.75,
        });'
Rep '                : "未绑定";' '                : Loc.T("keybindings.unbound");'
Rep '                Opacity = chords == "未绑定" ? 0.5 : 0.95,' '                Opacity = chords == Loc.T("keybindings.unbound") ? 0.5 : 0.95,'
Rep '                ? "选择一个动作，然后点击“录制”并按下组合键。"
                : $"已选择：{PlayerActionCatalog.Label(action)}。点击“录制”后按下新组合键；Esc 取消，Backspace 清除。";' '                ? Loc.T("keybindings.hint")
                : Loc.F("keybindings.hintSelected", PlayerActionCatalog.Label(action));'
Rep '                _bindingStatus.Text = "请先选择一个动作。";' '                _bindingStatus.Text = Loc.T("keybindings.pickFirst");'
Rep '            _recorderText.Text =
                $"正在录制：{PlayerActionCatalog.Label(_selectedAction)} — 请按下组合键（Esc 取消，Backspace 清除）。";' '            _recorderText.Text =
                Loc.F("keybindings.recording", PlayerActionCatalog.Label(_selectedAction));'
Rep '            _recorderText.Text =
                $"已选择：{PlayerActionCatalog.Label(_selectedAction)}。点击“录制”后按下新组合键。";' '            _recorderText.Text =
                Loc.F("keybindings.selectedAfter", PlayerActionCatalog.Label(_selectedAction));'
Rep '            _recorderText.Text = "选择一个动作，然后点击“录制”并按下组合键。";' '            _recorderText.Text = Loc.T("keybindings.hint");'

Rep '                "快捷键冲突",
                $"{chord} 已被“{PlayerActionCatalog.Label(conflict)}”使用。是否替换为“{PlayerActionCatalog.Label(action)}”？",
                "替换");' '                Loc.T("keybindings.conflictTitle"),
                Loc.F(
                    "keybindings.conflictBody",
                    chord,
                    PlayerActionCatalog.Label(conflict),
                    PlayerActionCatalog.Label(action)),
                Loc.T("keybindings.replace"));'
Rep '            _bindingStatus.Text =
                $"“{PlayerActionCatalog.Label(action)}”已绑定到 {chord}。";' '            _bindingStatus.Text =
                Loc.F("keybindings.bound", PlayerActionCatalog.Label(action), chord);'
Rep '            _bindingStatus.Text = $"已清除“{PlayerActionCatalog.Label(action)}”的快捷键。";' '            _bindingStatus.Text = Loc.F(
                "keybindings.clearedAction",
                PlayerActionCatalog.Label(action));'
Rep '            _bindingStatus.Text = $"“{PlayerActionCatalog.Label(action)}”已恢复默认快捷键。";' '            _bindingStatus.Text = Loc.F(
                "keybindings.resetOneAction",
                PlayerActionCatalog.Label(action));'
Rep '                "重置全部按键",
                "所有动作恢复文档默认快捷键。",
                "重置"))' '                Loc.T("keybindings.resetAllTitle"),
                Loc.T("keybindings.resetAllBody"),
                Loc.T("settings.reset")))'
Rep '            _bindingStatus.Text = "已恢复全部默认快捷键。";' '            _bindingStatus.Text = Loc.T("keybindings.resetAllDone");'
Rep '            await Dialogs.ShowMessageAsync(RootGrid.XamlRoot, "导入失败", error);' '            await Dialogs.ShowMessageAsync(
                RootGrid.XamlRoot,
                Loc.T("keybindings.importFailed"),
                error);'
Rep '                ? $"已导入 {imported.Count} 条绑定。"
                : $"已导入 {imported.Count} 条绑定；{error}";' '                ? Loc.F("keybindings.imported", imported.Count)
                : Loc.F("keybindings.importedPartial", imported.Count, error);'
Rep '            _bindingStatus.Text = "已导出 " + file.Name;' '            _bindingStatus.Text = Loc.F("keybindings.exported", file.Name);'

Rep '    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.SettingsChanged -= OnSettingsChanged;' '    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Title = Loc.T("ui.wiinaSettings");
            BuildNavigation();
            Navigate(_currentPage);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        App.SettingsChanged -= OnSettingsChanged;
        Loc.LanguageChanged -= OnLanguageChanged;'

[System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "patched"
