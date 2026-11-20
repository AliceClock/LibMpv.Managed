namespace LibMpv.Managed;

/// <summary>A snapshot of an event. Its data remains valid after the next native event.</summary>
public abstract record MpvEvent
{
    private MpvEvent() { }

    public sealed record Started(long PlaylistEntryId) : MpvEvent;
    public sealed record FileLoaded : MpvEvent;
    public sealed record PlaybackRestarted : MpvEvent;
    public sealed record Ended(MpvEndReason Reason, int ErrorCode, long PlaylistEntryId) : MpvEvent;
    public sealed record PropertyChanged(ulong ObservationId, string Name, MpvValue Value) : MpvEvent;
    public sealed record LogMessage(string Prefix, string Level, string Text) : MpvEvent;
    public sealed record Shutdown : MpvEvent;
}

public enum MpvEndReason
{
    EndOfFile = 0,
    Stopped = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5,
}
