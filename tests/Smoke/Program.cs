using System.Text;
using LibMpv.Managed;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var token = deadline.Token;
var runtime = args.ElementAtOrDefault(0);
var fixtures = Directory.CreateTempSubdirectory("libmpv-smoke-");
try
{
    Expect<DllNotFoundException>(() => MpvClient.Create(new() { LibraryPath = Path.Combine(fixtures.FullName, "missing.dll") }));
    Expect<ArgumentException>(() => MpvClient.Create(new() { LibraryPath = "relative.dll" }));
    var badImage = Path.Combine(fixtures.FullName, "bad-image.dll");
    File.WriteAllText(badImage, "This is not a PE image.");
    Expect<BadImageFormatException>(() => MpvClient.Create(new() { LibraryPath = badImage }));
    if (Environment.GetEnvironmentVariable("MPV_WRONG_ARCH_DLL") is { Length: > 0 } wrongArch)
        Expect<BadImageFormatException>(() => MpvClient.Create(new() { LibraryPath = wrongArch }));
    if (Environment.GetEnvironmentVariable("MPV_OLD_API_DLL") is { Length: > 0 } oldApi)
        Expect<NotSupportedException>(() => MpvClient.Create(new() { LibraryPath = oldApi }));

    var video = Path.Combine(fixtures.FullName, "视频 测试.y4m");
    using (var file = File.Create(video))
    {
        file.Write("YUV4MPEG2 W32 H32 F25:1 Ip A1:1 C420jpeg\n"u8);
        var frame = new byte[32 * 32 * 3 / 2];
        Array.Fill(frame, (byte)128);
        for (var i = 0; i < 60; i++)
        {
            file.Write("FRAME\n"u8);
            file.Write(frame);
        }
    }

    var audio = Path.Combine(fixtures.FullName, "音频 测试.wav");
    using (var wave = new BinaryWriter(File.Create(audio), Encoding.UTF8))
    {
        const int length = 48000 * 2;
        wave.Write("RIFF"u8);
        wave.Write(36 + length);
        wave.Write("WAVEfmt "u8);
        wave.Write(16);
        wave.Write((short)1);
        wave.Write((short)1);
        wave.Write(48000);
        wave.Write(96000);
        wave.Write((short)2);
        wave.Write((short)16);
        wave.Write("data"u8);
        wave.Write(length);
        wave.Write(new byte[length]);
    }

    await using var player = Open(runtime, paused: true);
    await using var secondPlayer = Open(runtime, paused: false);
    Check(player.ClientApiVersion.Major == 2 && player.ClientApiVersion.Minor >= 6, "ABI version");
    if (runtime is not null)
        Check(player.LibraryPath == Path.GetFullPath(runtime), "explicit DLL precedence");

    await using var events = player.ReadEventsAsync(token).GetAsyncEnumerator();
    var observation = player.ObserveProperty("pause");
    var initial = (MpvEvent.PropertyChanged)await Next(
        events, item => item is MpvEvent.PropertyChanged change && change.ObservationId == observation);
    Check(initial.Value is MpvValue.Flag { Value: true }, "initial pause notification");

    await Task.WhenAll(
        player.CommandAsync(["loadfile", video, "replace"], token),
        secondPlayer.CommandAsync(["loadfile", audio, "replace"], token));
    await Next(events, item => item is MpvEvent.FileLoaded);
    Check(player.GetProperty("path") is MpvValue.Text text && text.Value == video, "Unicode video path");
    Check(player.GetProperty("track-list") is MpvValue.Sequence { Items.Count: > 0 }, "nested property snapshot");
    Check(player.GetProperty("pause") is MpvValue.Flag { Value: true }, "pause property");

    await player.CommandAsync(["seek", "1.2", "absolute+exact"], token);
    while (player.GetProperty("time-pos") is not MpvValue.Number { Value: > 1 })
        await Task.Delay(20, token);

    await ExpectAsync<OperationCanceledException>(() => player.CommandAsync(["stop"], new CancellationToken(true)));
    await ExpectAsync<MpvException>(() => player.CommandAsync(["not-a-libmpv-command"], token));
    player.SetProperty("pause", new MpvValue.Flag(false));
    await Next(events, item => item is MpvEvent.PropertyChanged { Value: MpvValue.Flag { Value: false } } change &&
        change.ObservationId == observation);
    await Next(events, item => item is MpvEvent.Ended { Reason: MpvEndReason.EndOfFile });
    Check(player.UnobserveProperty(observation), "remove observation");
    Check(!player.UnobserveProperty(observation), "observation removed");

    await using var audioEvents = secondPlayer.ReadEventsAsync(token).GetAsyncEnumerator();
    await Next(audioEvents, item => item is MpvEvent.FileLoaded);
    await Next(audioEvents, item => item is MpvEvent.Ended { Reason: MpvEndReason.EndOfFile });

    await Task.WhenAll(
        Task.Run(async () => await player.DisposeAsync(), token),
        Task.Run(async () => await player.DisposeAsync(), token),
        Task.Run(async () => await secondPlayer.DisposeAsync(), token)).WaitAsync(token);
    Expect<ObjectDisposedException>(() => player.GetProperty("pause"));
    Console.WriteLine($"PASS: Win64 libmpv {player.ClientApiVersion}, Unicode audio/video, commands, seek, events, disposal ({player.LibraryPath}).");
}
finally
{
    fixtures.Delete(recursive: true);
}

static MpvClient Open(string? path, bool paused) => MpvClient.Create(new MpvClientOptions
{
    LibraryPath = path,
    Options = new Dictionary<string, string>
    {
        ["ao"] = "null",
        ["vo"] = "null",
        ["pause"] = paused ? "yes" : "no",
    },
});

static async Task<MpvEvent> Next(IAsyncEnumerator<MpvEvent> reader, Func<MpvEvent, bool> predicate)
{
    while (await reader.MoveNextAsync())
    {
        if (predicate(reader.Current))
            return reader.Current;
    }

    throw new InvalidOperationException("Session closed before the expected event.");
}

static void Check(bool predicate, string label)
{
    if (!predicate)
        throw new InvalidOperationException($"Smoke assertion failed: {label}.");
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
