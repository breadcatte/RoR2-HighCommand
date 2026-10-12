# Launches the game through Steam, waits until it exits (the autoexec script ends with @quit), then prints the log.
#   .\tools\run-game.ps1                         run, print HighCommand-related log lines
#   .\tools\run-game.ps1 -Pattern "Error|Exception" -Max 80
param(
    [int]$TimeoutMin = 8,
    [string]$Pattern = "HighCommand|AutoExec|hc_",
    [int]$Max = 200,
    [switch]$KeepRunning
)

$game = "C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2"
$log = Join-Path $game "BepInEx\LogOutput.log"

if (Get-Process | Where-Object { $_.ProcessName -like "Risk of Rain*" }) {
    Write-Host "The game is already running; reading the current log only."
} else {
    Start-Process "steam://rungameid/632360"
}

$deadline = (Get-Date).AddMinutes($TimeoutMin)
$started = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 4
    $p = Get-Process | Where-Object { $_.ProcessName -like "Risk of Rain*" }
    if ($p) { $started = $true } elseif ($started) { break }
}
$still = Get-Process | Where-Object { $_.ProcessName -like "Risk of Rain*" }
if ($still -and -not $KeepRunning) {
    Write-Host "Timeout: closing the game."
    $still | Stop-Process -Force
    Start-Sleep -Seconds 2
}

Write-Host "---- log (pattern: $Pattern)"
Get-Content $log -ErrorAction SilentlyContinue | Where-Object { $_ -match $Pattern } | Select-Object -First $Max
