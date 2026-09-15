param(
    [Parameter(Mandatory=$true)][string[]]$EquilibriumSourceDirectory,
    [string]$ResultsDirectory,
    [string]$ProvenanceFile = (Join-Path ([IO.Path]::GetTempPath()) 'acesim-equilibrium-reuse.json')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$isolated = ![string]::IsNullOrWhiteSpace($ResultsDirectory)
$reportRoot = if ($isolated) { [IO.Path]::GetFullPath($ResultsDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repo 'ReportResults')) }
if ($isolated -and (Test-Path -LiteralPath $reportRoot)) { throw 'An isolated rebuild requires a new results directory.' }
if (!$isolated -and $reportRoot -ne (Join-Path $repo 'ReportResults')) { throw 'Unexpected ReportResults target.' }
if (Get-Process -Name ACESimDistributed,ACESimDistributedSaturate -ErrorAction SilentlyContinue) {
    throw 'Stop the existing production run before preparing a clean rebuild.'
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ('acesim-seed-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
Push-Location -LiteralPath $repo
try {
    dotnet build ACESimDistributedSaturate -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Case-matrix build failed. ReportResults was not cleared.' }
    $matrixPath = Join-Path $staging 'routine-case-matrix.json'
    & (Join-Path $repo 'ACESimDistributedSaturate\bin\Release\net9.0\ACESimDistributedSaturate.exe') export-case-matrix --output $matrixPath
    if ($LASTEXITCODE -ne 0) { throw 'Case-matrix validation failed. ReportResults was not cleared.' }
    $matrix = @(Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json)
    $expected = @{}
    foreach ($case in $matrix) { $expected[$case.EquilibriumFileName] = $true }
}
finally { Pop-Location }
$entries = @{}
foreach ($sourceDirectory in $EquilibriumSourceDirectory) {
    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File -Filter '*-equ.csv') {
        if ($file.Name -notmatch '^(CS004|CS006EF) Specification-' -or $file.Name -match 'Specification-Mandatory') { continue }
        if (!$expected.ContainsKey($file.Name)) { throw "Unrecognized routine equilibrium: $($file.Name). ReportResults was not cleared." }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($entries.ContainsKey($file.Name)) {
            if ($entries[$file.Name].Sha256 -ne $hash) { throw "Conflicting equilibrium copies: $($file.Name)" }
            continue
        }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name)
        $entries[$file.Name] = [ordered]@{ FileName=$file.Name; Source=$file.FullName; Sha256=$hash; Bytes=$file.Length }
    }
}
if ($entries.Count -eq 0) { throw 'No reusable routine equilibria found. ReportResults was not cleared.' }
$records = @($entries.Values | Sort-Object { $_.FileName })
$missing = @($matrix | Where-Object { !$entries.ContainsKey($_.EquilibriumFileName) })
[ordered]@{ CreatedUtc=[datetime]::UtcNow.ToString('o'); Count=$records.Count; Files=$records;
    PlannedCount=$matrix.Count; MissingCount=$missing.Count; MissingCases=$missing } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ProvenanceFile -Encoding utf8
# Every destructive target must be an existing, resolved child of the explicit report root.
if (Test-Path -LiteralPath $reportRoot) {
    if ($isolated) { throw 'The isolated destination was created by another process; no files were removed.' }
    foreach ($child in Get-ChildItem -LiteralPath $reportRoot -Force) {
        $resolved = (Resolve-Path -LiteralPath $child.FullName).Path
        if (!$resolved.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unsafe cleanup target: $resolved" }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
$batch = Join-Path $reportRoot 'Run records\Retained study'
New-Item -ItemType Directory -Path $batch -Force | Out-Null
foreach ($entry in $records) {
    $destination = Join-Path $batch $entry.FileName
    Copy-Item -LiteralPath (Join-Path $staging $entry.FileName) -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Seed hash mismatch.' }
}
if ((Get-ChildItem -LiteralPath $reportRoot -File -Recurse).Count -ne $records.Count) { throw 'Unexpected non-equilibrium files after cleanup.' }
$resolvedStage = (Resolve-Path -LiteralPath $staging).Path
if (!$resolvedStage.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup.' }
Remove-Item -LiteralPath $resolvedStage -Recurse -Force
Write-Output "Prepared $batch with $($records.Count) verified equilibrium copies; $($missing.Count) planned cases need solving. Provenance: $ProvenanceFile"
