namespace LibMpv.Managed;

/// <summary>A managed snapshot of a value returned by libmpv.</summary>
public abstract record MpvValue
{
    private MpvValue() { }

    public static MpvValue Unavailable { get; } = new None();
    public sealed record None : MpvValue;
    public sealed record Text(string Value) : MpvValue;
    public sealed record Flag(bool Value) : MpvValue;
    public sealed record Integer(long Value) : MpvValue;
    public sealed record Number(double Value) : MpvValue;
    public sealed record Sequence(IReadOnlyList<MpvValue> Items) : MpvValue;
    public sealed record Map(IReadOnlyDictionary<string, MpvValue> Items) : MpvValue;
    public sealed record Bytes(ReadOnlyMemory<byte> Value) : MpvValue;
}
