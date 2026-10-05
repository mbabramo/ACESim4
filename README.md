# ACESim4 — correlated-signals article

This `correlated-signals` branch contains the simulation code and the `ArticleReplication` C# coordinator for the correlated-signals paper. The [article and reported results](https://github.com/mbabramo/correlated-signals-article) are maintained in a separate repository.

## Replicate the research outputs

Install and start Docker. Create a new folder called `replication`. For the saved-solutions route, extract the [saved-solutions ZIP](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip) into `replication/solutions`; skip this download for a from-scratch calculation. Open PowerShell or a Linux terminal in the `replication` folder and choose one command, without changing any paths.

**With saved solutions:**

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/solutions:/inputs:ro" -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1 run --input /inputs --output /output/run --missing wait --workers 4
```

**From scratch, without saved solutions:** skip the solutions download and run this command in the same new `replication` folder. It calculates equilibria and solver histories as well, which can take days or longer.

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1 run --output /output/run --missing compute --workers 4
```

Docker downloads the software and creates the output folders automatically. No GitHub login, source checkout or separate .NET/TeX/font installation is required. See the [step-by-step instructions](ArticleReplication/INSTALL.md) for Docker installation and folder setup.

When the command finishes, open **`replication/output/run/article`** for the regenerated Results, Tables, Figures and Supplemental materials. An existing run is never overwritten; use a new folder to repeat the exercise. The manuscript and bibliography remain separate.

The saved-solutions route verifies the equilibria and histories before recalculating analyses and exhibits. The [validation summary](ArticleReplication/VALIDATION.md) records completed checks and their scope.

## Replicate without Docker

Follow the [Windows/Linux setup instructions](ArticleReplication/INSTALL.md#run-without-docker) to install .NET, TeX/fonts and PDF tools. Extract the code into `replication/code` and, if using saved solutions, extract those into `replication/solutions`. From the `code` folder, choose one command:

**With saved solutions:**

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --input ../solutions --missing wait --workers 4
```

**From scratch, without saved solutions:** skip the solutions download and run from `replication/code`.

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --missing compute --workers 4
```

Both commands rebuild the C# projects and generate the same research-output folders. The first verifies saved solutions; the second computes them afresh. Results are at **`replication/output/run/article`**. Docker, Visual Studio and Git are not required. The [replication guide](ArticleReplication/README.md) describes settings and optional stages.
