param(
    [Parameter(Mandatory)][string]$DataFolder,
    [Parameter(Mandatory)][string]$LoadOrder,
    [string]$PatcherPath = "$PSScriptRoot\..\BaldursGateStyleOblivion\bin\Debug\net10.0\BaldursGateStyleOblivion.exe"
)
$ErrorActionPreference = 'Stop'
$PatcherPath = (Resolve-Path $PatcherPath).Path
$root = Join-Path "$PSScriptRoot\..\artifacts\foundation-checks" ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root -Force | Out-Null

function Run-Case($name, $settings) {
    $folder = Join-Path $root $name
    New-Item -ItemType Directory $folder | Out-Null
    $settings | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $folder 'settings.json')
    & $PatcherPath run-patcher -g Oblivion -d $DataFolder -l $LoadOrder -e $folder -o (Join-Path $folder 'Test.esp') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$name failed" }
    $bytes = [IO.File]::ReadAllBytes((Join-Path $folder 'Test.esp'))
    if ($bytes.Length -ne 38 -or [BitConverter]::ToUInt32($bytes, 30) -ne 0) { throw 'Expected empty patch' }
    if (!(Test-Path (Join-Path $folder 'Reports\Test.log'))) { throw 'Persistent log missing' }
    return $folder
}

$folder = Run-Case 'include' @{ IncludedPlugins = @('Knights.esp'); ExcludedPlugins = @() }
$summary = Get-Content (Join-Path $folder 'Reports\Test.diagnostic-summary.json') -Raw | ConvertFrom-Json
if ($summary.RecordCounts.spells -eq 0) { throw 'Include filter lost Knights spells' }
Get-ChildItem (Join-Path $folder 'Reports') -Filter '*.json' | ForEach-Object {
    $report = Get-Content $_.FullName -Raw | ConvertFrom-Json
    if (@($report.Records | Where-Object { $_ -and $_.SourcePlugin -ne 'Knights.esp' }).Count) { throw 'Include filter failed' }
}
$spell = (Get-Content (Join-Path $folder 'Reports\Test.spells.json') -Raw | ConvertFrom-Json).Records |
    Where-Object EditorID -eq 'NDAbArmorCumulative'
if ($spell.Original.Effects[0].MagicSchool -ne 'Alteration') { throw 'Excluded master data unavailable' }
$hash = (Get-FileHash (Join-Path $folder 'Reports\Test.diagnostic-summary.json')).Hash
& $PatcherPath run-patcher -g Oblivion -d $DataFolder -l $LoadOrder -e $folder -o (Join-Path $folder 'Test.esp') | Out-Null
if ($LASTEXITCODE -ne 0 -or $hash -ne (Get-FileHash (Join-Path $folder 'Reports\Test.diagnostic-summary.json')).Hash) { throw 'Repeat run differs' }

$folder = Run-Case 'exclude-wins' @{ IncludedPlugins = @('Knights.esp'); ExcludedPlugins = @('knights.ESP') }
$summary = Get-Content (Join-Path $folder 'Reports\Test.diagnostic-summary.json') -Raw | ConvertFrom-Json
if (($summary.RecordCounts.PSObject.Properties.Value | Measure-Object -Sum).Sum -ne 0) { throw 'Exclusion precedence failed' }

$folder = Run-Case 'classification-off' @{ IncludedPlugins = @('Knights.esp'); EnableActorClassification = $false; ReportOnly = $false }
if (Test-Path (Join-Path $folder 'Reports\Test.actor-classifications.json')) { throw 'Classification switch ignored' }
$summary = Get-Content (Join-Path $folder 'Reports\Test.diagnostic-summary.json') -Raw | ConvertFrom-Json
if ($summary.ActorClassificationEnabled -or $summary.ReportOnly) { throw 'Settings not loaded' }
if (($summary.ActorClassificationCoverage.PSObject.Properties.Value | Measure-Object -Sum).Sum -ne 0) { throw 'Disabled classification assigned values' }

$folder = Run-Case 'diagnostics-off' @{ EnableDiagnostics = $false; EnableActorClassification = $false }
if (@(Get-ChildItem (Join-Path $folder 'Reports') -Filter '*.json').Count) { throw 'Diagnostics switch ignored' }

$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::ChangeExtension($PatcherPath, '.dll'))
$settings = [Activator]::CreateInstance($assembly.GetType('BaldursGateStyleOblivion.Core.PatcherSettings'))
if ($settings.AllowsModule($true, $true) -or !$settings.AllowsModule($true, $false)) { throw 'Report-only gate failed' }
$settings.ReportOnly = $false
if (!$settings.AllowsModule($true, $true) -or $settings.AllowsModule($false, $false)) { throw 'Module gate failed' }
Write-Output "Foundation checks passed. Outputs: $root"
