using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Wiina.Core.Localization;

namespace Wiina.App.Localization;

/// <summary>
/// Runtime localization entry point. XAML uses the attached properties below
/// (<c>loc:Loc.Text="ui.settings"</c>) because WinUI 3 has no custom markup
/// extensions. Values are re-applied on <see cref="LanguageChanged"/>, but only
/// while the target still holds the value we last wrote, so dynamic text such as
/// the status line or a track name is never clobbered.
/// </summary>
public static class Loc
{
    private sealed record Entry(string Key, string Value);

    private sealed class State
    {
        public Dictionary<string, Entry> Properties { get; } = new(StringComparer.Ordinal);
    }

    private static readonly ConditionalWeakTable<DependencyObject, State> Tracked = new();
    private static readonly List<WeakReference<DependencyObject>> Targets = new();
    private static string _currentLanguage = AppLanguage.ZhHans;

    public static string CurrentLanguage => _currentLanguage;

    public static event EventHandler? LanguageChanged;

    /// <summary>Resolves a preference against the Windows display languages.</summary>
    public static void Initialize(string? preference)
    {
        string resolved = AppLanguage.Resolve(
            preference,
            Windows.Globalization.ApplicationLanguages.Languages);
        if (!string.Equals(resolved, _currentLanguage, StringComparison.Ordinal))
        {
            _currentLanguage = resolved;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        ReapplyAll();
    }

    /// <summary>Switches the UI language immediately; callers persist the setting.</summary>
    public static void ApplyPreference(string? preference)
    {
        string resolved = AppLanguage.Resolve(
            preference,
            Windows.Globalization.ApplicationLanguages.Languages);
        if (string.Equals(resolved, _currentLanguage, StringComparison.Ordinal))
        {
            return;
        }

        _currentLanguage = resolved;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
        ReapplyAll();
    }

    public static string T(string key) => UiStrings.Get(key, _currentLanguage);

    public static string F(string key, params object?[] args) =>
        UiStrings.Format(key, _currentLanguage, args);

    /// <summary>Language a new value should be written in.</summary>
    public static string Language => _currentLanguage;

    private static void ReapplyAll()
    {
        for (int i = Targets.Count - 1; i >= 0; i--)
        {
            if (!Targets[i].TryGetTarget(out DependencyObject? target))
            {
                Targets.RemoveAt(i);
                continue;
            }

            if (!Tracked.TryGetValue(target, out State? state))
            {
                continue;
            }

            foreach ((string slot, Entry entry) in state.Properties)
            {
                string fresh = UiStrings.Get(entry.Key, _currentLanguage);
                if (string.Equals(Read(target, slot), entry.Value, StringComparison.Ordinal))
                {
                    Write(target, slot, fresh);
                    state.Properties[slot] = new Entry(entry.Key, fresh);
                }
            }
        }
    }

    /// <summary>
    /// Re-applies every localized attached property in a live visual tree. This
    /// complements the weak target registry: WinUI may replace or detach element
    /// instances while a language change is raised, and only a tree walk is
    /// guaranteed to reach the elements the user is actually looking at.
    /// </summary>
    public static void ApplyToTree(DependencyObject root)
    {
        ApplyToElement(root);

        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            ApplyToTree(child);
        }
    }

    private static void ApplyToElement(DependencyObject target)
    {
        ApplySlot(target, "Text", TextProperty);
        ApplySlot(target, "Content", ContentProperty);
        ApplySlot(target, "Label", LabelProperty);
        ApplySlot(target, "Placeholder", PlaceholderProperty);
        ApplySlot(target, "ToolTip", ToolTipProperty);
        ApplySlot(target, "AutomationName", AutomationNameProperty);
        ApplySlot(target, "HelpText", HelpTextProperty);
        ApplySlot(target, "OnContent", OnContentProperty);
        ApplySlot(target, "OffContent", OffContentProperty);
    }

    private static void ApplySlot(DependencyObject target, string slot, DependencyProperty property)
    {
        if (target.GetValue(property) is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        Write(target, slot, UiStrings.Get(key, _currentLanguage));
    }

    private static void OnChanged(DependencyObject target, string slot, object? newValue)
    {
        if (newValue is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        string value = UiStrings.Get(key, _currentLanguage);
        State state = Tracked.GetOrCreateValue(target);
        if (!state.Properties.ContainsKey(slot))
        {
            Targets.Add(new WeakReference<DependencyObject>(target));
        }

        state.Properties[slot] = new Entry(key, value);
        Write(target, slot, value);
    }

    private static string? Read(DependencyObject target, string slot) => slot switch
    {
        "Text" => target is TextBlock text ? text.Text : target is TextBox box ? box.Text : null,
        "Content" => (target as ContentControl)?.Content as string,
        "Label" => target switch
        {
            TextBlock text => text.Text,
            ContentControl content => content.Content as string,
            ToggleMenuFlyoutItem toggle => toggle.Text,
            MenuFlyoutSubItem sub => sub.Text,
            MenuFlyoutItem item => item.Text,
            _ => null,
        },
        "Placeholder" => target switch
        {
            TextBox box => box.PlaceholderText,
            AutoSuggestBox suggest => suggest.PlaceholderText,
            _ => null,
        },
        "ToolTip" => ToolTipService.GetToolTip(target) as string,
        "AutomationName" => AutomationProperties.GetName(target),
        "HelpText" => AutomationProperties.GetHelpText(target),
        "OnContent" => (target as ToggleSwitch)?.OnContent as string,
        "OffContent" => (target as ToggleSwitch)?.OffContent as string,
        _ => null,
    };

    private static void Write(DependencyObject target, string slot, string value)
    {
        switch (slot)
        {
            case "Text" when target is TextBlock text:
                text.Text = value;
                break;
            case "Text" when target is TextBox box:
                box.Text = value;
                break;
            case "Content" when target is ContentControl content:
                content.Content = value;
                break;
            case "Label":
                WriteLabel(target, value);
                break;
            case "Placeholder" when target is TextBox textBox:
                textBox.PlaceholderText = value;
                break;
            case "Placeholder" when target is AutoSuggestBox suggest:
                suggest.PlaceholderText = value;
                break;
            case "ToolTip":
                ToolTipService.SetToolTip(target, value);
                break;
            case "AutomationName":
                AutomationProperties.SetName(target, value);
                break;
            case "HelpText":
                AutomationProperties.SetHelpText(target, value);
                break;
            case "OnContent" when target is ToggleSwitch toggle:
                toggle.OnContent = value;
                break;
            case "OffContent" when target is ToggleSwitch toggleOff:
                toggleOff.OffContent = value;
                break;
        }
    }

    private static void WriteLabel(DependencyObject target, string value)
    {
        switch (target)
        {
            case TextBlock text:
                text.Text = value;
                break;
            case ContentControl content:
                content.Content = value;
                break;
            case ToggleMenuFlyoutItem toggle:
                toggle.Text = value;
                break;
            case MenuFlyoutSubItem sub:
                sub.Text = value;
                break;
            case MenuFlyoutItem item:
                item.Text = value;
                break;
        }
    }

    private static DependencyProperty Register(
        string name,
        string slot,
        PropertyMetadata? metadata = null)
    {
        return DependencyProperty.RegisterAttached(
            name,
            typeof(string),
            typeof(Loc),
            metadata ?? new PropertyMetadata(null, (d, e) => OnChanged(d, slot, e.NewValue)));
    }

    public static readonly DependencyProperty TextProperty =
        Register("Text", "Text");

    public static readonly DependencyProperty ContentProperty =
        Register("Content", "Content");

    public static readonly DependencyProperty LabelProperty =
        Register("Label", "Label");

    public static readonly DependencyProperty PlaceholderProperty =
        Register("Placeholder", "Placeholder");

    public static readonly DependencyProperty ToolTipProperty =
        Register("ToolTip", "ToolTip");

    public static readonly DependencyProperty AutomationNameProperty =
        Register("AutomationName", "AutomationName");

    public static readonly DependencyProperty HelpTextProperty =
        Register("HelpText", "HelpText");

    public static readonly DependencyProperty OnContentProperty =
        Register("OnContent", "OnContent");

    public static readonly DependencyProperty OffContentProperty =
        Register("OffContent", "OffContent");

    public static void SetText(DependencyObject element, string value) =>
        element.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject element) =>
        element.GetValue(TextProperty) as string;

    public static void SetContent(DependencyObject element, string value) =>
        element.SetValue(ContentProperty, value);

    public static string? GetContent(DependencyObject element) =>
        element.GetValue(ContentProperty) as string;

    public static void SetLabel(DependencyObject element, string value) =>
        element.SetValue(LabelProperty, value);

    public static string? GetLabel(DependencyObject element) =>
        element.GetValue(LabelProperty) as string;

    public static void SetPlaceholder(DependencyObject element, string value) =>
        element.SetValue(PlaceholderProperty, value);

    public static string? GetPlaceholder(DependencyObject element) =>
        element.GetValue(PlaceholderProperty) as string;

    public static void SetToolTip(DependencyObject element, string value) =>
        element.SetValue(ToolTipProperty, value);

    public static string? GetToolTip(DependencyObject element) =>
        element.GetValue(ToolTipProperty) as string;

    public static void SetAutomationName(DependencyObject element, string value) =>
        element.SetValue(AutomationNameProperty, value);

    public static string? GetAutomationName(DependencyObject element) =>
        element.GetValue(AutomationNameProperty) as string;

    public static void SetHelpText(DependencyObject element, string value) =>
        element.SetValue(HelpTextProperty, value);

    public static string? GetHelpText(DependencyObject element) =>
        element.GetValue(HelpTextProperty) as string;

    public static void SetOnContent(DependencyObject element, string value) =>
        element.SetValue(OnContentProperty, value);

    public static string? GetOnContent(DependencyObject element) =>
        element.GetValue(OnContentProperty) as string;

    public static void SetOffContent(DependencyObject element, string value) =>
        element.SetValue(OffContentProperty, value);

    public static string? GetOffContent(DependencyObject element) =>
        element.GetValue(OffContentProperty) as string;
}


