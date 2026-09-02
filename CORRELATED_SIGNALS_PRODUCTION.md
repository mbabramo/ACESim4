# Correlated-signals production run

Run every command below from the ACESim4 repository root. The production launcher is
`LitigGameCorrelatedSignalsArticleLauncher`; it uses the existing distributed worker and
shared task-coordinator framework. Do not run the production command from a Debug build.

## Validated design

The 200 option sets consist of 50 fully crossed core combinations:

- signal structure: Case quality, Binary truth;
- cost multiplier: 0.25, 0.5, 1, 2, 4; and
- fee-shifting multiplier: 0, 0.5, 1, 1.5, 2.

Each core combination is evaluated at four non-core settings without crossing the two
robustness dimensions: risk-neutral 0.5x, 1x, and 2x information, plus moderately
risk-averse 1x information. This gives 100 paired Case quality/Binary truth comparisons
and exactly 200 `Optimize` worker tasks. Every option set uses ten offers and identity
liability/damages signal shaping.

The party calibration minimizes KL divergence over the unconditional 10-by-10 joint
party-signal distribution. The court calibration then holds the matched party sigma fixed
and minimizes KL divergence over the unconditional 10-by-10-by-2 plaintiff, defendant,
and court joint distribution.

| Information level | Case-quality party sigma | Binary-truth party sigma | Case-quality court sigma | Binary-truth court sigma |
| --- | ---: | ---: | ---: | ---: |
| 0.5x | 0.1000000000 | 0.2964025888 | 0.1000000000 | 0.1947624474 |
| 1x | 0.2000000000 | 0.3498283040 | 0.2000000000 | 0.3060453855 |
| 2x | 0.4000000000 | 0.5507929452 | 0.4000000000 | 0.5210455266 |

## Build and preflight

`ControlExcel` is not part of this workflow. Build the production coordinator and worker:

```powershell
dotnet build .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- preflight
```

`preflight` prints all 200 option sets and 200 worker tasks without solving them. It also
checks the core cross, non-core design, calibrated sigmas, signal shaping, generators,
offer count, unique identifiers, and production restrictions.

## Start production on all available processors

```powershell
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- run --processors all
```

`all` uses `Environment.ProcessorCount`, which reflects the logical processors available
to the .NET process. To override it, replace `all` with a positive integer. The run command
only executes the 200 worker tasks; it does not aggregate or generate final reports.

Worker progress logs are written under `ReportResults\Process Logs`. A failed task writes
`ReportResults\CS001 FAILURE <task identity>.txt`. Each worker log and each option-set
result filename has a unique owner; option-set identifiers include signal structure,
information label, party sigma, court sigma, cost, fee shifting, and risk.

## Status, recovery, and reporting

Check completion at any time:

```powershell
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- status
```

A successful production run reports `complete: 200; pending: 0; failed: 0; unstarted: 0`.
The coordinator status contains a task-plan fingerprint, so a changed matrix is rejected.
Aggregation also verifies that all 200 primary result files exist and are nonempty; it will
not begin from an incomplete or failed coordinator state.

After all tasks complete, generate the aggregate tables and diagrams:

```powershell
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- aggregate
```

The direct numerical outputs include `ReportResults\CS001 output.csv` and the paired
`ReportResults\CS001 paired signal structures.csv`. The paired report records the common
information label, both raw party and court sigmas, each structure's numerical outcome,
and the Binary truth minus Case quality difference. Missing, duplicate, unexpected, or
mispaired rows stop report generation.

After inspecting a failure and confirming no distributed workers remain, reset failed
tasks and restart production:

```powershell
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- recover --failed --include-pending
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- run --processors all
```

If a machine or coordinator process stopped while tasks were pending, first confirm that
no old worker process remains, then explicitly reset both failed and stranded pending tasks:

```powershell
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- recover --failed --include-pending
dotnet run --project .\ACESimDistributedSaturate\ACESimDistributedSaturate.csproj -c Release --no-build -- run --processors all
```
