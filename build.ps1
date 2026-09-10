# jrdp — build.ps1
#
# Publishes a self-contained, single-file jrdp.exe into dist/.
# Runs the SAME dotnet publish command as .github/workflows/release.yml, so a
# local build and a release build can't silently diverge.
#
#   ./build.ps1                        # host arch, version 0.0.0-dev
#   ./build.ps1 -Arch arm64
#   ./build.ps1 -Version 1.2.3         # stamp a real version
#   ./build.ps1 -Arch x64,arm64        # both (arm64 needs the cross-arch pack)
#
# Prerequisite: .NET 8 SDK — https://dotnet.microsoft.com/download

[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string[]]$Arch,

    [string]$Version = '0.0.0-dev'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot 'src/jrdp.csproj'
$distDir = Join-Path $repoRoot 'dist'

# Default to the host architecture: that's the only one guaranteed to be
# runnable on this machine for a smoke test.
if (-not $Arch) {
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $Arch = @($hostArch)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK not found on PATH. Install from https://dotnet.microsoft.com/download'
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

foreach ($a in $Arch) {
    $rid = "win-$a"
    Write-Host "[jrdp] publishing $rid (self-contained, single-file, v$Version)..."

    # -r overrides any RuntimeIdentifier in the csproj (a command-line MSBuild
    # property wins over the project file), so one csproj serves both arches.
    dotnet publish $csproj `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:Version=$Version `
        -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    $built = Join-Path $repoRoot "src/bin/Release/net8.0-windows/$rid/publish/jrdp.exe"
    if (-not (Test-Path $built)) { throw "expected artifact missing: $built" }

    $dest = Join-Path $distDir "jrdp-$rid.exe"
    Copy-Item $built $dest -Force

    $size = [math]::Round((Get-Item $dest).Length / 1MB, 1)
    Write-Host "[jrdp] SUCCESS: $dest ($size MB)"
}
