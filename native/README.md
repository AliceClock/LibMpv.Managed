# Windows x64 LGPL runtime

This recipe cross-compiles unmodified upstream mpv v0.42.0 with MinGW and static LGPL FFmpeg. The Ubuntu Base archive, tool package snapshot, source commits and upstream submodule commits are fixed. The distribution enables Windows OpenGL and WASAPI. Its enabled components are selected explicitly and can differ from the upstream full Windows build used in interoperability testing.

From the repository root on a Linux Docker runner:

```sh
mkdir -p .artifacts/native
bash native/bootstrap.sh
docker run --rm --mount "type=bind,source=$PWD/native,target=/recipe,readonly" \
  --mount "type=bind,source=$PWD/.artifacts/native,target=/out" libmpv-managed-build:0.42.0
```

The build verifies the LGPL FFmpeg options, Win64 PE format, imports and standard libmpv exports. It outputs the DLL, license inventory, lock and build records, and a corresponding source archive with the build recipe. To rebuild with the same sources and image, mount the archive directory as `/input` and run that image with `--network=none`. Use the included build recipe as `/recipe`.

On Windows, pack the candidate and run the NuGet consumer test:

```powershell
dotnet pack src/LibMpv.Managed -c Release -o .artifacts/feed
pwsh ./scripts/Pack-Native.ps1 -Artifacts .artifacts/native -Output .artifacts/feed
pwsh ./scripts/Test-Packages.ps1 -Source .artifacts/feed
```

The manual workflow has three operations. `validate` builds and tests candidate artifacts. `stage` attaches the validated packages, corresponding source, checksums and build records to a versioned GitHub prerelease. `publish` verifies and pushes those exact GitHub assets to NuGet.org, then tests a public restore from an empty cache.

Configure NuGet Trusted Publishing for both package IDs with new-package publishing scopes. Use repository `AliceClock/LibMpv.Managed`, workflow `release.yml`, and GitHub environment `nuget`. Set the environment variable `NUGET_USER` in GitHub Actions to the NuGet account username.

The package embeds its license files and its matching source link. A consumer may run a modified ABI-compatible build by setting `MpvClientOptions.LibraryPath`. The official binary hashes are used during build and packaging only.
