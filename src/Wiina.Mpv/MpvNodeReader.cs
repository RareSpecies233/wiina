using System.Runtime.InteropServices;

namespace Wiina.Mpv;

/// <summary>Managed mirror of one <c>mpv_node</c> tree. Values are copied out
/// before mpv frees the event payload.</summary>
internal sealed class MpvNodeValue
{
    public MpvFormat Format { get; init; }

    public string? StringValue { get; init; }

    public long Int64Value { get; init; }

    public double DoubleValue { get; init; }

    public bool FlagValue { get; init; }

    public IReadOnlyList<MpvNodeValue>? ArrayValue { get; init; }

    public IReadOnlyDictionary<string, MpvNodeValue>? MapValue { get; init; }

    public MpvNodeValue? this[string key] =>
        MapValue is not null && MapValue.TryGetValue(key, out MpvNodeValue? value)
            ? value
            : null;

    public string? AsString() =>
        Format == MpvFormat.String ? StringValue : null;

    public int AsInt(int fallback = 0) => Format switch
    {
        MpvFormat.Int64 => (int)Int64Value,
        MpvFormat.Double => (int)DoubleValue,
        _ => fallback,
    };

    public double AsDouble(double fallback = 0) => Format switch
    {
        MpvFormat.Double => DoubleValue,
        MpvFormat.Int64 => Int64Value,
        _ => fallback,
    };

    public bool AsFlag(bool fallback = false) =>
        Format == MpvFormat.Flag ? FlagValue : fallback;

    public static MpvNodeValue? Read(nint pointer, int depth = 0)
    {
        if (pointer == 0 || depth > 16)
        {
            return null;
        }

        MpvNode node = Marshal.PtrToStructure<MpvNode>(pointer);
        return Read(ref node, depth);
    }

    public static unsafe MpvNodeValue? ReadNode(ref MpvNode node)
    {
        fixed (MpvNode* pointer = &node)
        {
            return Read((nint)pointer, 0);
        }
    }

    private static MpvNodeValue? Read(ref MpvNode node, int depth)
    {
        switch (node.Format)
        {
            case MpvFormat.None:
                return new MpvNodeValue { Format = MpvFormat.None };
            case MpvFormat.String:
                return new MpvNodeValue
                {
                    Format = MpvFormat.String,
                    StringValue = node.Data == 0
                        ? null
                        : Marshal.PtrToStringUTF8(node.Data),
                };
            case MpvFormat.Flag:
                return new MpvNodeValue
                {
                    Format = MpvFormat.Flag,
                    FlagValue = (int)node.Data != 0,
                };
            case MpvFormat.Int64:
                return new MpvNodeValue
                {
                    Format = MpvFormat.Int64,
                    Int64Value = (long)node.Data,
                };
            case MpvFormat.Double:
                return new MpvNodeValue
                {
                    Format = MpvFormat.Double,
                    DoubleValue = BitConverter.Int64BitsToDouble((long)node.Data),
                };
            case MpvFormat.NodeArray:
            case MpvFormat.NodeMap:
                return ReadList(node.Format, node.Data, depth);
            default:
                return new MpvNodeValue { Format = node.Format };
        }
    }

    private static MpvNodeValue? ReadList(
        MpvFormat format,
        nint listPointer,
        int depth)
    {
        if (listPointer == 0)
        {
            return new MpvNodeValue { Format = format };
        }

        MpvNodeList list = Marshal.PtrToStructure<MpvNodeList>(listPointer);
        int count = Math.Clamp(list.Count, 0, 10_000);
        int nodeSize = Marshal.SizeOf<MpvNode>();

        if (format == MpvFormat.NodeArray)
        {
            var values = new List<MpvNodeValue>(count);
            for (int i = 0; i < count; i++)
            {
                nint itemPointer = list.Values + (i * nodeSize);
                values.Add(Read(itemPointer, depth + 1) ??
                    new MpvNodeValue { Format = MpvFormat.None });
            }

            return new MpvNodeValue
            {
                Format = format,
                ArrayValue = values,
            };
        }

        var map = new Dictionary<string, MpvNodeValue>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            nint keyPointer = list.Keys == 0
                ? 0
                : Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size);
            string? key = keyPointer == 0
                ? null
                : Marshal.PtrToStringUTF8(keyPointer);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            nint valuePointer = list.Values + (i * nodeSize);
            map[key] = Read(valuePointer, depth + 1) ??
                new MpvNodeValue { Format = MpvFormat.None };
        }

        return new MpvNodeValue
        {
            Format = format,
            MapValue = map,
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvNode
{
    public nint Data;
    public MpvFormat Format;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvNodeList
{
    public int Count;
    public nint Values;
    public nint Keys;
}
