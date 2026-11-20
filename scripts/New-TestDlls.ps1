[CmdletBinding()]
param([string]$Output = (Join-Path $PSScriptRoot '../.artifacts/shims'))

$ErrorActionPreference = 'Stop'
$directory = (New-Item -ItemType Directory -Path $Output -Force).FullName
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ tools are required to build the native test fixtures.' }

Import-Module (Join-Path $vs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
$source = Join-Path $PSScriptRoot '../tests/Smoke/api-shim.c'
Push-Location $directory
try {
    foreach ($architecture in 'x64', 'x86') {
        Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments "-arch=$architecture -host_arch=x64" | Out-Null
        & cl.exe /nologo /LD /MT "/Fo$architecture.obj" "/Fe$architecture.dll" $source
        if ($LASTEXITCODE -ne 0) { throw "Native test fixture build failed for $architecture." }
    }
}
finally {
    Pop-Location
}
