"""Pooling must preserve incentives and probes, independently of occurrence counts."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("population_combined", Path(__file__).parents[1] / "Scripts/population_combined.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PoolTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
