namespace Wiina.Mpv;

/// <summary>
/// Runtime information about the loaded libmpv build. Used by About, the
/// settings diagnostics section and the startup version check without creating
/// a player instance.
/// </summary>
public static class MpvRuntime
{
    /// <summary>True when libmpv-2.dll could be resolved and queried.</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                _ = MpvInterop.mpv_client_api_version();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Client API version as "major.minor" (mpv 0.41 → 2.3).</summary>
    public static string ClientApiVersion
    {
        get
        {
            try
            {
                ulong version = MpvInterop.mpv_client_api_version();
                return $"{version >> 16}.{version & 0xFFFF}";
            }
            catch
            {
                return "未加载";
            }
        }
    }
}
