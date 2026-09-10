param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [ValidateSet('CorrelatedSignals', 'EndogenousDisputes')]
    [string]$Model = 'CorrelatedSignals'
)
$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
foreach ($command in @('dotnet', 'lualatex', 'pdftoppm')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command is not available: $command"
    }
}
$switch = if ($Model -eq 'CorrelatedSignals') { '--article-game-trees' } else { '--endogenous-game-tree' }
& dotnet run --project (Join-Path $repository 'ACESimConsole/ACESimConsole.csproj') --configuration Release --framework net9.0 -- $switch $destination
if ($LASTEXITCODE -ne 0) { throw 'C# tree generation failed.' }
$stems = if ($Model -eq 'CorrelatedSignals') {
    @('game tree 2x2x2', 'game tree 2x2x2 beginning', 'game tree 2x2x2 end',
      'game tree 2x2x2 simplified', 'game tree 2x2x2 beginning simplified', 'game tree 2x2x2 end simplified')
} else { @('endogenous disputes beginning') }
$previews = if ($Model -eq 'CorrelatedSignals') {
    @('game tree 2x2x2 beginning', 'game tree 2x2x2 end')
} else { @('endogenous disputes beginning') }
# Only deliverables are copied back from this isolated compilation directory.
$buildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('acesim-game-trees-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $buildDirectory | Out-Null
try {
    foreach ($stem in $stems) {
        & lualatex --interaction=nonstopmode --halt-on-error "--output-directory=$buildDirectory" (Join-Path $destination ($stem + '.tex'))
        if ($LASTEXITCODE -ne 0) { throw "LaTeX compilation failed: $stem" }
    }
    foreach ($stem in $stems) {
        Copy-Item -LiteralPath (Join-Path $buildDirectory ($stem + '.pdf')) -Destination $destination
    }
    foreach ($stem in $previews) {
        & pdftoppm -png -scale-to 2200 (Join-Path $destination ($stem + '.pdf')) (Join-Path $destination $stem)
        if ($LASTEXITCODE -ne 0) { throw "Preview rendering failed: $stem" }
    }
    if ($Model -eq 'CorrelatedSignals') {
        $sourceCommit = & git -C $repository rev-parse HEAD
        $sourceState = & git -C $repository status --porcelain -- ACESimBase ACESimConsole scripts/Generate-ArticleGameTrees.ps1
        $note = if ($sourceState) { ' (source changes were present)' } else { ' (clean source)' }
        $lineFeed = [string][char]10
        [System.IO.File]::AppendAllText(
            (Join-Path $destination 'README.md'),
            ($lineFeed + $lineFeed + "Generated from ACESim4 commit " + $sourceCommit + $note + "." + $lineFeed))
    }
}
finally {
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $resolvedBuild = [System.IO.Path]::GetFullPath($buildDirectory)
    if (-not $resolvedBuild.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolvedBuild -Leaf) -notlike 'acesim-game-trees-*' -or
        ((Get-Item -LiteralPath $resolvedBuild).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to delete an unexpected temporary directory: $resolvedBuild"
    }
    Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
}
