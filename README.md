# LibMpv.Managed

A .NET 10 library for embedding upstream libmpv in Windows x64 applications. The managed API supports playback commands, typed properties, observations, and asynchronous event delivery. The application supplies the video surface if it embeds a window.

## Install

Install the managed package and, optionally, the separately versioned LGPL runtime.

```powershell
dotnet add package LibMpv.Managed --version 0.1.0-preview.1
dotnet add package LibMpv.Managed.Native.WinX64 --version 0.42.0-preview.1
```

The runtime package provides `libmpv-2.dll` for a Windows x64 process. To use a compatible custom build instead, set `MpvClientOptions.LibraryPath` to its absolute DLL path. The loader uses that exact path and searches its directory for dependencies. Without an override, it looks for the DLL next to the application and under `runtimes/win-x64/native/`. Client API 2.6 or a newer 2.x ABI is required.

## Play a file

```csharp
using LibMpv.Managed;

await using var player = MpvClient.Create();
await player.CommandAsync(["loadfile", Path.GetFullPath("video.mp4"), "replace"]);
await foreach (var item in player.ReadEventsAsync())
{
    if (item is MpvEvent.Ended ended)
    {
        Console.WriteLine($"Playback finished: {ended.Reason}");
        break;
    }
}
```

Command tasks complete when libmpv replies. For `loadfile`, read the `FileLoaded` event before querying media-specific properties. A caller can cancel its wait without aborting a native command already submitted. Each session has one event reader. Consume events as they arrive when observing properties or collecting logs. A queue overflow closes the session and faults the event stream. Applications can dispatch events to their UI thread through their framework's dispatcher.

The [console sample](samples/Play) shows default playback. The [WinForms sample](samples/Embed) passes a Win32 panel HWND to libmpv via the upstream `wid` option. Both take a media path and optionally an absolute DLL path.

## Build and test

```powershell
dotnet build src/LibMpv.Managed -c Release
dotnet build samples/Play -c Release
dotnet build samples/Embed -c Release
pwsh ./scripts/Get-UpstreamRuntime.ps1
dotnet run --project tests/Smoke -c Release -- (Resolve-Path .artifacts/upstream/libmpv-2.dll)
```

The upstream test asset is verified by SHA-256. The smoke program generates its own media. See the [native build and release instructions](native/README.md) for the independently built runtime and package verification.

## Licenses

The C# wrapper is available under [Apache-2.0](LICENSE). Native declarations adapted from the upstream C header carry its [ISC notice](THIRD-PARTY-NOTICES.txt). The optional native package ships its own license inventory and a link to its matching source archive.
