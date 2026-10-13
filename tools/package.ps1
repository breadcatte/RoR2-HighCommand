# Builds the mod and packs it for Thunderstore / r2modman: dist\HighCommand-<version>.zip
#   manifest.json, icon.png, README.md, CHANGELOG.md, HighCommand.dll, profiles\hero_profiles.json
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# 1. build (refuses to install while the game runs, which is fine: we only want the dll)
& (Join-Path $PSScriptRoot 'build.ps1')

$manifest = Get-Content (Join-Path $root 'package\manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number

# the plugin must report the same version as the manifest
$pluginSrc = Get-Content (Join-Path $root 'src\Plugin.cs') -Raw
if ($pluginSrc -notmatch ('Version = "' + [regex]::Escape($version) + '"')) {
    throw "Plugin.Version does not match manifest version $version"
}

if (-not (Test-Path (Join-Path $root 'package\icon.png'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }

$stage = Join-Path $root 'dist\stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $root 'package\manifest.json') $stage
Copy-Item (Join-Path $root 'package\icon.png') $stage
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'CHANGELOG.md') $stage
Copy-Item (Join-Path $root 'bin\Release\netstandard2.1\HighCommand.dll') $stage
if (Test-Path (Join-Path $root 'package\profiles')) {
    Copy-Item (Join-Path $root 'package\profiles') $stage -Recurse
}

$zip = Join-Path $root "dist\HighCommand-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Compress-Archive (PowerShell 5.1) writes entries like "profiles\x.json": mod managers expect "/" in zip paths
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Host "Package: $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName.Substring($stage.Length + 1) + "  (" + $_.Length + " bytes)") }

