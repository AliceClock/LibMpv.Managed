[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Artifacts,
    [Parameter(Mandatory)][string]$Output
)

$ErrorActionPreference = 'Stop'
$artifactRoot = (Resolve-Path -LiteralPath $Artifacts).Path
$outputPath = (New-Item -ItemType Directory -Path $Output -Force).FullName
$project = Join-Path $PSScriptRoot '../native/package/LibMpv.Managed.Native.WinX64.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.Project.PropertyGroup.Version

foreach ($file in 'runtime/libmpv-2.dll', 'runtime.sha256', 'corresponding-source.tar.xz',
    'corresponding-source.tar.xz.sha256', 'sources.lock.tsv', 'pe.txt', 'config.h', 'toolchain-packages.tsv') {
    if (!(Test-Path -LiteralPath (Join-Path $artifactRoot $file) -PathType Leaf)) {
        throw "Missing native build artifact: $file"
    }
}
foreach ($name in 'mpv', 'ffmpeg', 'libplacebo', 'freetype', 'fribidi', 'harfbuzz', 'libass') {
    if (!(Test-Path -LiteralPath (Join-Path $artifactRoot "licenses/$name") -PathType Container)) {
        throw "Missing native license inventory for $name"
    }
}
foreach ($pair in @(
    @{ Path = 'runtime/libmpv-2.dll'; Checksum = 'runtime.sha256' },
    @{ Path = 'corresponding-source.tar.xz'; Checksum = 'corresponding-source.tar.xz.sha256' }
)) {
    $checksum = Get-Content -LiteralPath (Join-Path $artifactRoot $pair.Checksum) -Raw
    $expected = ($checksum -split '\s+')[0]
    if ($expected -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath (Join-Path $artifactRoot $pair.Path)).Hash -ne $expected) {
        throw "Invalid native artifact checksum: $($pair.Path)"
    }
}

$release = "https://github.com/AliceClock/LibMpv.Managed/releases/download/native-v$version"
@"
# LGPL libmpv for Windows x64

This package supplies the optional native runtime for LibMpv.Managed. It contains libmpv 0.42.0 linked with LGPL FFmpeg.

[Corresponding source and build recipe]($release/corresponding-source.tar.xz)
[Source SHA-256]($release/corresponding-source.tar.xz.sha256)

To use a compatible modified build, set the managed client's LibraryPath option to its absolute DLL path.
"@ | Set-Content -LiteralPath (Join-Path $artifactRoot 'RUNTIME.md') -Encoding utf8

@"
# Runtime licenses

This Windows x64 runtime combines mpv in LGPL mode with LGPL v3 FFmpeg. Each bundled component's license and copyright notices are in the licenses directory. See licenses/mpv and licenses/ffmpeg for the LGPL and incorporated GPL license texts.

The matching [source archive]($release/corresponding-source.tar.xz) includes the source trees, lock file and build recipe.
"@ | Set-Content -LiteralPath (Join-Path $artifactRoot 'LICENSES.md') -Encoding utf8

dotnet pack $project -c Release -o $outputPath "-p:NativePackageStage=$artifactRoot"
if ($LASTEXITCODE -ne 0) { throw 'Native NuGet packaging failed.' }
