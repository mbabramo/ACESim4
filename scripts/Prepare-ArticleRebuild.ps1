param(
    [Parameter(Mandatory=$true)][string[]]$EquilibriumSourceDirectory,
    [string]$ProvenanceFile = (Join-Path ([IO.Path]::GetTempPath()) 'acesim-equilibrium-reuse.json')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$reportRoot = [IO.Path]::GetFullPath((Join-Path $repo 'ReportResults'))
if ($reportRoot -ne (Join-Path $repo 'ReportResults')) { throw 'Unexpected ReportResults target.' }
if (Get-Process -Name ACESimDistributed,ACESimDistributedSaturate -ErrorAction SilentlyContinue) {
    throw 'Stop the existing production run before preparing a clean rebuild.'
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ('acesim-seed-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$entries = @{}
foreach ($sourceDirectory in $EquilibriumSourceDirectory) {
    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File -Filter '*-equ.csv') {
        if ($file.Name -notmatch '^(CS004|CS006EF) Specification-' -or $file.Name -match 'Specification-Mandatory') { continue }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($entries.ContainsKey($file.Name)) {
            if ($entries[$file.Name].Sha256 -ne $hash) { throw "Conflicting equilibrium copies: $($file.Name)" }
            continue
        }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name)
        $entries[$file.Name] = [ordered]@{ FileName=$file.Name; Source=$file.FullName; Sha256=$hash; Bytes=$file.Length }
    }
}
if ($entries.Count -ne 124) { throw "Expected 124 retained equilibria; found $($entries.Count). ReportResults was not cleared." }
$records = @($entries.Values | Sort-Object { $_.FileName })
[ordered]@{ CreatedUtc=[datetime]::UtcNow.ToString('o'); Count=$records.Count; Files=$records } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ProvenanceFile -Encoding utf8
# Every destructive target must be an existing, resolved child of the explicit report root.
if (Test-Path -LiteralPath $reportRoot) {
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
if ((Get-ChildItem -LiteralPath $reportRoot -File -Recurse).Count -ne 124) { throw 'Unexpected non-equilibrium files after cleanup.' }
$resolvedStage = (Resolve-Path -LiteralPath $staging).Path
if (!$resolvedStage.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup.' }
Remove-Item -LiteralPath $resolvedStage -Recurse -Force
Write-Output "Prepared $batch with 124 verified equilibrium copies and no other files. Provenance: $ProvenanceFile"
