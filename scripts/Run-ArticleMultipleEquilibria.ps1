param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$Processors = [Environment]::ProcessorCount
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$production = Join-Path $output 'Sources\Production'
$stateFile = Join-Path $output 'Sources\multiple-equilibria-run.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $stateFile) -Force | Out-Null
$state = [ordered]@{Status='Building';StartedUtc=[datetime]::UtcNow.ToString('o');
    Source=$repo;Output=$output;Processors=$Processors;Pid=$PID}
$state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
Push-Location -LiteralPath $repo
try {
    $changed = @(git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0 -or $changed.Count) { throw 'Run multiple-equilibrium production from a committed, clean source tree.' }
    $state.SourceCommit = (git rev-parse HEAD).Trim()
    dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Multiple-equilibrium build failed.' }
    $runner = Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe'
    $state.Status='Solving'
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    & $runner run --plan multiple-equilibria --processors $Processors --hidden-workers --results-directory $production
    if ($LASTEXITCODE -ne 0) { throw 'Multiple-equilibrium solving failed; inspect production records before resuming.' }
    $state.Status='Aggregating'
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    & $runner aggregate --plan multiple-equilibria --results-directory $production
    if ($LASTEXITCODE -ne 0) { throw 'Multiple-equilibrium aggregation failed.' }
    $state.Status='Rendering'
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    & (Join-Path $repo 'LitigCharts\bin\Release\net9.0\LitigCharts.exe') multiple-equilibria-report --input $production --output $output --jobs $Processors
    if ($LASTEXITCODE -ne 0) { throw 'Multiple-equilibrium report generation failed.' }
    $state.Status='Completed'
}
catch {
    $state.Status='Failed'
    $state.Error=$_.Exception.Message
    throw
}
finally {
    $state.EndedUtc=[datetime]::UtcNow.ToString('o')
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    Pop-Location
}
