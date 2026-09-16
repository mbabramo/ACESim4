param(
    [Parameter(Mandatory=$true)][string]$ResultsSource,
    [Parameter(Mandatory=$true)][string]$ArticleDirectory,
    [string]$Python = 'python',
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$source = (Resolve-Path -LiteralPath $ResultsSource).Path.TrimEnd('\')
$article = (Resolve-Path -LiteralPath $ArticleDirectory).Path.TrimEnd('\')
$target = [IO.Path]::GetFullPath((Join-Path $article 'Results'))
if ($source -eq $target -or $source.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a separately generated Results source.'
}
$records = Join-Path $source 'Run records'
if (!(Test-Path -LiteralPath (Join-Path $records 'diagram-inventory.json'))) { throw 'No diagram inventory.' }
$matrix = Join-Path $records 'routine-case-matrix.json'
Push-Location -LiteralPath $repo
try {
    dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Case-matrix build failed.' }
    & (Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe') export-case-matrix --output $matrix
    if ($LASTEXITCODE -ne 0) { throw 'Case-matrix export failed.' }
    $verify = Join-Path $PSScriptRoot 'publish_article_results.py'
    $arguments = @('-B',$verify,'verify','--results',$source,'--matrix',$matrix)
    if (Test-Path -LiteralPath $target) { $arguments += @('--existing',$target) }
    & $Python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Source verification failed; article Results was not replaced.' }
    if ($VerifyOnly) { return }

    # Never replace a collection while an incremental importer is writing to it.
    $writers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq 'LitigCharts.exe' -and $_.CommandLine -like '*completed-cases*' -and
        $_.CommandLine.Contains($target,[StringComparison]::OrdinalIgnoreCase)
    })
    if ($writers.Count) { throw 'Wait for the incremental case importer to finish before publishing.' }

    # Stage and verify a full copy before touching the existing Results directory.
    $stage = Join-Path $article ('.results-import-'+[guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $source -Destination $stage -Recurse
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($source,$file.FullName)
        $copy = Join-Path $stage $relative
        if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash) { throw "Staged copy mismatch: $relative" }
    }
    foreach ($path in @($stage,$target)) {
        $full = [IO.Path]::GetFullPath($path)
        if (!$full.StartsWith($article+'\',[StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe target: $full" }
        if (Test-Path -LiteralPath $full) {
            $item = Get-Item -LiteralPath $full
            if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
                (Resolve-Path -LiteralPath $full).Path -ne $full) { throw "Unexpected resolved target: $full" }
        }
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    Move-Item -LiteralPath $stage -Destination $target
    [ordered]@{ImportedUtc=[datetime]::UtcNow.ToString('o');SourceRoot=$source;ImportedRoot=$target;
        Verification='Every source file was copied and SHA256 checked before replacing Results.';
        Provenance='Historical source paths and fingerprints are retained; resolve diagram paths relative to the inventory Root.'} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'Run records\import-provenance.json') -Encoding utf8
    & $Python -B $verify refresh-main --article $article
    if ($LASTEXITCODE -ne 0) { throw 'Results imported, but main-exhibit refresh needs repair.' }
    Write-Output 'Verified routine Results imported; numbered routine exhibits refreshed. Separate analyses and manuscript prose retained.'
}
finally { Pop-Location }
