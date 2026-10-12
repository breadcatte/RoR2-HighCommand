# Runs one scripted in-game scenario and prints the interesting log lines.
#   .\tools\qa-run.ps1 perf            builds + installs, runs tools\qa\perf.txt, prints the result
#   .\tools\qa-run.ps1 perf -NoBuild   skip the build (the DLL is already installed)
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [switch]$NoBuild,
    [int]$TimeoutMin = 12
)

$root = Split-Path -Parent $PSScriptRoot
$game = "C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2"
$src = Join-Path $root "tools\qa\$Name.txt"
if (-not (Test-Path $src)) { throw "No such scenario: $src" }
if (Get-Process | Where-Object { $_.ProcessName -like "Risk of Rain*" }) { throw "The game is running." }

if (-not $NoBuild) { & (Join-Path $root "tools\build.ps1") | Out-Null }

# hc_cfg tests are saved into the .cfg on exit: always start from the defaults
$cfg = Join-Path $game "BepInEx\config\kantemiir.HighCommand.cfg"
if (Test-Path $cfg) { [System.IO.File]::Delete($cfg) }

$dst = Join-Path $game "BepInEx\config\HighCommand.autoexec.txt"
Copy-Item $src $dst -Force
Clear-Content (Join-Path $game "BepInEx\LogOutput.log") -ErrorAction SilentlyContinue

& (Join-Path $root "tools\run-game.ps1") -TimeoutMin $TimeoutMin -Pattern "AutoExec: (quit|script finished)" -Max 2 | Out-Null
Move-Item $dst (Join-Path $game "BepInEx\config\HighCommand.autoexec.last.txt") -Force

$lines = Get-Content (Join-Path $game "BepInEx\LogOutput.log") -Encoding UTF8
$start = ($lines | Select-String "AutoExec: running" | Select-Object -First 1).LineNumber
$noise = "multiple NetworkIdentity|JobTempAlloc|AkGameObjManager|older than|screenshot ->|Hero profiles|AutoExec>"
$inStatus = $false
for ($i = [Math]::Max(0, $start - 1); $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match "AutoExec: ===|hc_perf:|HighCommand status:|HighCommand flow|HighCommand: |\[Error|HighCommand\]|Exception|NullRef") {
        if ($l -notmatch $noise) { if ($l.Length -gt 300) { $l.Substring(0, 300) } else { $l } }
        $inStatus = ($l -match "HighCommand status:")
    } elseif ($inStatus -and $l -notmatch "^\[") {
        if ($l.Trim().Length -gt 0) { if ($l.Length -gt 300) { $l.Substring(0, 300) } else { $l } }
    } else { $inStatus = $false }
}
