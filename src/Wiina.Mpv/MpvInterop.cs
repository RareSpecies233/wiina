using System.Runtime.InteropServices;

namespace Wiina.Mpv;

/// <summary>Raw libmpv client API entry points. Keep this 1:1 with client.h.</summary>
internal static class MpvInterop
{
    private const string Lib = "libmpv-2.dll";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_initialize(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_terminate_destroy(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong mpv_client_api_version();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_set_option_string(nint ctx, string name, string data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_set_property_string(nint ctx, string name, string data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern nint mpv_get_property_string(nint ctx, string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_get_property(nint ctx, string name, MpvFormat format, out long data);

    [DllImport(Lib, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_get_property_double(nint ctx, string name, MpvFormat format, out double data);

    [DllImport(Lib, EntryPoint = "mpv_get_property", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_get_property_flag(nint ctx, string name, MpvFormat format, out int data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_free(nint data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_command(nint ctx, nint args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_command_node(nint ctx, nint args, out MpvNode result);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_command_async(
        nint ctx,
        ulong replyUserData,
        nint args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_get_property_node(
        nint ctx,
        string name,
        MpvFormat format,
        out MpvNode data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_free_node_contents(ref MpvNode node);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_request_log_messages(nint ctx, string minLevel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int mpv_observe_property(nint ctx, ulong replyUserData, string name, MpvFormat format);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_wait_event(nint ctx, double timeout);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_wakeup(nint ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_error_string(int error);
}
