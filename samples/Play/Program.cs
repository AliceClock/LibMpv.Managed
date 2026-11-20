using LibMpv.Managed;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: Play <media-file> [absolute-libmpv-DLL-path]");
    return 2;
}

await using var player = MpvClient.Create(new MpvClientOptions { LibraryPath = args.ElementAtOrDefault(1) });
await player.CommandAsync(["loadfile", Path.GetFullPath(args[0]), "replace"]);
await foreach (var item in player.ReadEventsAsync())
{
    if (item is MpvEvent.FileLoaded)
        Console.WriteLine("File loaded.");
    if (item is MpvEvent.Ended ended)
    {
        Console.WriteLine($"Playback ended: {ended.Reason}");
        return ended.ErrorCode < 0 ? 1 : 0;
    }
}

return 0;
