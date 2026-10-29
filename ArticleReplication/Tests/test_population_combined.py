"""Pooling must preserve incentives and probes, independently of occurrence counts."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("population_combined", Path(__file__).parents[1] / "Scripts/population_combined.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PoolTests(unittest.TestCase):
    def test_attempt_discovery_ignores_supervisor_logs(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            attempt = root / "attempt-001"
            attempt.mkdir()
            (root / "attempt-001.log").write_text("log")
            (root / "attempt-002.log").write_text("another log")
            self.assertEqual([attempt], module.attempt_directories(root))

    def test_atomic_status_replace_retries_transient_windows_locks(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "status.json"
            module.save(path, {"state": "old"})
            replace = module.os.replace
            calls = []
            def locked_twice(source, target):
                calls.append(target)
                if len(calls) < 3:
                    raise PermissionError("sharing violation")
                replace(source, target)
            with patch.object(module.os, "replace", side_effect=locked_twice), patch.object(module.time, "sleep"):
                module.save(path, {"state": "new"})
            self.assertEqual(3, len(calls))
            self.assertEqual({"state": "new"}, module.read(path))
            with patch.object(module.os, "replace", side_effect=PermissionError("locked")), patch.object(module.time, "sleep"):
                with self.assertRaises(PermissionError):
                    module.save(path, {"state": "pending"})
            self.assertEqual({"state": "new"}, module.read(path))
            self.assertEqual({"state": "pending"}, module.read(path.with_suffix(".json.tmp")))

    def test_case_continuation_requires_identical_science_and_reviewed_harness(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "inputs"
            module.save(source / "catalog.json", {})
            first, second = root / "runtime1", root / "runtime2"
            for runtime in (first, second):
                runtime.mkdir()
                for name in ("ArticleReplication.dll", "ACESimBase.dll", "LitigCharts.dll"):
                    (runtime / name).write_bytes(name.encode())
            config = {"model": "endogenous", "assembly": str(first / "ArticleReplication.dll"), "input": str(source), "output": str(root / "original")}
            original = module.Batch(config)
            (second / "ArticleReplication.dll").write_bytes(b"IO-only harness update")
            continued = config | {"assembly": str(second / "ArticleReplication.dll"), "output": str(root / "continued"), "resume_cases_from": str(original.root)}
            with self.assertRaisesRegex(ValueError, "prior harness"):
                module.Batch(continued)
            continued["compatible_harness_sha256"] = original.runtime_hashes["ArticleReplication.dll"]
            module.Batch(continued)
            (second / "ACESimBase.dll").write_bytes(b"changed numerical code")
            with self.assertRaisesRegex(ValueError, "numerical/model"):
                module.Batch(continued | {"output": str(root / "invalid")})

    def archive(self):
        policies = [{"Name": "uniform", "Player": p, "Probabilities": [[.5, .5]]} for p in (0, 1)]
        return {"Schema": "population-policy-archive-v1", "GameHash": "game", "Keys": ["p", "d"], "Policies": policies, "Probes": copy.deepcopy(policies)}

    def merge(self, values):
        with tempfile.TemporaryDirectory() as folder:
            paths = []
            for i, value in enumerate(values):
                path = Path(folder) / f"{i}.json"
                path.write_text(json.dumps(value), encoding="utf-8")
                paths.append(path)
            return module.merge_archives(paths)[0]

    def test_occurrence_counts_and_source_order_do_not_change_pool(self):
        a, b = self.archive(), self.archive()
        b["Policies"].append({"Name": "pure", "Player": 0, "Probabilities": [[1, 0]]})
        self.assertEqual(self.merge([a, b]), self.merge([b, a, a, b, a]))
        self.assertEqual(3, len(self.merge([a, b])["Policies"]))

    def test_near_copies_retain_distinct_incentives(self):
        a = self.archive()
        a["Policies"].append({"Name": "near", "Player": 0, "Probabilities": [[.500001, .499999]]})
        self.assertEqual(3, len(self.merge([a])["Policies"]))

    def test_different_games_layouts_and_probes_are_rejected(self):
        for field, value in [("GameHash", "other"), ("Keys", ["other", "d"])]:
            a, b = self.archive(), self.archive()
            b[field] = value
            with self.assertRaises(ValueError):
                self.merge([a, b])
        a, b = self.archive(), self.archive()
        b["Probes"][0]["Probabilities"] = [[.6, .4]]
        with self.assertRaises(ValueError):
            self.merge([a, b])

    def test_invalid_probabilities_are_rejected(self):
        for row in ([float("nan"), .5], [-.1, 1.1], [.2, .2]):
            a = self.archive()
            a["Policies"][0]["Probabilities"] = [row]
            with self.assertRaises(ValueError):
                self.merge([a])

    def test_policy_shape_must_match_frozen_layout(self):
        a = self.archive()
        a["Policies"][0]["Probabilities"] = [[1, 0, 0]]
        with self.assertRaises(ValueError):
            self.merge([a])

    def test_import_requires_same_seed_and_numerical_settings(self):
        batch = module.Batch.__new__(module.Batch)
        batch.config = {"input": str(Path.cwd()), "selectors": ["gini", "liu"], "epsilon": 1e-5, "bandwidth": .1, "floor": 1e-6}
        plan = {"InitialSeed": 1729, "SeedMode": "independent", "Selectors": ["gini", "liu"], "InputDirectory": str(Path.cwd()),
                "ReportOutcomes": True, "Settings": {"MaximumNormalizedGain": 1e-5, "Affinity": {"Bandwidth": .1, "ReferenceFloorMass": 1e-6}}}
        batch.validate_import_plan(plan, 1729)
        with self.assertRaises(ValueError):
            batch.validate_import_plan(plan, 2718)
        plan["Settings"]["Affinity"]["Bandwidth"] = .2
        with self.assertRaises(ValueError):
            batch.validate_import_plan(plan, 1729)

    def test_ranges_exclude_unverified_results_and_record_verified_replacement(self):
        def result(selector, value, status="FullGameCceVerified"):
            outcomes = {"Filing": value, "AnsweringUnconditional": value, "Trial": value, "Settlement": value,
                        "NativeUtilities": [value, value], "Welfare": {"Cost": value}}
            return {"CaseId": "case", "Selector": selector, "Status": status, "Outcomes": outcomes, "Baseline": outcomes, "NormalizedGains": [0, 0]}
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            repetitions = [root / "first", root / "second"]
            module.save(repetitions[0] / "completed.json", {"Results": [result("gini", 1), result("liu", 1)]})
            module.save(repetitions[1] / "completed.json", {"Results": [result("gini", 2), result("liu", 999, "MetaSolverLimit")]})
            audit = root / "audit.json"
            module.save(audit, {"Rows": [{"Selector": s, "ProductIsApproximateNashAt1eMinus5": True, "NormalizedProductNashGains": [0, 0]} for s in ("gini", "liu")]})
            combined = [{"Case": {"Id": "case", "Family": "baseline", "FeeRule": "trial-only"}, "Audit": str(audit),
                         "CommonPopulationCounts": [2, 2], "MaximumNormalizedGain": 0, "Results": [result(s, 1.5) for s in ("gini", "liu")]}]
            summary = module.summarize(combined, repetitions)
            row = next(r for r in summary["Comparisons"] if r["Selector"] == "liu")
            self.assertEqual(1, row["IndividualMaximum"])
            self.assertEqual(1, row["IndividualVerifiedRuns"])
            repair = root / "repair"
            module.save(repair / "completed.json", {"Results": [result(s, 2) for s in ("gini", "liu")]})
            summary = module.summarize(combined, repetitions, {(1, "case"): repair})
            row = next(r for r in summary["Comparisons"] if r["Selector"] == "liu")
            self.assertEqual(2, row["IndividualMaximum"])
            self.assertEqual(2, row["IndividualVerifiedRuns"])
            self.assertEqual(1, summary["OriginalIndividualStatusCounts"]["MetaSolverLimit"])
            self.assertEqual({"FullGameCceVerified": 4}, summary["IndividualStatusCounts"])


if __name__ == "__main__":
    unittest.main()
