# ACESim4 — correlated-signals article

This `correlated-signals` branch contains the simulation code and the `ArticleReplication` C# coordinator for the correlated-signals paper. The [article and reported results](https://github.com/mbabramo/correlated-signals-article) are maintained in a separate repository.

## Replicate the research outputs

Install and start Docker. Create a new folder called `replication`, then extract the [saved-solutions ZIP](https://github.com/mbabramo/correlated-signals-article/releases/download/replication-20261004/correlated-signals-saved-solutions.zip) into `replication/solutions`. Open PowerShell or a Linux terminal in the `replication` folder and paste this command without changing any paths:

```sh
docker run --rm --network none --cpus 4 -v "${PWD}/solutions:/inputs:ro" -v "${PWD}/output:/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04.1 run --input /inputs --output /output/run --missing wait --workers 4
```

Docker downloads the software and creates the output folders automatically. No GitHub login, source checkout or separate .NET/TeX/font installation is required. See the [step-by-step instructions](ArticleReplication/INSTALL.md) for Docker installation and folder setup.

When the command finishes, open **`replication/output/run/article`** for the regenerated Results, Tables, Figures and Supplemental materials. An existing run is never overwritten; use a new folder to repeat the exercise. The manuscript and bibliography remain separate.

To calculate equilibria and solver histories afresh, omit the input mount and `--input`, and use `--missing compute`; this can take much longer. The [validation summary](ArticleReplication/VALIDATION.md) records completed checks and their scope.

## Replicate without Docker

Follow the [Windows/Linux setup instructions](ArticleReplication/INSTALL.md#run-without-docker) to install .NET, TeX/fonts and PDF tools. Extract the code into `replication/code` and the saved solutions into `replication/solutions`. From the `code` folder, run this one line:

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../output --input ../solutions --missing wait --workers 4
```

This rebuilds the C# projects, verifies the saved solutions and generates the same research-output folders at **`replication/output/run/article`**. Docker, Visual Studio and Git are not required. The [replication guide](ArticleReplication/README.md) describes settings and optional stages.
