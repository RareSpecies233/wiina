using Microsoft.UI.Xaml.Controls;
using Wiina.App.Interop;

namespace Wiina.App.Controls;

/// <summary>
/// One attachable video surface. mpv renders into a single composition
/// swapchain; moving to PiP detaches the panel here and attaches the same
/// swapchain to the PiP window's surface, so audio and playback state continue
/// without re-initialising libmpv (Stage 6).
/// </summary>
public sealed partial class MpvVideoView : UserControl
{
    public MpvVideoView()
    {
        InitializeComponent();
    }

    /// <summary>mpv-owned swapchain currently bound to this control.</summary>
    public nint SwapChain { get; private set; }

    /// <summary>Binds the swapchain. Safe to call with 0 to clear.</summary>
    public void Attach(nint swapChain)
    {
        if (swapChain == 0)
        {
            Detach();
            return;
        }

        SwapChainPanelInterop.SetSwapChain(VideoPanel, swapChain);
        SwapChain = swapChain;
    }

    /// <summary>Unbinds without destroying the swapchain (mpv still owns it).</summary>
    public void Detach()
    {
        if (SwapChain == 0)
        {
            return;
        }

        try
        {
            SwapChainPanelInterop.SetSwapChain(VideoPanel, 0);
        }
        catch
        {
            // The panel may already be disappearing with its window.
        }

        SwapChain = 0;
    }

    /// <summary>
    /// Sizes the panel to the <em>layout</em> size of the video area (full
    /// window, physical pixels) and applies the inverse DPI transform (the
    /// composition driver rejects SetMatrixTransform).
    ///
    /// This is deliberately not the mpv render/composition size: mpv may render
    /// into a capped buffer (see <c>PipGeometry.RenderSize</c>) which the
    /// compositor upscales to this panel. Passing the capped buffer size here
    /// used to shrink the XAML surface, leaving the window partly uncovered
    /// (issue 3).
    /// </summary>
    public void SetLayoutSize(int pixelWidth, int pixelHeight, double rasterizationScale)
    {
        int width = Math.Max(1, pixelWidth);
        int height = Math.Max(1, pixelHeight);
        Width = width;
        Height = height;
        VideoPanel.Width = width;
        VideoPanel.Height = height;

        double scale = rasterizationScale > 0.1 ? rasterizationScale : 1.0;
        VideoTransform.ScaleX = 1.0 / scale;
        VideoTransform.ScaleY = 1.0 / scale;
    }
}
