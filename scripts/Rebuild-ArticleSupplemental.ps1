param(
    [string]$OutputDirectory,
    [string[]]$OriginalSolveLogDirectory = @(),
    [string]$Python = 'python',
    [int]$Processors = [Environment]::ProcessorCount,
    [switch]$SkipMultipleEquilibria,
    [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$OutputDirectory) { $OutputDirectory=Join-Path $repo 'SupplementalResults' }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
Push-Location -LiteralPath $repo
try {
    dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Supplemental build failed.' }
    if (!$SkipMultipleEquilibria -and !$PrepareOnly) {
        $multiple=Join-Path $OutputDirectory 'Multiple equilibria'
        $multipleProduction=Join-Path $multiple 'Sources\Production'
        $production=Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe'
        & $production run --plan multiple-equilibria --processors $Processors --hidden-workers --results-directory $multipleProduction
        if ($LASTEXITCODE -ne 0) { throw 'Multiple-start production failed.' }
        & $production aggregate --plan multiple-equilibria --results-directory $multipleProduction
        if ($LASTEXITCODE -ne 0) { throw 'Multiple-start aggregation failed.' }
        $charts=Join-Path $repo 'LitigCharts\bin\Release\net9.0\LitigCharts.exe'
        & $charts multiple-equilibria-report --input $multipleProduction --output $multiple --jobs $Processors
        if ($LASTEXITCODE -ne 0) { throw 'Multiple-start exhibits failed.' }
    }
    $arguments=@((Join-Path $PSScriptRoot 'rebuild_article_supplemental.py'),'--output',$OutputDirectory,'--jobs',"$Processors")
    foreach ($directory in $OriginalSolveLogDirectory) { $arguments+=@('--original-logs',$directory) }
    if ($PrepareOnly) { $arguments+='--prepare-only' }
    & $Python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Supplemental analyses failed; inspect supplemental-state.json and the workflow Sources/Run records, then resume the same command.' }
    if (!$PrepareOnly) {
        $verification=@((Join-Path $PSScriptRoot 'verify_article_supplemental.py'),'--output',$OutputDirectory)
        if ($SkipMultipleEquilibria) { $verification+='--changes-only' }
        & $Python @verification
        if ($LASTEXITCODE -ne 0) { throw 'Supplemental verification failed.' }
    }
}
finally { Pop-Location }
