using System.Runtime.InteropServices;

namespace Wiina.Mpv;

/// <summary>Decoded pixels returned by mpv's <c>screenshot-raw</c> command.</summary>
public sealed record MpvScreenshot(
    int Width,
    int Height,
    int Stride,
    string PixelFormat,
    byte[] Pixels);

/// <summary>
/// Reads the node map returned by <c>screenshot-raw</c> without going through
/// <see cref="MpvNodeValue"/> (which copies every byte into an object and is
/// too expensive for a 4K frame).
/// </summary>
internal static class MpvScreenshotReader
{
    private const int MaxDimension = 16_384;

    public static unsafe MpvScreenshot? Read(nint nodePointer)
    {
        if (nodePointer == 0)
        {
            return null;
        }

        MpvNode root = Marshal.PtrToStructure<MpvNode>(nodePointer);
        if (root.Format != MpvFormat.NodeMap || root.Data == 0)
        {
            return null;
        }

        MpvNodeList list = Marshal.PtrToStructure<MpvNodeList>(root.Data);
        int count = Math.Clamp(list.Count, 0, 128);
        int nodeSize = Marshal.SizeOf<MpvNode>();

        int width = 0;
        int height = 0;
        int stride = 0;
        string? format = null;
        byte[]? pixels = null;

        for (int i = 0; i < count; i++)
        {
            nint keyPointer = list.Keys == 0
                ? 0
                : Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size);
            if (keyPointer == 0)
            {
                continue;
            }

            string? key = Marshal.PtrToStringUTF8(keyPointer);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            MpvNode value = Marshal.PtrToStructure<MpvNode>(
                list.Values + (i * nodeSize));
            switch (key)
            {
                case "w":
                    width = (int)ReadInteger(value);
                    break;
                case "h":
                    height = (int)ReadInteger(value);
                    break;
                case "stride":
                    stride = (int)ReadInteger(value);
                    break;
                case "format":
                    format = value.Format == MpvFormat.String && value.Data != 0
                        ? Marshal.PtrToStringUTF8(value.Data)
                        : null;
                    break;
                case "data":
                    pixels = ReadBytes(value, stride, height);
                    break;
            }
        }

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            return null;
        }

        if (pixels is null || pixels.Length == 0)
        {
            return null;
        }

        // mpv may report a negative stride for bottom-up images.
        int absoluteStride = Math.Abs(stride);
        if (absoluteStride <= 0)
        {
            absoluteStride = pixels.Length / Math.Max(1, height);
            stride = absoluteStride;
        }

        return new MpvScreenshot(
            width,
            height,
            stride,
            string.IsNullOrEmpty(format) ? "bgra" : format.ToLowerInvariant(),
            pixels);
    }

    private static long ReadInteger(MpvNode node) => node.Format switch
    {
        MpvFormat.Int64 => (long)node.Data,
        MpvFormat.Double => (long)BitConverter.Int64BitsToDouble((long)node.Data),
        _ => 0,
    };

    private static unsafe byte[]? ReadBytes(MpvNode node, int stride, int height)
    {
        if (node.Format == MpvFormat.ByteArray && node.Data != 0)
        {
            MpvByteArray array = Marshal.PtrToStructure<MpvByteArray>(node.Data);
            int size = array.Size > int.MaxValue ? 0 : (int)array.Size;
            if (size <= 0 || array.Data == 0)
            {
                return null;
            }

            var bytes = new byte[size];
            Marshal.Copy(array.Data, bytes, 0, size);
            return bytes;
        }

        // Compatibility with older mpv builds that return data as an array of
        // int64 byte values.
        if (node.Format == MpvFormat.NodeArray && node.Data != 0)
        {
            MpvNodeList list = Marshal.PtrToStructure<MpvNodeList>(node.Data);
            int count = Math.Clamp(list.Count, 0, 512 * 1024 * 1024);
            var bytes = new byte[count];
            int nodeSize = Marshal.SizeOf<MpvNode>();
            for (int i = 0; i < count; i++)
            {
                MpvNode item = Marshal.PtrToStructure<MpvNode>(
                    list.Values + (i * nodeSize));
                bytes[i] = item.Format == MpvFormat.Int64
                    ? (byte)(long)item.Data
                    : (byte)0;
            }

            return bytes;
        }

        return null;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvByteArray
{
    public nint Data;
    public nuint Size;
}
