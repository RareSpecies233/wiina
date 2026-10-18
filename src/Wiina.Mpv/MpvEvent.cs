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

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    public int Reason;
    public int Error;
    public long PlaylistEntryId;
    public long PlaylistInsertId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessage
{
    public nint Prefix;
    public nint Level;
    public nint Text;
    public int LogLevel;
}
