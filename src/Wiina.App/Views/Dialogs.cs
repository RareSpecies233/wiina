using System.Collections.Generic;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Wiina.App.Services;
using Wiina.Core.Settings;
using Wiina.Core.Text;

namespace Wiina.App.Views;

internal sealed record OpenUrlRequest(string Url, bool NewWindow);

/// <summary>
/// Small WinUI dialogs shared by the welcome window, the settings window and
/// the player: Open URL (validated), subtitle font picker and confirmations.
/// </summary>
internal static class Dialogs
{
    public static async Task<OpenUrlRequest?> OpenUrlAsync(
        XamlRoot xamlRoot,
        AppSettings settings)
    {
        var input = new TextBox
        {
            Text = settings.RecentUrls.FirstOrDefault() ?? string.Empty,
            PlaceholderText = "https://example.com/stream.m3u8",
            MinWidth = 380,
        };
        var recent = new ComboBox
        {
            ItemsSource = settings.RecentUrls.ToArray(),
            PlaceholderText = "最近打开的地址",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var newWindow = new CheckBox { Content = "在新窗口打开" };
        var error = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0x6C, 0x5A)),
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(input);
        if (settings.RecentUrls.Count > 0)
        {
            panel.Children.Add(recent);
        }

        panel.Children.Add(error);
        panel.Children.Add(newWindow);

        var dialog = new ContentDialog
        {
            Title = "打开 URL",
            Content = panel,
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = xamlRoot,
        };

        string? normalized = null;
        void Validate()
        {
            if (UrlValidation.TryNormalize(input.Text, out string value))
            {
                normalized = value;
                error.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
            }
            else
            {
                normalized = null;
                error.Text = input.Text.Trim().Length == 0
                    ? "请输入 URL。"
                    : "不是有效的媒体 URL（支持 http/https/rtmp/rtsp/ftp 等）。";
                error.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
            }
        }

        input.TextChanged += (_, _) => Validate();
        recent.SelectionChanged += (_, _) =>
        {
            if (recent.SelectedItem is string selected)
            {
                input.Text = selected;
            }
        };
        Validate();

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || normalized is null)
        {
            return null;
        }

        return new OpenUrlRequest(normalized, newWindow.IsChecked == true);
    }

    public static async Task<string?> PickFontAsync(XamlRoot xamlRoot, string current)
    {
        var fonts = new List<string>(FontCatalog.Names);
        if (!fonts.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            fonts.Insert(0, current);
        }

        var list = new ListView
        {
            ItemsSource = fonts,
            Height = 240,
            SelectionMode = ListViewSelectionMode.Single,
            SelectedItem = fonts.FirstOrDefault(font =>
                string.Equals(font, current, StringComparison.OrdinalIgnoreCase)),
        };
        var search = new AutoSuggestBox
        {
            PlaceholderText = "筛选字体",
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        var preview = new TextBlock
        {
            Text = "字幕预览 · Subtitle 123",
            FontSize = 22,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(current),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var previewBorder = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = preview,
        };
        var note = new TextBlock
        {
            Text = "列表为常用字体；也可以在输入框中直接填写已安装的字体家族名。完整 DirectWrite 枚举在 Stage 11 补充。",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };

        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(search);
        panel.Children.Add(list);
        panel.Children.Add(previewBorder);
        panel.Children.Add(note);

        search.TextChanged += (_, args) =>
        {
            string query = args.Reason == AutoSuggestionBoxTextChangeReason.UserInput
                ? search.Text.Trim()
                : string.Empty;
            list.ItemsSource = string.IsNullOrEmpty(query)
                ? fonts
                : fonts.Where(font => font.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string font)
            {
                preview.FontFamily = new FontFamily(font);
            }
        };
        search.QuerySubmitted += (_, args) =>
        {
            string text = FontNameValidation.Normalize(args.QueryText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (!fonts.Contains(text, StringComparer.OrdinalIgnoreCase))
                {
                    fonts.Insert(0, text);
                }

                list.SelectedItem = text;
            }
        };

        var dialog = new ContentDialog
        {
            Title = "选择字幕字体",
            Content = panel,
            PrimaryButtonText = "使用此字体",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        string chosen = list.SelectedItem as string ??
            (string.IsNullOrWhiteSpace(search.Text) ? current : search.Text);
        return FontNameValidation.Normalize(chosen);
    }

    public static async Task<bool> ConfirmAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        string primaryText = "继续")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            },
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowMessageAsync(
        XamlRoot xamlRoot,
        string title,
        string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
            },
            CloseButtonText = "关闭",
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }
}

/// <summary>
/// Curated font list for the subtitle font picker. The list is editable, so any
/// installed family name is accepted; full enumeration needs DirectWrite
/// (documented Stage 11 gap).
/// </summary>
internal static class FontCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> Cached = new(Build);

    public static IReadOnlyList<string> Names => Cached.Value;

    private static IReadOnlyList<string> Build()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Segoe UI",
            "Segoe UI Variable Text",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "DengXian",
            "SimSun",
            "SimHei",
            "KaiTi",
            "FangSong",
            "Source Han Sans SC",
            "Noto Sans CJK SC",
            "Arial",
            "Calibri",
            "Cambria",
            "Cascadia Mono",
            "Consolas",
            "Tahoma",
            "Times New Roman",
            "Verdana",
        };

        try
        {
            var group = new Windows.Globalization.Fonts.LanguageFontGroup("zh-Hans-CN");
            Windows.Globalization.Fonts.LanguageFont[] fonts =
            {
                group.UICaptionFont,
                group.UITextFont,
                group.UITitleFont,
                group.DocumentHeadingFont,
                group.ModernDocumentFont,
            };
            foreach (Windows.Globalization.Fonts.LanguageFont? font in fonts)
            {
                if (font is not null && !string.IsNullOrWhiteSpace(font.FontFamily))
                {
                    names.Add(font.FontFamily);
                }
            }
        }
        catch
        {
            // LanguageFontGroup is available on Windows 10+, but a locked-down
            // environment may throw; the curated list still works.
        }

        return names.ToArray();
    }
}
