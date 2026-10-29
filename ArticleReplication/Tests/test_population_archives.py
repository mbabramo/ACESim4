"""Shared archives must preserve probabilities exactly and detect changed evidence."""
import copy
import gzip
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parents[1] / "Scripts"))
import population_archives as archives
from population_combined import merge_archives


class ArchiveTests(unittest.TestCase):
    def sample(self):
        policies = [{"Name": "random", "Player": p, "Probabilities": [[.1, .9], [-0., 1.]]} for p in (0, 1)]
        return {"Schema": "population-policy-archive-v1", "GameHash": "game", "Keys": ["p", "d"],
                "Policies": policies, "Probes": copy.deepcopy(policies)}

    def test_shared_roundtrip_preserves_bits_and_deduplicates_renamed_policies(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            first = self.sample()
            second = copy.deepcopy(first)
            second["Policies"][0]["Name"] = "another repetition"
            for name, value in (("first", first), ("nested/second", second)):
                path = root / name / "population.json"
                archives.save_archive(path, value, root / "store")
                actual = archives.read(path)
                self.assertEqual(value, actual)
                self.assertEqual(struct.pack('<d', -0.), struct.pack('<d', actual["Policies"][0]["Probabilities"][1][0]))
            self.assertEqual(2, len(list((root / "store").rglob("*.bin.gz"))))

    def test_changed_blob_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / "population.json"
            archives.save_archive(path, self.sample(), root / "store")
            blob = next((root / "store").rglob("*.bin.gz"))
            data = bytearray(gzip.decompress(blob.read_bytes()))
            data[-1] ^= 1
            blob.write_bytes(gzip.compress(data))
            with self.assertRaisesRegex(ValueError, "Changed policy blob"):
                archives.read(path)

    def test_mixed_archive_formats_produce_same_pool(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            ordinary, shared = root / "plain.json", root / "shared.json"
            value = self.sample()
            ordinary.write_text(json.dumps(value))
            archives.save_archive(shared, value, root / "store")
            self.assertEqual(merge_archives([ordinary])[0], merge_archives([ordinary, shared, shared])[0])
            self.assertEqual(3, len(merge_archives([ordinary, shared, shared])[1]))


if __name__ == "__main__":
    unittest.main()
