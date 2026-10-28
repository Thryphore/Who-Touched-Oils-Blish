# Quick offline parse of the newest Deimos .zevtc using a tiny compiled helper.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "DeimosOilSummary\DeimosOilSummary.csproj"
dotnet build $proj -c Release | Out-Host

$dll = Join-Path $root "DeimosOilSummary\bin\Release\net48\DeimosOilSummary.dll"
Add-Type -Path $dll

$logRoot = Join-Path $env:USERPROFILE "OneDrive\Documents\Guild Wars 2\addons\arcdps\arcdps.cbtlogs\Deimos (17154)"
$latest = Get-ChildItem $logRoot -Filter *.zevtc | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Parsing $($latest.FullName)"
$result = [DeimosOilSummary.Services.EvtcOilParser]::Parse($latest.FullName)
Write-Host "Success=$($result.Success) TotalHits=$($result.TotalHits) Error=$($result.ErrorMessage)"
foreach ($p in $result.Players) {
    Write-Host ("  {0} = {1}" -f $p.DisplayName, $p.HitCount)
}
