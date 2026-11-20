using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace LibMpv.Managed.Native;

// Native definitions adapted from libmpv client.h under the ISC license.
// See THIRD-PARTY-NOTICES.txt for the notice.
internal enum MpvFormat
{
    None = 0,
    String = 1,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8,
    ByteArray = 9,
}

internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
}

[StructLayout(LayoutKind.Explicit)]
internal struct MpvNodeValue
{
    [FieldOffset(0)] internal nint Pointer;
    [FieldOffset(0)] internal int Flag;
    [FieldOffset(0)] internal long Integer;
    [FieldOffset(0)] internal double Number;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvNode
{
    internal MpvNodeValue Value;
    internal MpvFormat Format;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvNodeList
{
    internal int Count;
    internal MpvNode* Values;
    internal nint* Keys;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvByteArray
{
    internal byte* Data;
    internal nuint Size;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEvent
{
    internal MpvEventId Id;
    internal int Error;
    internal ulong UserData;
    internal nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePropertyEvent
{
    internal nint Name;
    internal MpvFormat Format;
    internal nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEndFile
{
    internal MpvEndReason Reason;
    internal int Error;
    internal long PlaylistEntryId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLogMessage
{
    internal nint Prefix;
    internal nint Level;
    internal nint Text;
}

internal static unsafe class NativeValues
{
    internal static string ReadString(nint address) => Marshal.PtrToStringUTF8(address) ?? string.Empty;

    internal static MpvValue Copy(MpvNode node) => node.Format switch
    {
        MpvFormat.None => MpvValue.Unavailable,
        MpvFormat.String => new MpvValue.Text(ReadString(node.Value.Pointer)),
        MpvFormat.Flag => new MpvValue.Flag(node.Value.Flag != 0),
        MpvFormat.Int64 => new MpvValue.Integer(node.Value.Integer),
        MpvFormat.Double => new MpvValue.Number(node.Value.Number),
        MpvFormat.NodeArray => CopySequence((MpvNodeList*)node.Value.Pointer),
        MpvFormat.NodeMap => CopyMap((MpvNodeList*)node.Value.Pointer),
        MpvFormat.ByteArray => CopyBytes((MpvByteArray*)node.Value.Pointer),
        _ => throw new InvalidDataException($"Unsupported libmpv node format {(int)node.Format}."),
    };

    private static MpvValue CopySequence(MpvNodeList* list)
    {
        var items = new MpvValue[list->Count];
        for (var i = 0; i < items.Length; i++)
            items[i] = Copy(list->Values[i]);

        return new MpvValue.Sequence(Array.AsReadOnly(items));
    }

    private static MpvValue CopyMap(MpvNodeList* list)
    {
        var items = new Dictionary<string, MpvValue>(list->Count, StringComparer.Ordinal);
        for (var i = 0; i < list->Count; i++)
            items[ReadString(list->Keys[i])] = Copy(list->Values[i]);

        return new MpvValue.Map(new ReadOnlyDictionary<string, MpvValue>(items));
    }

    private static MpvValue CopyBytes(MpvByteArray* bytes) =>
        new MpvValue.Bytes(new ReadOnlySpan<byte>(bytes->Data, checked((int)bytes->Size)).ToArray());
}
