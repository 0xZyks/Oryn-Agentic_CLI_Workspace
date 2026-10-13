# Build and install for the current Windows user. Supports Windows PowerShell 5.1+.
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime,
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Oryn'),
    [switch] $NoPath
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this installer on Windows.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install a .NET 10 SDK first.'
}
if (-not $Runtime) {
    $architecture = $env:PROCESSOR_ARCHITEW6432
    if (-not $architecture) { $architecture = $env:PROCESSOR_ARCHITECTURE }
    switch ($architecture) {
        'AMD64' { $Runtime = 'win-x64' }
        'ARM64' { $Runtime = 'win-arm64' }
        default { throw "Unsupported architecture: $architecture. Specify -Runtime." }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$InstallDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)
$publishDir = Join-Path ([IO.Path]::GetTempPath()) ('oryn-publish-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $publishDir | Out-Null
try {
    Push-Location -LiteralPath $repoRoot
    try {
        & dotnet publish src/Oryn.Cli/Oryn.Cli.csproj --configuration Release `
            --runtime $Runtime --self-contained true --output $publishDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
    }
    finally { Pop-Location }

    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'oryn.exe') -PathType Leaf)) {
        throw 'Publication did not produce oryn.exe.'
    }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $InstallDir -Recurse -Force
}
finally { Remove-Item -LiteralPath $publishDir -Recurse -Force }

if (-not $NoPath) {
    # Preserve the user PATH without copying the machine PATH into it.
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $userEntries = @($userPath -split ';' | Where-Object { $_ })
    $present = @($userEntries | Where-Object { $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') })
    if ($present.Count -eq 0) {
        $newUserPath = (@($InstallDir) + $userEntries) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
    }
    $processEntries = @($env:Path -split ';' | Where-Object { $_ })
    $present = @($processEntries | Where-Object { $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') })
    if ($present.Count -eq 0) { $env:Path = "$InstallDir;$env:Path" }
}
Write-Host "Installed $(Join-Path $InstallDir 'oryn.exe') ($Runtime)."
if (-not $NoPath) { Write-Host 'User PATH and current PowerShell PATH configured. Launch: oryn' }
