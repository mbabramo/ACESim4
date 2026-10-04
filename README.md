# ACESim4 — correlated-signals article

This `correlated-signals` branch contains the simulation code and the `ArticleReplication` C# coordinator for the correlated-signals paper. The [article and reported results](https://github.com/mbabramo/correlated-signals-article) are maintained in a separate repository.

## Replicate the research outputs

Install the [.NET, TeX, font and PDF-tool prerequisites](ArticleReplication/INSTALL.md). Optionally extract the [saved-solutions download](https://github.com/mbabramo/correlated-signals-article/releases) into a sibling `saved-solutions` directory. From this code checkout, run:

```sh
dotnet run --project ArticleReplication -c Release -- rebuild --source . --output ../replication-output --input ../saved-solutions --missing wait --workers 4
```

The output directory must be new and outside the code checkout. The command rebuilds the code, revalidates saved solutions and generates **Results**, **Tables**, **Figures** and **Supplemental materials** under `../replication-output/run/article`. The manuscript and bibliography remain separate; compiling the embedded author snapshot is an optional `--manuscript true` step.

To calculate equilibria and solver histories afresh, omit `--input` and use `--missing compute`; this can take much longer. See the [replication guide](ArticleReplication/README.md) for settings, optional stages and the container workflow, and the [validation summary](ArticleReplication/VALIDATION.md) for completed checks and their scope.
