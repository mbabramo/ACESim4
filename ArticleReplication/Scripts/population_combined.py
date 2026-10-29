"""Repeat JPSRO experiments, then reselect and expand a common policy population.

This is process orchestration over the existing population commands. It never
averages equilibrium weights or turns repetition frequencies into prior weights.
Only the Python standard library is required.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import threading
import time
import traceback


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def save(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def policy_key(policy):
    player = policy["Player"]
    if player not in (0, 1):
        raise ValueError("Exactly two strategic players are required.")
    rows = policy["Probabilities"]
    for row in rows:
        if not row or any(not math.isfinite(x) or x < 0 for x in row) or abs(sum(row) - 1) > 1e-10:
            raise ValueError("Invalid behavioral probabilities in archive.")
    # Numeric equality deduplicates +0/-0 and integer/floating representations.
    return player, tuple(tuple(float(x) for x in row) for row in rows)


def merge_archives(paths):
    """Union exact policies, with fixed game/layout/probes and deterministic order."""
    paths = [Path(p) for p in paths]
    if not paths:
        raise ValueError("Supply at least one archive.")
    first = read(paths[0])
    if first["Schema"] != "population-policy-archive-v1":
        raise ValueError("Unsupported archive schema.")
    probe_keys = [policy_key(p) for p in first["Probes"]]
    shapes = {}
    for p in first["Probes"]:
        shape = tuple(map(len, p["Probabilities"]))
        if p["Player"] in shapes and shape != shapes[p["Player"]]:
            raise ValueError("Inconsistent probe layout.")
        shapes[p["Player"]] = shape
    if set(shapes) != {0, 1}:
        raise ValueError("Both players require frozen probes.")
    entries = {}
    sources = []
    for path in paths:
        archive = read(path)
        if any(archive[k] != first[k] for k in ("Schema", "GameHash", "Keys")):
            raise ValueError("Cannot pool different games or semantic layouts.")
        if [policy_key(p) for p in archive["Probes"]] != probe_keys:
            raise ValueError("Cannot pool archives with different frozen probes.")
        counts = [0, 0]
        for policy in archive["Policies"]:
            key = policy_key(policy)
            if tuple(map(len, policy["Probabilities"])) != shapes[policy["Player"]]:
                raise ValueError("Policy and probe layouts differ.")
            counts[policy["Player"]] += 1
            entries[key] = {"Player": key[0], "Probabilities": [list(row) for row in key[1]]}
        if min(counts) == 0:
            raise ValueError("Both player populations must be nonempty.")
        sources.append({"Path": str(path.resolve()), "Sha256": digest(path), "PolicyCounts": counts})
    policies = []
    for key in sorted(entries):
        policy = entries[key]
        policy["Name"] = "pooled-" + hashlib.sha256(canonical(policy).encode()).hexdigest()[:20]
        policies.append(policy)
    result = {k: first[k] for k in ("Schema", "GameHash", "Keys", "Probes")}
    result["Policies"] = policies
    return result, sources


def policy_set(archive):
    return {policy_key(p) for p in archive["Policies"]}


class Batch:
    def __init__(self, config):
        defaults = {"cases": "all", "repetitions": 10, "workers": 4, "iterations": 150,
                    "initial_seed": 1729, "seed_stride": 989, "pool_rounds": 10,
                    "selectors": ["gini", "liu"], "epsilon": 1e-5, "bandwidth": .1, "floor": 1e-6,
                    "repetitions_from": None, "replacement_runs": []}
        unknown = set(config) - set(defaults) - {"assembly", "input", "output"}
        if unknown:
            raise ValueError("Unknown batch settings: " + str(sorted(unknown)))
        self.config = defaults | config
        for key in ("assembly", "input", "output"):
            self.config[key] = str(Path(self.config[key]).resolve())
        if self.config["repetitions_from"]:
            self.config["repetitions_from"] = str(Path(self.config["repetitions_from"]).resolve())
        self.replacements = {}
        for item in self.config["replacement_runs"]:
            if set(item) != {"repetition", "run"} or not 1 <= item["repetition"] <= self.config["repetitions"]:
                raise ValueError("Replacement requires a one-based repetition number and completed run.")
            item["run"] = str(Path(item["run"]).resolve())
            replacement = Path(item["run"])
            plan = read(replacement / "plan.json")
            expected_seed = self.config["initial_seed"] + (item["repetition"] - 1) * self.config["seed_stride"]
            self.validate_import_plan(plan, expected_seed)
            result = read(replacement / "completed.json")
            if not result["Passed"] or any(r["Status"] != "FullGameCceVerified" for r in result["Results"]):
                raise ValueError("A replacement run must have verified results.")
            for case in plan["Cases"]:
                key = (item["repetition"] - 1, case["Id"])
                if key in self.replacements:
                    raise ValueError("Duplicate replacement for a repetition/case.")
                self.replacements[key] = replacement
        self.root = Path(self.config["output"])
        source = Path(self.config["input"])
        if self.root == source or source in self.root.parents or self.root in source.parents:
            raise ValueError("Output must be separate from saved inputs.")
        for key in ("workers", "repetitions", "iterations", "seed_stride", "pool_rounds"):
            if not isinstance(self.config[key], int) or self.config[key] < 1:
                raise ValueError("Invalid positive integer setting: " + key)
        if self.config["workers"] > 8:
            raise ValueError("At most eight single-threaded workers per batch.")
        if self.config["selectors"] != ["gini", "liu"]:
            raise ValueError("The common-population comparison uses gini and liu.")
        final_seed = self.config["initial_seed"] + (self.config["repetitions"] - 1) * self.config["seed_stride"] + 101
        if self.config["initial_seed"] < 0 or final_seed > 2147483647:
            raise ValueError("Seeds exceed the nonnegative Int32 range.")
        if self.config["seed_stride"] <= 101:
            raise ValueError("Use disjoint initial random seeds across players/repetitions.")
        assembly = Path(self.config["assembly"])
        self.runtime_hashes = {p.name: digest(p) for p in (assembly, assembly.parent / "ACESimBase.dll", assembly.parent / "LitigCharts.dll")}
        self.root.mkdir(parents=True, exist_ok=True)
        identity = {"Schema": "pooled-population-batch-v1", "Settings": self.config, "RuntimeSha256": self.runtime_hashes,
                    "RunnerSha256": digest(Path(__file__)),
                    "ReplacementReceipts": [{"Run": str(p), "CompletedSha256": digest(p / "completed.json"), "PlanSha256": digest(p / "plan.json")}
                                            for p in sorted(set(self.replacements.values()))]}
        if self.config["repetitions_from"]:
            previous = Path(self.config["repetitions_from"])
            if self.root == previous or self.root in previous.parents or previous in self.root.parents:
                raise ValueError("A new pooling run must be separate from its repetition source.")
            source_settings = read(previous / "batch-plan.json")["Settings"]
            for key in ("input", "cases", "repetitions", "initial_seed", "seed_stride", "selectors", "epsilon", "bandwidth", "floor"):
                if source_settings[key] != self.config[key]:
                    raise ValueError("Imported repetition settings differ: " + key)
            identity["ImportedRepetitions"] = {"BatchPlanSha256": digest(previous / "batch-plan.json"),
                                                "ManifestSha256": digest(previous / "repetitions.json")}
        plan_path = self.root / "batch-plan.json"
        if plan_path.exists() and read(plan_path) != identity:
            raise ValueError("Resume requires identical settings, runner, and runtime.")
        if not plan_path.exists():
            save(plan_path, identity)
        self.lock = threading.Lock()
        self.started = time.time()
        self.state = {"Status": "Starting", "Pid": os.getpid(), "StartedUtc": self.utc(),
                      "RepetitionsComplete": 0, "RepetitionsRequested": self.config["repetitions"],
                      "CombinedCasesComplete": 0, "Active": {}, "Failures": []}
        self.env = dict(os.environ, DOTNET_PROCESSOR_COUNT="1", OMP_NUM_THREADS="1", MKL_NUM_THREADS="1", OPENBLAS_NUM_THREADS="1")

    def validate_import_plan(self, plan, expected_seed):
        c = self.config
        if plan["InitialSeed"] != expected_seed or plan["SeedMode"] != "independent" or plan["Selectors"] != c["selectors"]:
            raise ValueError("Imported initial seed, mode, or selectors differ.")
        if Path(plan["InputDirectory"]).resolve() != Path(c["input"]):
            raise ValueError("Imported saved-solution input differs.")
        settings = plan["Settings"]
        if (settings["MaximumNormalizedGain"] != c["epsilon"] or settings["Affinity"]["Bandwidth"] != c["bandwidth"]
                or settings["Affinity"]["ReferenceFloorMass"] != c["floor"] or not plan["ReportOutcomes"]):
            raise ValueError("Imported tolerances, affinity settings, or reporting differ.")

    @staticmethod
    def utc():
        return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    def update(self, **changes):
        with self.lock:
            self.state.update(changes)
            self.state["UpdatedUtc"] = self.utc()
            self.state["ElapsedSeconds"] = round(time.time() - self.started, 2)
            save(self.root / "status.json", self.state)

    def event(self, message):
        with self.lock:
            with (self.root / "events.jsonl").open("a", encoding="utf-8") as f:
                f.write(json.dumps({"Utc": self.utc(), "Message": message}) + "\n")
            print(self.utc(), message, flush=True)

    def command(self, label, args, log):
        assembly = Path(self.config["assembly"])
        if any(digest(assembly.parent / name) != value for name, value in self.runtime_hashes.items()):
            raise ValueError("The frozen worker runtime changed.")
        log.parent.mkdir(parents=True, exist_ok=True)
        with log.open("w", encoding="utf-8") as f:
            process = subprocess.Popen(["dotnet", str(assembly), *map(str, args)], stdout=f, stderr=subprocess.STDOUT, env=self.env,
                                       creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            with self.lock:
                self.state["Active"][label] = {"Pid": process.pid, "Log": str(log), "StartedUtc": self.utc()}
            self.update()
            code = process.wait()
            with self.lock:
                self.state["Active"].pop(label, None)
            self.update()
        if code:
            raise RuntimeError(f"{label} exited {code}; see {log}")

    def population(self, label, base, cases, seed, archive=None):
        base.mkdir(parents=True, exist_ok=True)
        # Interrupted attempts stay intact. Resume adopts a complete immutable attempt.
        attempts = sorted(base.glob("attempt-*"))
        for attempt in reversed(attempts):
            if (attempt / "completed.json").exists():
                return attempt
        attempt = base / f"attempt-{len(attempts) + 1:03d}"
        c = self.config
        args = ["population", "--input", c["input"], "--output", attempt, "--cases", cases,
                "--selectors", ",".join(c["selectors"]), "--iterations", c["iterations"], "--initial-seed", seed,
                "--epsilon", c["epsilon"], "--bandwidth", c["bandwidth"], "--floor", c["floor"]]
        if archive is not None:
            args += ["--seed-archive", archive]
        self.command(label, args, base / f"{attempt.name}.log")
        completed = read(attempt / "completed.json")
        if not completed["Passed"] or completed["Runs"] != 2 * completed["Cases"]:
            raise ValueError("Incomplete population experiment: " + str(attempt))
        return attempt

    def repetition(self, index):
        seed = self.config["initial_seed"] + index * self.config["seed_stride"]
        path = self.population(f"repetition-{index + 1}", self.root / "repetitions" / f"{index + 1:02d}-seed-{seed}", self.config["cases"], seed)
        results = read(path / "completed.json")["Results"]
        self.event(f"Repetition {index + 1}/{self.config['repetitions']} complete: {sum(r['Status'] == 'FullGameCceVerified' for r in results)}/{len(results)} verified.")
        with self.lock:
            self.state["RepetitionsComplete"] += 1
        self.update()
        return index, path

    def combine(self, case, repetitions):
        case_id = case["Id"]
        folder = self.root / "combined" / case_id
        receipt_path = folder / "completed.json"
        if receipt_path.exists():
            receipt = read(receipt_path)
            if not receipt["Passed"]:
                raise ValueError("Invalid combined receipt.")
        else:
            sources = [path / case_id / "union-population.json" for path in repetitions]
            # Preserve policies from an unsuccessful attempt as well as its verified rerun.
            sources += [path / case_id / "union-population.json" for (index, replaced_case), path in self.replacements.items() if replaced_case == case_id]
            if self.config["repetitions_from"]:
                prior = Path(self.config["repetitions_from"]) / "combined" / case_id
                # Completed selector archives can be reused even when their sibling or
                # common-population round was interrupted. These are policies, not weights.
                sources += sorted(prior.glob(f"round-*/solve/attempt-*/{case_id}/*/population.json"))
            history = []
            for round_index in range(self.config["pool_rounds"]):
                pool, provenance = merge_archives(sources)
                round_folder = folder / f"round-{round_index:02d}"
                archive_path = round_folder / "seed-population.json"
                if archive_path.exists() and read(archive_path) != pool:
                    raise ValueError("Changed pooled input during resume.")
                if not archive_path.exists():
                    save(archive_path, pool)
                    save(round_folder / "sources.json", provenance)
                run = self.population(f"pool-{case_id}-round-{round_index}", round_folder / "solve", case_id, self.config["initial_seed"], archive_path)
                results = read(run / "completed.json")["Results"]
                output_archive = run / case_id / "union-population.json"
                after = read(output_archive)
                before_set, after_set = policy_set(pool), policy_set(after)
                if not before_set <= after_set:
                    raise ValueError("Pooling unexpectedly removed a policy.")
                added = len(after_set - before_set)
                verified = all(r["Status"] == "FullGameCceVerified" for r in results)
                history.append({"Round": round_index, "InputCounts": [sum(p["Player"] == i for p in pool["Policies"]) for i in (0, 1)],
                                "PoliciesAdded": added, "Statuses": [r["Status"] for r in results], "Run": str(run)})
                self.event(f"{case_id} pooling round {round_index}: {len(before_set)} policies, {added} added; " + ", ".join(r["Status"] for r in results))
                if added == 0 and verified:
                    # Both selectors now solved the exact same set, with no subsequent additions.
                    for selector in self.config["selectors"]:
                        if policy_set(read(run / case_id / selector / "population.json")) != before_set:
                            raise ValueError("Selectors do not share the final population.")
                    audit = round_folder / "product-nash-audit.json"
                    if not audit.exists():
                        self.command(f"audit-{case_id}", ["population-audit", "--run", run, "--output", audit], round_folder / "audit.log")
                    if not read(audit)["Passed"]:
                        raise ValueError("Combined product audit failed.")
                    receipt = {"Passed": True, "Case": case, "FinalRun": str(run), "Audit": str(audit),
                               "Rounds": history, "Repetitions": len(repetitions), "CommonPopulationCounts": history[-1]["InputCounts"],
                               "MaximumNormalizedGain": max(max(r["NormalizedGains"]) for r in results),
                               "FinalArchiveSha256": digest(output_archive), "Results": results}
                    save(receipt_path, receipt)
                    break
                if added == 0:
                    raise RuntimeError(f"{case_id}: no new policy, but full-game checks fail; review solver diagnostics.")
                sources = [output_archive]
            else:
                raise RuntimeError(f"{case_id}: common-population round budget exhausted.")
        with self.lock:
            self.state["CombinedCasesComplete"] += 1
        self.update()
        return receipt

    def parallel(self, jobs, function):
        results = []
        with concurrent.futures.ThreadPoolExecutor(max_workers=self.config["workers"]) as executor:
            futures = {executor.submit(function, item): item for item in jobs}
            for future in concurrent.futures.as_completed(futures):
                try:
                    results.append(future.result())
                except Exception as error:
                    item = futures[future]
                    with self.lock:
                        self.state["Failures"].append({"Job": str(item), "Error": str(error), "Traceback": traceback.format_exc()})
                    self.event("FAILED: " + str(error))
                    self.update()
        if self.state["Failures"]:
            raise RuntimeError("One or more jobs failed; successful work is preserved for resume.")
        return results

    def run(self):
        self.update(Status="Repetitions")
        if self.config["repetitions_from"]:
            source = Path(self.config["repetitions_from"])
            repetitions = [Path(p) for p in read(source / "repetitions.json")["Runs"]]
            if len(repetitions) != self.config["repetitions"]:
                raise ValueError("Incomplete imported repetitions.")
            for index, path in enumerate(repetitions):
                self.validate_import_plan(read(path / "plan.json"), self.config["initial_seed"] + index * self.config["seed_stride"])
                completed = read(path / "completed.json")
                if not completed["Passed"] or completed["Runs"] != 2 * completed["Cases"]:
                    raise ValueError("Incomplete imported experiment.")
            save(self.root / "imported-repetitions.json", [{"Run": str(p), "CompletedSha256": digest(p / "completed.json"), "PlanSha256": digest(p / "plan.json")} for p in repetitions])
            self.update(RepetitionsComplete=len(repetitions))
            self.event(f"Reused all {len(repetitions)} completed repetitions; original files remain immutable.")
        else:
            repetitions = [path for _, path in sorted(self.parallel(range(self.config["repetitions"]), self.repetition))]
        cases = read(repetitions[0] / "plan.json")["Cases"]
        for path in repetitions:
            if read(path / "plan.json")["Cases"] != cases:
                raise ValueError("Repetition case catalogs differ.")
        by_case = {case["Id"]: case for case in cases}
        for path in set(self.replacements.values()):
            for case in read(path / "plan.json")["Cases"]:
                if case != by_case.get(case["Id"]):
                    raise ValueError("Replacement game differs from the repetition catalog.")
        save(self.root / "repetitions.json", {"Runs": [str(p) for p in repetitions], "Cases": cases, "Replacements": self.config["replacement_runs"]})
        self.update(Status="Pooling", ArticleCases=len(cases))
        combined = sorted(self.parallel(cases, lambda case: self.combine(case, repetitions)), key=lambda r: r["Case"]["Id"])
        summary = summarize(combined, repetitions, self.replacements)
        save(self.root / "summary.json", summary)
        for name in ("Comparisons", "FeeContrasts"):
            records = summary[name]
            if not records:
                continue
            with (self.root / (name.lower() + ".csv")).open("w", newline="", encoding="utf-8") as f:
                writer = csv.DictWriter(f, fieldnames=list(records[0]))
                writer.writeheader()
                writer.writerows(records)
        save(self.root / "completed.json", {"Passed": True, "ArticleCases": len(cases), "Repetitions": len(repetitions),
                                            "CombinedRuns": 2 * len(cases), "ElapsedSeconds": time.time() - self.started,
                                            "FinalResults": [str(self.root / "combined" / c["Id"] / "completed.json") for c in cases]})
        self.update(Status="Completed")
        self.event(f"Completed {len(cases)} games with {len(repetitions)} repetitions and {2 * len(cases)} common-population results.")


def measures(outcomes):
    return {k: outcomes[k] for k in ("Filing", "AnsweringUnconditional", "Trial", "Settlement")} | outcomes["Welfare"] | {
        "PlaintiffUtility": outcomes["NativeUtilities"][0], "DefendantUtility": outcomes["NativeUtilities"][1]}


def summarize(combined, repetitions, replacements=None):
    individual = {}
    statuses, original_statuses, accepted = {}, {}, {}
    replacements = replacements or {}
    for index, path in enumerate(repetitions):
        originals = read(path / "completed.json")["Results"]
        records = {(r["CaseId"], r["Selector"]): r for r in originals}
        for r in originals:
            original_statuses[r["Status"]] = original_statuses.get(r["Status"], 0) + 1
        for (repetition, case_id), replacement in replacements.items():
            if index == repetition:
                for r in read(replacement / "completed.json")["Results"]:
                    if r["CaseId"] == case_id:
                        records[(case_id, r["Selector"])] = r
        for r in records.values():
            individual[(index, r["CaseId"], r["Selector"])] = measures(r["Outcomes"])
            accepted[(index, r["CaseId"], r["Selector"])] = r["Status"] == "FullGameCceVerified"
            statuses[r["Status"]] = statuses.get(r["Status"], 0) + 1
    rows, contrasts = [], []
    lookup = {}
    nash_count = 0
    for item in combined:
        case = item["Case"]
        audits = {r["Selector"]: r for r in read(item["Audit"])["Rows"]}
        for r in item["Results"]:
            key = (case["Id"], r["Selector"])
            current, baseline = measures(r["Outcomes"]), measures(r["Baseline"])
            lookup[key] = (case, current, baseline)
            audit = audits[r["Selector"]]
            nash_count += audit["ProductIsApproximateNashAt1eMinus5"]
            for metric, value in current.items():
                values = [individual[(i, *key)][metric] for i in range(len(repetitions)) if accepted[(i, *key)]]
                rows.append({"CaseId": case["Id"], "Family": case["Family"], "Selector": r["Selector"], "Measure": metric,
                             "Combined": value, "Article": baseline[metric], "Difference": value - baseline[metric],
                             "IndividualMinimum": min(values) if values else None, "IndividualMaximum": max(values) if values else None,
                             "FirstRepetition": individual[(0, *key)][metric] if accepted[(0, *key)] else None, "IndividualVerifiedRuns": len(values),
                             "Player0Policies": item["CommonPopulationCounts"][0], "Player1Policies": item["CommonPopulationCounts"][1],
                             "NormalizedCceGain": max(r["NormalizedGains"]), "ProductNashGain": max(audit["NormalizedProductNashGains"])})
    sign = lambda x: 0 if abs(x) < 1e-6 else (1 if x > 0 else -1)
    for (case_id, selector), (case, current, baseline) in lookup.items():
        if case["FeeRule"] != "american":
            continue
        complete_id = case_id.replace("__american__", "__complete__")
        target = lookup.get((complete_id, selector))
        if target is None:
            continue
        for metric, value in current.items():
            original = target[2][metric] - baseline[metric]
            pooled = target[1][metric] - value
            values = [individual[(i, complete_id, selector)][metric] - individual[(i, case_id, selector)][metric]
                      for i in range(len(repetitions)) if accepted[(i, complete_id, selector)] and accepted[(i, case_id, selector)]]
            contrasts.append({"AmericanCase": case_id, "CompleteCase": complete_id, "Family": case["Family"], "Selector": selector,
                              "CostMultiplier": case["CostMultiplier"], "AlphaP": case["AlphaP"], "AlphaD": case["AlphaD"], "Measure": metric,
                              "ArticleCompleteMinusAmerican": original, "CombinedCompleteMinusAmerican": pooled,
                              "CombinedDirectionMatches": sign(original) == sign(pooled), "IndividualMinimum": min(values) if values else None,
                              "IndividualMaximum": max(values) if values else None, "IndividualDirectionMatches": sum(sign(x) == sign(original) for x in values),
                              "IndividualRuns": len(values)})
    return {"Passed": True, "ArticleCases": len(combined), "Repetitions": len(repetitions), "IndividualStatusCounts": statuses,
            "OriginalIndividualStatusCounts": original_statuses, "ReplacedRepetitionCases": len(replacements),
            "CombinedVerifiedCces": 2 * len(combined), "ProductsAlsoApproximateNashAt1eMinus5": nash_count,
            "MaximumNormalizedGain": max(x["MaximumNormalizedGain"] for x in combined), "Comparisons": rows, "FeeContrasts": contrasts,
            "Interpretation": "One result per case and selector, reselected on a common union population and expanded until both pass full-game CCE checks. Individual ranges describe initialization sensitivity, not confidence intervals. Repetition frequency never sets final weights."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, help="JSON batch configuration; rerun unchanged to resume")
    args = parser.parse_args()
    batch = Batch(read(args.config))
    lock = batch.root / "running.lock"
    try:
        descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        raise RuntimeError("Batch lock exists; confirm its PID has exited before removing a stale lock.")
    try:
        with os.fdopen(descriptor, "w") as f:
            f.write(str(os.getpid()))
        batch.run()
    except Exception:
        batch.update(Status="Failed")
        raise
    finally:
        lock.unlink()


if __name__ == "__main__":
    main()
