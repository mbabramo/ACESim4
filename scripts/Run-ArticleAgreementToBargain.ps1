param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$Workers = [Math]::Min(30, [Environment]::ProcessorCount),
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$production = Join-Path $output 'Sources\Production'
$stateFile = Join-Path $output 'Sources\agreement-run.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $stateFile) -Force | Out-Null
$state = [ordered]@{Status='Building';StartedUtc=[datetime]::UtcNow.ToString('o');
    Source=$repo;Output=$output;Workers=$Workers;Cases=30;StartsPerCase=1;Pid=$PID}
Push-Location -LiteralPath $repo
try {
    $changed = @(git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0 -or $changed.Count) { throw 'Use a clean reproducible source checkout.' }
    $state.SourceCommit = (git rev-parse HEAD).Trim()
    if (-not $SkipBuild) {
        dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }
    $runner = Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe'
    $state.Status='Solving'
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    & $runner run --plan agreement-to-bargain --processors $Workers --hidden-workers --results-directory $production
    if ($LASTEXITCODE -ne 0) { throw 'Production failed; completed work remains available for validated resumption.' }
    $state.Status='Aggregating'
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    & $runner aggregate --plan agreement-to-bargain --results-directory $production
    if ($LASTEXITCODE -ne 0) { throw 'Aggregation failed.' }
    $state.Status='SolvedAndAggregated'
}
catch { $state.Status='Failed'; $state.Error=$_.Exception.Message; throw }
finally {
    $state.EndedUtc=[datetime]::UtcNow.ToString('o')
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    Pop-Location
}
