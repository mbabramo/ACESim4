param([Parameter(Mandatory)][string]$Runtime,[Parameter(Mandatory)][string]$Pilot,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Tests require a fresh directory.'}
[IO.Directory]::CreateDirectory($Output)|Out-Null
$sourceRoot=Join-Path $Pilot 'article'
$copyRoot=Join-Path $Output 'article'
$request=Get-Content -LiteralPath (Join-Path $Pilot 'requests/standard-litigcharts.json') -Raw|ConvertFrom-Json
$request.ResultsDirectory=Join-Path $copyRoot 'Results'
$request.SupplementalDirectory=Join-Path $copyRoot 'Supplemental materials'
$requestFile=Join-Path $Output 'request.json'
$request|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $requestFile
$inventoryFile=Join-Path $request.ResultsDirectory 'Run records/standard-diagram-inventory.json'
$inventory=Get-Content -LiteralPath (Join-Path $sourceRoot 'Results/Run records/standard-diagram-inventory.json') -Raw|ConvertFrom-Json
function CopyFixture([string]$File){
    $destination=Join-Path $copyRoot ([IO.Path]::GetRelativePath($sourceRoot,$File))
    if(-not ([IO.Path]::GetFullPath($destination)).StartsWith($Output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture containment check failed.'}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
    if(-not (Test-Path -LiteralPath $destination)){Copy-Item -LiteralPath $File -Destination $destination}
    return $destination
}
foreach($a in $inventory.Artifacts){
    $sidecar=[IO.Path]::ChangeExtension($a.Source,'.json')
    if(Test-Path -LiteralPath $sidecar){CopyFixture $sidecar|Out-Null}
    CopyFixture (Join-Path ([IO.Path]::GetDirectoryName($a.Output)) 'Standard reports.md')|Out-Null
    foreach($field in @('Source','Output','Preview')){$a.$field=CopyFixture $a.$field}
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($inventoryFile))|Out-Null
$original=$inventory|ConvertTo-Json -Depth 100
[IO.File]::WriteAllText($inventoryFile,$original)
$records=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[bool]$ShouldPass){
    $report=Join-Path $Output ($Name+'.json')
    & dotnet $Runtime verify-standard --request $requestFile --output $report *> (Join-Path $Output ($Name+'.log'))
    $passed=$LASTEXITCODE -eq 0
    if($passed -ne $ShouldPass){throw "Unexpected coverage result: $Name"}
    $records.Add(@{Test=$Name;Passed=$true;ExpectedAcceptance=$ShouldPass;ExitCode=$LASTEXITCODE})
}
Check 'intact-copy' $true
$preview=$inventory.Artifacts[0].Preview
$saved=[IO.File]::ReadAllBytes($preview)
try { [IO.File]::WriteAllBytes($preview,[byte[]]@(1,2,3));Check 'damaged-preview-rejected' $false }
finally { [IO.File]::WriteAllBytes($preview,$saved) }
$pdf=$inventory.Artifacts[0].Output
if(-not ([IO.Path]::GetFullPath($pdf)).StartsWith($Output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Copy containment check failed.'}
try { Move-Item -LiteralPath $pdf -Destination ($pdf+'.test-held');Check 'missing-pdf-rejected' $false }
finally { Move-Item -LiteralPath ($pdf+'.test-held') -Destination $pdf }
try {
    $inventory.Artifacts=@($inventory.Artifacts|Select-Object -Skip 1)
    $inventory|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $inventoryFile
    Check 'omitted-inventory-entry-rejected' $false
} finally { [IO.File]::WriteAllText($inventoryFile,$original) }
Check 'restored-copy' $true
@{Passed=$true;FinishedUtc=[DateTime]::UtcNow.ToString('o');Tests=$records;OriginalInputsUntouched=$true}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $Output 'tests.json')
