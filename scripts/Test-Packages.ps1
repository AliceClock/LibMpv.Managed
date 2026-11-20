[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [string]$OverrideDll,
    [string[]]$AdditionalSources = @()
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Source) {
    $Source = (Resolve-Path -LiteralPath $Source).Path
}
if ($OverrideDll) {
    $OverrideDll = (Resolve-Path -LiteralPath $OverrideDll).Path
}

$sourceRoot = Split-Path -Parent $PSScriptRoot
[xml]$managed = Get-Content -LiteralPath (Join-Path $sourceRoot 'src/LibMpv.Managed/LibMpv.Managed.csproj') -Raw
[xml]$native = Get-Content -LiteralPath (Join-Path $sourceRoot 'native/package/LibMpv.Managed.Native.WinX64.csproj') -Raw
$consumerRoot = [IO.Directory]::CreateTempSubdirectory('libmpv-consumer-').FullName
$consumer = Join-Path $consumerRoot 'Consumer.csproj'
$packages = Join-Path $consumerRoot 'packages'
$defaultItems = '$(DefaultItemExcludes)'

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <SelfContained>false</SelfContained>
    <DefaultItemExcludes>${defaultItems};packages/**;publish/**</DefaultItemExcludes>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LibMpv.Managed" Version="[$($managed.Project.PropertyGroup.Version)]" />
    <PackageReference Include="LibMpv.Managed.Native.WinX64" Version="[$($native.Project.PropertyGroup.Version)]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $consumer -Encoding utf8

function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)." }
}

Copy-Item -LiteralPath (Join-Path $sourceRoot 'tests/Smoke/Program.cs') -Destination $consumerRoot
$sourceArguments = @('--source', $Source)
foreach ($additional in $AdditionalSources) {
    $sourceArguments += @('--source', $additional)
}

Invoke-Dotnet @(@('restore', $consumer, '--packages', $packages, '--no-http-cache', '-p:NuGetAudit=false') + $sourceArguments)
Invoke-Dotnet 'run' '--project' $consumer '--no-restore' '-c' 'Release'
if ($OverrideDll) {
    Invoke-Dotnet 'run' '--project' $consumer '--no-restore' '-c' 'Release' '--' $OverrideDll
}

Invoke-Dotnet @(@('restore', $consumer, '--packages', $packages, '-r', 'win-x64', '-p:SelfContained=false', '-p:EnableRuntimePackDownload=false', '-p:NuGetAudit=false') + $sourceArguments)
$publish = Join-Path $consumerRoot 'publish'
Invoke-Dotnet 'publish' $consumer '--no-restore' '-r' 'win-x64' '--self-contained' 'false' '-c' 'Release' '-o' $publish
$copies = @(Get-ChildItem -LiteralPath $publish -Filter 'libmpv-2.dll' -File -Recurse)
if ($copies.Count -ne 1 -or !(Test-Path -LiteralPath (Join-Path $publish 'libmpv-notices/RUNTIME.md'))) {
    throw 'The publish output must contain one libmpv DLL and its source notice.'
}

$hashFile = Join-Path $publish 'libmpv-notices/runtime.sha256'
$expected = ((Get-Content -LiteralPath $hashFile -Raw) -split '\s+')[0]
if ($expected -notmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $copies[0].FullName).Hash -ne $expected) {
    throw 'Published DLL does not match its accompanying SHA-256.'
}
Invoke-Dotnet (Join-Path $publish 'Consumer.dll')
Write-Output "Verified fresh restore and publish: $consumerRoot"
