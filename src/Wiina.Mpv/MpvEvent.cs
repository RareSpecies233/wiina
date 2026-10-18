using System.Runtime.InteropServices;

namespace Wiina.Mpv;

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserData;
    public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
    public nint Name;
    public MpvFormat Format;
    public nint Data;
}
