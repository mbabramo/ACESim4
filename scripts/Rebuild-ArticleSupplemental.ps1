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
        $production=Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe'
        & $production run --plan multiple-equilibria --processors $Processors --hidden-workers --results-directory $multiple
        if ($LASTEXITCODE -ne 0) { throw 'Multiple-start production failed.' }
        & $production aggregate --plan multiple-equilibria --results-directory $multiple
        if ($LASTEXITCODE -ne 0) { throw 'Multiple-start aggregation failed.' }
    }
    $arguments=@((Join-Path $PSScriptRoot 'rebuild_article_supplemental.py'),'--output',$OutputDirectory,'--jobs',"$Processors")
    foreach ($directory in $OriginalSolveLogDirectory) { $arguments+=@('--original-logs',$directory) }
    if ($PrepareOnly) { $arguments+='--prepare-only' }
    & $Python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Supplemental analyses failed; inspect Run records and resume the same command.' }
}
finally { Pop-Location }
