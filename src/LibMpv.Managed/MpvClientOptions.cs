namespace LibMpv.Managed;

/// <summary>Settings applied before the native playback core is initialized.</summary>
public sealed record MpvClientOptions
{
    public string? LibraryPath { get; init; }
    public IReadOnlyDictionary<string, string>? Options { get; init; }
    public string? LogLevel { get; init; }
}
