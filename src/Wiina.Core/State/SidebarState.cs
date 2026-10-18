using Wiina.Core.Models;

namespace Wiina.Core.State;

/// <summary>How the sidebar container enters/leaves the screen.</summary>
public enum SidebarAnimationMode
{
    /// <summary>Animations disabled: switch instantly.</summary>
    None,

    /// <summary>Reduced motion: cross-fade only.</summary>
    Fade,

    /// <summary>Default: 0.24 s slide from the window edge.</summary>
    Slide,
}

/// <summary>What a tab click did to a sidebar slot.</summary>
public enum SidebarToggleResult
{
    Unchanged,
    Shown,
    Hidden,
    Replaced,
    Moved,
}

/// <summary>
/// Pure sidebar model shared by the two slots. UI code owns the animation,
/// width drag and XAML hosts; this class only enforces the visibility rules:
/// one type per side, a type can only live on one side, and clicking the same
/// tab toggles it off while another type replaces the current one.
/// </summary>
public sealed class SidebarState
{
    public const double DefaultWidth = 360;

    public const double MinWidth = 300;

    public const double MaxWidth = 500;

    /// <summary>Sidebar may never cover more than 80% of the window width.</summary>
    public const double MaxWindowFraction = 0.8;

    /// <summary>Below this window width the shell hides sidebars entirely.</summary>
    public const double CompactWindowWidth = 420;

    /// <summary>Spec §20.1: SideBarAnimationDuration = 0.24 s.</summary>
    public const double AnimationSeconds = 0.24;

    private readonly Dictionary<SidebarSide, SidebarType> _shown = new();

    /// <summary>Current width in DIPs, always inside <see cref="ClampWidth"/>.</summary>
    public double Width { get; private set; } = DefaultWidth;

    public IReadOnlyDictionary<SidebarSide, SidebarType> Shown => _shown;

    public bool IsAnyVisible => _shown.Count > 0;

    public SidebarType? TypeFor(SidebarSide side) =>
        _shown.TryGetValue(side, out SidebarType type) ? type : null;

    public bool IsVisible(SidebarSide side) => _shown.ContainsKey(side);

    public bool IsVisible(SidebarType type) => _shown.ContainsValue(type);

    public SidebarSide? SideFor(SidebarType type)
    {
        foreach ((SidebarSide side, SidebarType shown) in _shown)
        {
            if (shown == type)
            {
                return side;
            }
        }

        return null;
    }

    /// <summary>
    /// Tab click: same tab closes, same type in another tab moves the sidebar,
    /// a different type replaces the slot. The other slot is never touched
    /// unless it already shows the requested type.
    /// </summary>
    public SidebarToggleResult Toggle(SidebarSide side, SidebarType type)
    {
        if (_shown.TryGetValue(side, out SidebarType current))
        {
            if (current == type)
            {
                _shown.Remove(side);
                return SidebarToggleResult.Hidden;
            }

            _shown[side] = type;
            RemoveFromOtherSide(side, type);
            return SidebarToggleResult.Replaced;
        }

        return ShowCore(side, type);
    }

    /// <summary>
    /// Opens a type without the "same tab closes" rule (menu/accelerator that
    /// explicitly asks for a page). A visible same type stays where it is.
    /// </summary>
    public SidebarToggleResult Show(SidebarSide side, SidebarType type)
    {
        if (_shown.TryGetValue(side, out SidebarType current))
        {
            if (current == type)
            {
                return SidebarToggleResult.Unchanged;
            }

            _shown[side] = type;
            RemoveFromOtherSide(side, type);
            return SidebarToggleResult.Replaced;
        }

        return ShowCore(side, type);
    }

    public bool Hide(SidebarSide side) => _shown.Remove(side);

    public void HideAll() => _shown.Clear();

    /// <summary>Removes a type from whichever slot owns it.</summary>
    public bool Hide(SidebarType type)
    {
        SidebarSide? side = SideFor(type);
        return side is not null && _shown.Remove(side.Value);
    }

    /// <summary>Sets and clamps the persisted width.</summary>
    public void SetWidth(double width, double windowWidth) =>
        Width = ClampWidth(width, windowWidth);

    /// <summary>
    /// 300–500 DIP and at most 80% of the window. For very narrow windows the
    /// lower bound wins (the shell hides sidebars below 420 DIP anyway) so the
    /// width never collapses to a useless sliver.
    /// </summary>
    public static double ClampWidth(double width, double windowWidth)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
        {
            width = DefaultWidth;
        }

        double max = MaxWidth;
        if (windowWidth > 0 && !double.IsInfinity(windowWidth))
        {
            max = Math.Min(max, windowWidth * MaxWindowFraction);
        }

        max = Math.Max(MinWidth, max);
        return Math.Clamp(width, MinWidth, max);
    }

    /// <summary>
    /// App-level animation switch wins; a system that asks for reduced motion
    /// gets a fade instead of a slide.
    /// </summary>
    public static SidebarAnimationMode ResolveAnimation(
        bool appAnimationsEnabled,
        bool systemAnimationsEnabled)
    {
        if (!appAnimationsEnabled)
        {
            return SidebarAnimationMode.None;
        }

        return systemAnimationsEnabled
            ? SidebarAnimationMode.Slide
            : SidebarAnimationMode.Fade;
    }

    /// <summary>Docked video inset for one side; edge-to-edge overlays the video.</summary>
    public static double DockedInset(
        WindowLayoutMode layout,
        bool visible,
        double width) =>
        layout == WindowLayoutMode.Docked && visible ? width : 0;

    /// <summary>Sidebars are suppressed below the compact window width.</summary>
    public static bool AllowedAtWindowWidth(double windowWidth) =>
        windowWidth <= 0 || windowWidth >= CompactWindowWidth;

    private SidebarToggleResult ShowCore(SidebarSide side, SidebarType type)
    {
        SidebarSide? other = SideFor(type);
        if (other == side)
        {
            return SidebarToggleResult.Unchanged;
        }

        if (other is not null)
        {
            _shown.Remove(other.Value);
            _shown[side] = type;
            return SidebarToggleResult.Moved;
        }

        _shown[side] = type;
        return SidebarToggleResult.Shown;
    }

    private void RemoveFromOtherSide(SidebarSide side, SidebarType type)
    {
        foreach (SidebarSide other in _shown.Keys.ToArray())
        {
            if (other != side && _shown[other] == type)
            {
                _shown.Remove(other);
            }
        }
    }
}
