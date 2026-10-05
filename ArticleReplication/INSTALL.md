# Container replication and native installation

Choose either [Docker](#run-the-published-container), which includes the required tools, or [running without Docker](#run-without-docker), which installs those tools on your own computer. Both regenerate the same research-output folders, either by verifying saved solutions or by computing equilibria and histories from scratch. The manuscript is maintained separately.

## Run the published container

Install [Docker Desktop](https://docs.docker.com/desktop/) on Windows and select Linux containers, or install [Docker Engine](https://docs.docker.com/engine/install/) on Linux. Start Docker and ensure `docker version` shows a server. The release image is Linux x86-64; ARM emulation is not part of the tested platform. Allow four CPUs and sufficient memory for the full reporting run (the hosted validation runner has 16 GiB).

Create a new folder called `replication`. **If using saved solutions**, download [correlated-signals-saved-solutions.zip](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip) and extract its contents into `replication/solutions`. The `solutions` folder must directly contain `Equilibria`, `Search` and `Histories`.

To compute from scratch, skip the solutions download.

Open PowerShell on Windows, or a Linux terminal, in the `replication` folder. On Windows, right-click inside the folder and choose **Open in Terminal**, using a PowerShell tab. Choose one of these commands; no paths need editing.

**With saved solutions:**

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/solutions:/inputs:ro" -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1 run --input /inputs --output /output/run --missing wait --workers 4
```

**From scratch, without saved solutions:**

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1 run --output /output/run --missing compute --workers 4
```

This also calculates all equilibrium searches and solver histories and can take days or longer. It reads no saved inputs.

`${PWD}` means the current folder in both PowerShell and a Linux shell. Docker downloads the software and creates the output folder automatically; no registry login is required. Internet access is needed for downloading Docker, the software and saved solutions, but replication itself runs with networking disabled.

Leave the terminal open until the command finishes, then open **`replication/output/run/article`**. Use a new `replication` folder to repeat the exercise: an existing run is never overwritten. The manuscript remains separate. Choose fewer workers and matching `--cpus` if needed; each numerical child remains single-threaded. On Linux, the default container process writes files as root. The image includes the application, .NET runtime, TeX, fonts and PDF tools; the saved equilibrium/history files are separate optional inputs. They are mathematically checked on reuse; decompositions and reporting are recalculated.

The [image package](https://github.com/users/mbabramo/packages/container/package/acesim-correlated-signals) provides version tags and digests. The dated release tag identifies this article collection; a digest can be substituted for the tag to select the exact image. Source and the automated build/test/publish workflow are in the `correlated-signals` branch. The workflow publishes only after the complete saved-solutions run and native release checks pass.

## Run without Docker

This option downloads the C# source and builds it on your computer. You do not need Docker, Visual Studio, Git or a GitHub account. Install the tools once, then use the single reproduction command below. They stay installed on your computer; they are not copied into the results or saved-solutions folder.

### 1. Install the tools

**Windows (Intel/AMD, 64-bit)**

- **.NET:** install the **.NET SDK 10.0.401**, as specified in [`global.json`](../global.json), and the **.NET 9 Runtime**. Use Microsoft's [Windows installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/windows) and [downloads](https://dotnet.microsoft.com/en-us/download/dotnet). The SDK is needed to build the code; the separate version 9 runtime runs the application.
- **TeX and fonts:** install [TeX Live](https://tug.org/texlive/) with its full package selection. Alternatively, install [MiKTeX](https://miktex.org/howto/install-miktex) and use MiKTeX Console to install the required packages before running: LuaLaTeX, BibTeX, Latin Modern, [Clear Sans](https://ctan.org/pkg/clearsans), standalone, PGF/TikZ/PGFPlots, booktabs, tabularx and microtype. Clear Sans needs its fonts as well as its TeX package.
- **PDF tools:** download the binary ZIP from the [Poppler Windows releases](https://github.com/oschwartz10612/poppler-windows/releases), extract it to a permanent folder, and add its `Library/bin` folder to your user **Path**. In Windows search, open **Edit environment variables for your account**, select **Path**, then **Edit → New** and add that folder. It must contain `pdftoppm.exe` and `pdfunite.exe`.

Close and reopen your terminal after installation so that it sees the installed tools.

**Linux (Intel/AMD, 64-bit)**

Install **.NET SDK 10.0.401** and the **.NET 9 Runtime** using [Microsoft's Linux instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux). For Debian/Ubuntu, install the rendering tools and fonts with:

```sh
sudo apt-get update
sudo apt-get install texlive-luatex texlive-latex-extra texlive-pictures texlive-fonts-extra lmodern fonts-lmodern fonts-clear-sans poppler-utils
```

Other distributions can install equivalent packages. These TeX/font packages are large; allow sufficient disk space. No Python installation is required.

### 2. Download the code and, optionally, saved solutions

Create a new folder named `replication`. Download and extract:

- The [C# source ZIP for the correlated-signals branch](https://github.com/mbabramo/ACESim4/archive/refs/heads/correlated-signals.zip). Rename the extracted `ACESim4-correlated-signals` folder to **`code`** and put it inside `replication`. The `code` folder should directly contain `global.json` and `ArticleReplication`.
- **Only if using saved solutions:** the [saved-solutions ZIP](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip). Extract its contents into **`replication/solutions`**, which should directly contain `Equilibria`, `Search` and `Histories`.

With saved solutions, the folders should look like this. For a from-scratch run, omit `solutions`. Do not create `output` yourself:

```text
replication/
  code/
    global.json
    ArticleReplication/
    ...
  solutions/
    Equilibria/
    Search/
    Histories/
```

### 3. Run one command

Open PowerShell on Windows, or a terminal on Linux, **inside `replication/code`**. On Windows, right-click inside the `code` folder and choose **Open in Terminal**, using a PowerShell tab. Choose one of these commands without changing any paths:

**With saved solutions:**

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --input ../solutions --missing wait --workers 4
```

**From scratch, without saved solutions:**

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --missing compute --workers 4
```

Both commands check the installed tools, rebuild the required C# projects, and regenerate all the analyses and exhibits. The first verifies saved solutions; the second calculates all equilibria and histories afresh. The build downloads code dependencies, so keep an internet connection available. With the complete saved-solutions archive, it starts no equilibrium searches or solver-history recreations. Missing inputs remain pending because of `--missing wait`.

Leave the terminal open until it finishes, then open **`replication/output/run/article`** for Results, Tables, Figures and Supplemental materials. Four workers are used; reduce `--workers 4` if necessary. The output directory must be new and outside `code`. To repeat the exercise, use another new `replication` folder and keep the earlier results.

### If a tool is missing

Read the error in the terminal. You can test just the installed tools before attempting replication, from the same `code` folder:

```sh
dotnet run --project ArticleReplication -c Release -- doctor --output ../tool-check
```

The check compiles a font/TikZ example, renders a PNG and merges two PDF pages. Its logs are in `replication/tool-check/logs`. If an executable is missing, install it and ensure its folder is on **Path**, then reopen the terminal. If a font/package is missing, install it through your TeX distribution. Use a fresh folder name such as `../tool-check-2` when repeating the check. The program never installs tools automatically.

To calculate everything without saved solutions, omit `--input ../solutions` and replace `--missing wait` with `--missing compute` in the reproduction command. This includes the expensive equilibrium searches and solver histories and can take much longer. The saved-solutions route verifies those calculations' reusable outputs instead.

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

Release validation requires exact strategies, game identities, best-response results and search decisions. Derived floating-point report values may differ across operating systems by at most `1e-14` in absolute value; every accepted difference is recorded. This does not change the solver, its pivot rules, equilibrium acceptance, or reporting precision. Larger differences and structural changes fail the release check.

Primary reports include a full-precision CSV alongside the formatted CSV. A formatting-midpoint difference in the latter is permitted only when the full-precision values meet the same tolerance and reproduce both printed numbers with the existing formatter.

The public command reads only saved `.equ` and optional `.history` files. Omit the input mount and `--input` to solve afresh. Use `--missing wait` or explicit `--reserve-cases` when unavailable cases must remain pending. See [README.md](README.md) for the journal command, C#/CLI settings and selected stages. Default replication generates the research-output folders; manuscript compilation is an optional author step. A selected-stage test is not a whole-collection test. No machine-specific case reservations are implicit.

## Publishing a new tested image

The repository's `publish-correlated-signals-container.yml` workflow is triggered by a version tag such as `correlated-signals-container-2026-10-04` on the selected `correlated-signals` commit. Update `ContainerRelease.Image` and the instructions for a new version before tagging. The workflow uses the C# project's MSBuild container target, downloads the public saved-solutions archive, runs the complete default replication without network access, and compares complete strategies and numeric reports with a pinned published reference. That reference is used only for release testing, never as a required journal input. It pushes the dated image and a source-commit tag only after these checks pass.

GitHub's workflow token supplies registry access; no personal registry token is required. A newly created GitHub package is private by default, so its owner must set package visibility to Public once for anonymous journal downloads. The separate `check-public-correlated-signals-container.yml` workflow, triggered by a `correlated-signals-public-check-2026-10-04` tag, pulls with an empty Docker credential directory and runs the complete journal replication in a fresh directory. It verifies the scientific results and retains the entire generated collection, inventory and execution records as workflow artifacts. Do not reuse a published version tag for a different image.

Before replacing the article repository's research outputs, back up the existing repository and preserve its author-maintained manuscript folder. After synchronization, run the native read-only `verify-delivery` command described in [README.md](README.md). Every generated and published file must be accounted for with identical bytes, and both directory trees must match, including empty directories. Git status alone cannot detect leftover empty directories. These final delivery checks are separate from the equilibrium and scientific regression checks and are not required inputs to replication.
