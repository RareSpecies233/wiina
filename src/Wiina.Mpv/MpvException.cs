using System.Runtime.InteropServices;

namespace Wiina.Mpv;

public sealed class MpvException : Exception
{
    public int ErrorCode { get; }

    public MpvException(string message) : base(message)
    {
    }

    public MpvException(string operation, int code)
        : base($"{operation} failed: {ErrorString(code)} ({code})")
    {
        ErrorCode = code;
    }

    private static string ErrorString(int code)
    {
        nint ptr = MpvInterop.mpv_error_string(code);
        return ptr == 0 ? "unknown error" : Marshal.PtrToStringUTF8(ptr) ?? "unknown error";
    }
}
