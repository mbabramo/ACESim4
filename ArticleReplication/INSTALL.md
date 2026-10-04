# Container replication and native installation

## Run the published container

Install [Docker Desktop](https://docs.docker.com/desktop/) on Windows and select Linux containers, or install [Docker Engine](https://docs.docker.com/engine/install/) on Linux. Start Docker and ensure `docker version` shows a server. The release image is Linux x86-64; ARM emulation is not part of the tested platform. Allow four CPUs and sufficient memory for the full reporting run (the hosted validation runner has 16 GiB).

Download and extract [correlated-signals-saved-solutions.zip](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip). The extracted folder must directly contain `Equilibria`, `Search` and `Histories`. Create a separate empty output-parent folder. Replace the two host paths with absolute paths to those existing folders:

```sh
docker run --rm --network none --cpus 4 --mount "type=bind,source=/absolute/path/saved-solutions,target=/inputs,readonly" --mount "type=bind,source=/absolute/path/output,target=/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04 run --input /inputs --output /output/run --missing wait --workers 4
```

The command works on one line in PowerShell or a Linux shell. Windows host paths may be `C:/Replication/saved-solutions` and `C:/Replication/output`, including spaces inside the quoted mount argument. Docker downloads the public image automatically when needed; no registry login is required. Internet access is needed for downloading Docker, the image and optional solutions, but replication itself runs with networking disabled.

Read the generated research collection in the host output folder's `run/article`. The `run` subdirectory must be new. The manuscript remains separate. Choose fewer workers and matching `--cpus` if needed; each numerical child remains single-threaded. On Linux, the default container process writes files as root. The image includes the application, .NET runtime, TeX, fonts and PDF tools; the saved equilibrium/history files are separate optional inputs. They are mathematically checked on reuse; decompositions and reporting are recalculated.

The [image package](https://github.com/users/mbabramo/packages/container/package/acesim-correlated-signals) provides version tags and digests. The dated release tag identifies this article collection; a digest can be substituted for the tag to select the exact image. Source and the automated build/test/publish workflow are in the `correlated-signals` branch. The workflow publishes only after the complete saved-solutions run and native release checks pass.

## Native alternative

Use the following prerequisites only when running/building natively. They are already installed in the published image. They are never copied into the article output or computational-input bundle.

### Windows

Install the SDK version in [`global.json`](../global.json) (currently .NET SDK 10.0.401), plus the .NET 9 runtime for the application's `net9.0` target. Install [MiKTeX](https://miktex.org/download) or [TeX Live](https://tug.org/texlive/), including LuaLaTeX, BibTeX, Latin Modern, standalone, PGF/TikZ/PGFPlots, booktabs, tabularx and microtype. Install [Poppler](https://poppler.freedesktop.org/) command-line tools through your preferred Windows package distributor. Put their executable directories on PATH.

### Linux

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

The image contains the application, runtime and rendering tools; it needs no network access at execution time. Input mounts should be read-only:

```sh
docker run --rm --network=none --cpus=4 \
  --mount type=bind,source=/absolute/path/inputs,target=/inputs,readonly \
  --mount type=bind,source=/absolute/path/output-parent,target=/output \
  acesim-correlated-signals:local run \
  --input /inputs \
  --output /output/new-run --workers 4 --other-workers 2
```

On shared hosts choose workers to leave the total, including other calculations, at or below 32. Each numerical child process remains single-threaded. Docker CPU limits and declared workers must agree. Native and container runs use the same validation code. Output directories must be fresh; there is no automatic deletion or live-repository replacement.

## Scope of the current implementation

The public command reads only saved `.equ` and optional `.history` files. Omit the input mount and `--input` to solve afresh. Use `--missing wait` or explicit `--reserve-cases` when unavailable cases must remain pending. See [README.md](README.md) for the journal command, C#/CLI settings and selected stages. Default replication generates the research-output folders; manuscript compilation is an optional author step. A selected-stage test is not a whole-collection test. No machine-specific case reservations are implicit.

## Publishing a new tested image

The repository's `publish-correlated-signals-container.yml` workflow is triggered by a version tag such as `correlated-signals-container-2026-10-04` on the selected `correlated-signals` commit. Update `ContainerRelease.Image` and the instructions for a new version before tagging. The workflow uses the C# project's MSBuild container target, downloads the public saved-solutions archive, runs the complete default replication without network access, and compares complete strategies and numeric reports with a pinned published reference. That reference is used only for release testing, never as a required journal input. It pushes the dated image and a source-commit tag only after these checks pass.

GitHub's workflow token supplies registry access; no personal registry token is required. A newly created GitHub package is private by default, so its owner must set package visibility to Public once for anonymous journal downloads. The separate `check-public-correlated-signals-container.yml` workflow, triggered by a `correlated-signals-public-check-2026-10-04` tag, pulls with an empty Docker credential directory and runs the complete journal replication in a fresh directory. It verifies the scientific results and retains the entire generated collection, inventory and execution records as workflow artifacts. Do not reuse a published version tag for a different image.

Before replacing the article repository's research outputs, back up the existing repository and preserve its author-maintained manuscript folder. After synchronization, run the native read-only `verify-delivery` command described in [README.md](README.md). Every generated and published file must be accounted for with identical bytes, and both directory trees must match, including empty directories. Git status alone cannot detect leftover empty directories. These final delivery checks are separate from the equilibrium and scientific regression checks and are not required inputs to replication.
