# Builds HighCommand and installs it into the game's BepInEx plugins folder.
#   .\tools\build.ps1            build + install
#   .\tools\build.ps1 -NoInstall build only
param(
    [switch]$NoInstall,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $env:LOCALAPPDATA "dotnet-sdk"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source } else { $env:DOTNET_ROOT = $dotnetRoot }

Push-Location $root
try {
    $out = & $dotnet build "$root\HighCommand.csproj" -c $Configuration --nologo -v minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Where-Object { $_ -match "error|warning CS" } | Select-Object -Unique | ForEach-Object { Write-Host $_ }
        throw "Build failed."
    }
} finally { Pop-Location }

$dll = Join-Path $root "bin\$Configuration\netstandard2.1\HighCommand.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }
Write-Host "Built: $dll"

if ($NoInstall) { return }

$game = "C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2"
if (Get-Process | Where-Object { $_.ProcessName -like "Risk of Rain*" }) {
    throw "Risk of Rain 2 is running. Close the game before installing (the DLL is locked)."
}
$dest = Join-Path $game "BepInEx\plugins\local-HighCommand"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $dll (Join-Path $dest "HighCommand.dll") -Force

$profiles = Join-Path $root "profiles"
if (Test-Path $profiles) {
    $pdest = Join-Path $dest "profiles"
    New-Item -ItemType Directory -Force $pdest | Out-Null
    Copy-Item (Join-Path $profiles "*") $pdest -Recurse -Force
}
Write-Host "Installed to: $dest"
