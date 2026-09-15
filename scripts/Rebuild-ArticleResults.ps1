param(
    [switch]$DiagramsOnly,
    [switch]$SourcesOnly,
    [switch]$List,
    [string]$ResultsDirectory,
    [int]$Processors = [Environment]::ProcessorCount
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$results = if ($ResultsDirectory) { [IO.Path]::GetFullPath($ResultsDirectory) } else { Join-Path $repo 'ReportResults' }
$batch = Join-Path $results 'Run records\Retained study'
Push-Location -LiteralPath $repo
try {
    if (!$DiagramsOnly -and !$List) {
        dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Production build failed.' }
        & (Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe') run-suite --processors $Processors --hidden-workers --results-directory $batch
        if ($LASTEXITCODE -ne 0) { throw 'Production failed; inspect run records before restarting.' }
    }
    dotnet build LitigCharts -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Diagram build failed.' }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    $inputs = @('CS004','CS006EF') | ForEach-Object {
        [ordered]@{ NumericalResultsCsv="Run records/Retained study/$_ numerical results.csv";
            IndividualDirectory='Run records/Retained study'; ReportPrefix=$_ }
    }
    [ordered]@{Inputs=@($inputs);OutputDirectory='Aggregated Data';RequireCompleteRoutineMatrix=$true} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $results 'welfare-exhibits.json') -Encoding utf8
    [ordered]@{UseArticleResultsLayout=$true;WelfareExhibitsRequest='welfare-exhibits.json';
        MaxParallelCompilers=$Processors;ProcessTimeoutSeconds=300;LatexExecutable='lualatex';PreviewExecutable='pdftoppm'} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'article-diagrams.json') -Encoding utf8
    $diagramArgs = @('diagrams','results','--config',(Join-Path $results 'article-diagrams.json'),'--jobs',"$Processors")
    if ($SourcesOnly) { $diagramArgs += '--sources-only' }
    if ($List) { $diagramArgs += '--list' }
    & (Join-Path $repo 'LitigCharts\bin\Release\net9.0\LitigCharts.exe') @diagramArgs
    if ($LASTEXITCODE -ne 0) { throw 'Article diagrams failed.' }
    if (!$List) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Article-results.md') -Destination (Join-Path $results 'README.md') -Force }
}
finally { Pop-Location }
