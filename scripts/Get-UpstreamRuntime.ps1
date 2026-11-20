[CmdletBinding()]
param([string]$Output = (Join-Path $PSScriptRoot '../.artifacts/upstream'))

$ErrorActionPreference = 'Stop'
$directory = (New-Item -ItemType Directory -Path $Output -Force).FullName
$archive = Join-Path $directory 'mpv-v0.42.0-win-x64-lgpl.zip'
$uri = 'https://github.com/mpv-player/mpv/releases/download/v0.42.0/libmpv-v0.42.0-x86_64-w64-mingw32-lgpl.zip'
$sha = '8e5f77430efc53ef175804ba6618527e3af0448a0dcabf04d3cc72659d1a6fce'
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri $uri -OutFile "$archive.download"
    if ((Get-FileHash -LiteralPath "$archive.download").Hash -ne $sha) {
        throw 'The upstream libmpv archive did not match the pinned SHA-256.'
    }

    Move-Item -LiteralPath "$archive.download" -Destination $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $sha) {
    throw "Bad checksum: $archive"
}

Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
Get-Item -LiteralPath (Join-Path $directory 'libmpv-2.dll') | Select-Object -ExpandProperty FullName
