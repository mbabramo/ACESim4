# Installed tools and container deployment

The tools below are system prerequisites. They are never copied into the article output or computational-input bundle. The C# application and its NuGet dependencies are built normally.

## Windows

Install the SDK version in [`global.json`](../global.json) (currently .NET SDK 10.0.401), plus the .NET 9 runtime for the application's `net9.0` target. Install [MiKTeX](https://miktex.org/download) or [TeX Live](https://tug.org/texlive/), including LuaLaTeX, BibTeX, Latin Modern, standalone, PGF/TikZ/PGFPlots, booktabs, tabularx and microtype. Install [Poppler](https://poppler.freedesktop.org/) command-line tools through your preferred Windows package distributor. Put their executable directories on PATH.

## Linux

Install the same .NET SDK and runtime from [Microsoft's installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux). On Debian/Ubuntu install the rendering dependencies:

```sh
sudo apt-get update
sudo apt-get install texlive-luatex texlive-latex-extra texlive-pictures texlive-fonts-extra lmodern fonts-lmodern fonts-clear-sans poppler-utils
```

Standard reports also use [Clear Sans](https://packages.debian.org/bookworm/fonts-clear-sans); both its TeX support and actual font files are required. Other distributions can install equivalent packages. Check prerequisites before any expensive calculation:

```sh
dotnet run --project ArticleReplication -c Release -- doctor --output /absolute/path/new-preflight
```

This records tool versions and executable/font hashes, compiles a font/TikZ test, renders a PNG and merges two PDF pages. A missing tool fails with logs. No automatic tool installation occurs. A downloaded source archive works without Git: `rebuild` snapshots and hashes the actual source files before compilation.

## Build the container through MSBuild

Install a Docker engine that runs Linux containers. From the source root:

```sh
dotnet build ArticleReplication/ArticleReplication.csproj -c Release -m:1 \
  -p:BuildReplicationContainer=true \
  -p:ArticleContainerTag=acesim-correlated-signals:local \
  -p:ArticleContainerRecords=/absolute/path/new-container-build-records
```

The records directory must be outside the source directory. In PowerShell place the command on one line or use PowerShell continuation syntax. Ordinary `dotnet build` does not require Docker; the property explicitly enables the image build. The C# build command freezes and hashes the build context, invokes Docker, and records the image identity and logs. Base image digests are pinned in `Containerfile`; installed Debian package versions are recorded inside the image at `/usr/share/article-replication/toolchain-packages.txt`. APT package resolution is not yet pinned to an immutable repository snapshot.

```sh
docker run --rm --cpus=1 \
  --mount type=bind,source=/absolute/path/output-parent,target=/output \
  acesim-correlated-signals:local doctor --output /output/new-linux-preflight
```

The image contains the application, runtime and rendering tools; it needs no network access at execution time. Input mounts should be read-only. For the currently implemented saved-results stages:

```sh
docker run --rm --network=none --cpus=4 \
  --mount type=bind,source=/absolute/path/inputs,target=/inputs,readonly \
  --mount type=bind,source=/absolute/path/output-parent,target=/output \
  acesim-correlated-signals:local reproduce \
  --solutions /inputs/solutions-v1 --histories /inputs/histories-v1 \
  --settings /inputs/selected-steps.json --external-jobs /inputs/external-jobs.json \
  --output /output/new-run --workers 4 --other-workers 2
```

On shared hosts choose workers to leave the total, including other calculations, at or below 32. Each numerical child process remains single-threaded. Docker CPU limits and declared workers must agree. Native and container runs use the same validation code. Output directories must be fresh; there is no automatic deletion or live-repository replacement.

## Scope of the current implementation

The container makes the implemented stages portable; it does not fill the outstanding scientific/report-generation integrations. Read [README.md](README.md) and [PortableReplicationContract.md](PortableReplicationContract.md) before interpreting a successful selected-stage run. In particular, old saved-results bundles still include some data-bearing presentation sources; they are not the final computation-only input contract. Complete fresh replication and final migration remain gated on those integrations and the two externally reserved simulations. No deployment command launches replacements for those protected jobs.
