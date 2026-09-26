# Optional solve files

The public replication interface consumes only complete `.equ` solve records and optional `.history` files. Scientific defaults live in C#; command-line options can select stages and settings. See [README.md](README.md) for commands.

An exact primary file holds the case ID, format/protocol identity and full probability vector. Reuse initializes the current full game, verifies the vector and normalization, recalculates unrestricted best responses, accounting and reports, and checks the complete policy remains unchanged.

A multiple-start file also records its start/seed, pivot budget, stopping pivot, cutoff, gain units and stopping reason. It contains either a complete accepted strategy or `Status: NoEquilibriumFound` with an empty strategy. An unsuccessful record avoids repeating that particular search attempt, not validating a nonexistent strategy. Changing its budget requires a new attempt. Accepted profiles must pass fresh approximate acceptance checks.

A history file contains its case/initialization metadata and native pivot/strategy frames. Every frame is evaluated afresh, native projections are checked, and its final complete policy is compared with the validated equilibrium. This verifies the trajectory data used by the viewer; it does not reconstruct every exact tableau transition.

No integrity hashes, provenance manifests, old audits, action reports, outcome reports, figures, settings files, decomposition results or tremble results are required inputs. New execution records and output hashes are retained for debugging and review. Optional explicit regression comparisons can use old results separately.

All remaining calculations are regenerated. In particular, coalition responses and strategic decompositions are freshly computed and independently checked. Prior broad computation-bundle formats survive only in legacy migration/regression commands; they are not the public `run` contract.
