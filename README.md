# ACESim4 — correlated-signals article

This `correlated-signals` branch contains the simulation code and the `ArticleReplication` C# coordinator for the correlated-signals paper. The [article and reported results](https://github.com/mbabramo/correlated-signals-article) are maintained in a separate repository.

## Replicate the research outputs

Install Docker with Linux-container support and extract the [saved-solutions download](https://github.com/mbabramo/correlated-signals-article/releases/tag/replication-20261004). Create an empty output-parent folder. Replace the two host paths below with absolute paths to those existing folders:

```sh
docker run --rm --network none --cpus 4 --mount "type=bind,source=/absolute/path/saved-solutions,target=/inputs,readonly" --mount "type=bind,source=/absolute/path/output,target=/output" ghcr.io/mbabramo/acesim-correlated-signals:2026-10-04 run --input /inputs --output /output/run --missing wait --workers 4
```

Docker downloads the public image if needed; no GitHub login, source checkout or separate .NET/TeX/font installation is required. The image contains the compiled C# application and rendering tools. The equilibrium/history files remain a separate optional input. See [Windows/Linux setup and image details](ArticleReplication/INSTALL.md). On Windows use host paths such as `C:/Replication/saved-solutions` and `C:/Replication/output`.

The `run` subdirectory must be new. The command revalidates saved solutions and generates **Results**, **Tables**, **Figures** and **Supplemental materials** in the host output folder's `run/article`. The manuscript and bibliography remain separate; compiling the embedded author snapshot is an optional `--manuscript true` step.

To calculate equilibria and solver histories afresh, omit the input mount and `--input`, and use `--missing compute`; this can take much longer. The [replication guide](ArticleReplication/README.md) also retains the one-command native source rebuild, settings and optional stages. The [validation summary](ArticleReplication/VALIDATION.md) records completed checks and their scope.
