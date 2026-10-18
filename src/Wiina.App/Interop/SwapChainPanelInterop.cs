using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Interop;

/// <summary>
/// Binds an mpv-owned DXGI composition swapchain to a WinUI 3 SwapChainPanel.
/// The vtable call is deliberately small and explicit; no third-party interop
/// package is needed.
///
/// DPI note: this driver rejects IDXGISwapChain2::SetMatrixTransform on a
/// composition swapchain with DXGI_ERROR_INVALID_CALL (0x887A0001). Wiina
/// therefore lays the panel out at the buffer's physical pixel size and applies
/// a 1/dpi XAML CompositeTransform (see MainWindow.ApplyVideoLayout).
/// </summary>
internal static class SwapChainPanelInterop
{
    private static readonly Guid IID_ISwapChainPanelNative =
        new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetSwapChainDelegate(nint @this, nint swapChain);

    /// <summary>Call on the UI thread. Pass 0 to detach the swapchain.</summary>
    public static void SetSwapChain(SwapChainPanel panel, nint swapChain)
    {
        nint unknown = ((WinRT.IWinRTObject)panel).NativeObject.ThisPtr;
        Guid iid = IID_ISwapChainPanelNative;
        int hr = Marshal.QueryInterface(unknown, in iid, out nint native);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            // ISwapChainPanelNative: IUnknown(3) + SetSwapChain = slot 3.
            nint vtable = Marshal.ReadIntPtr(native);
            nint function = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            var setSwapChain =
                Marshal.GetDelegateForFunctionPointer<SetSwapChainDelegate>(function);
            Marshal.ThrowExceptionForHR(setSwapChain(native, swapChain));
        }
        finally
        {
            Marshal.Release(native);
        }
    }
}
