import gzip
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Scripts'))
from population_archive_validation import ArchiveVerifier, canonical_policy, decode_policy
import population_archives


class ArchiveValidationTests(unittest.TestCase):
    def test_numeric_identity_and_invalid_probability(self):
        self.assertEqual(canonical_policy(0, [[-0.0, 1.0]])[0], canonical_policy(0, [[0, 1]])[0])
        self.assertNotEqual(canonical_policy(0, [[0, 1]])[0], canonical_policy(1, [[0, 1]])[0])
        for row in ([.2, .2], [-.1, 1.1], [float('nan'), 1]):
            with self.assertRaises(ValueError): canonical_policy(0, [row])

    def test_truncated_and_wrong_player_binary(self):
        data = struct.pack('<4sBii2d', b'POL1', 0, 1, 2, .25, .75)
        self.assertEqual(decode_policy(data, 0)[0], canonical_policy(0, [[.25, .75]])[0])
        for invalid, player in ((data[:-1], 0), (data + b'x', 0), (data, 1)):
            with self.assertRaises(ValueError): decode_policy(invalid, player)

    def test_reference_hashes_cache_and_semantic_layout(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            policies = [{'Name': str(i), 'Player': i, 'Probabilities': [[.5, .5]]} for i in (0, 1)]
            archive = {'Schema': 'population-policy-archive-v1', 'GameHash': 'game',
                       'Keys': ['0/a', '1/b'], 'Policies': policies, 'Probes': policies}
            path = root / 'population.json'
            population_archives.save_archive(path, archive, root / 'store')
            verifier = ArchiveVerifier(); checked = verifier.archive(path)
            self.assertEqual(checked['Counts'], [1, 1]); self.assertEqual(verifier.blobs, 2)
            verifier.archive(path); self.assertEqual(verifier.blobs, 2)
            index = json.loads(gzip.decompress(Path(str(path) + '.gz').read_bytes()))
            ref = index['Policies'][0]; blob = root / 'store' / ref['Sha256'][:2] / (ref['Sha256'] + '.bin.gz')
            blob.write_bytes(gzip.compress(b'changed'))
            with self.assertRaisesRegex(ValueError, 'Changed policy'): ArchiveVerifier().archive(path)

    def test_legacy_layout_mismatch(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'population.json'
            policies = [{'Name': str(i), 'Player': i, 'Probabilities': [[1.0]]} for i in (0, 1)]
            path.write_text(json.dumps({'Schema': 'population-policy-archive-v1', 'GameHash': 'game',
                'Keys': ['0/a', '0/b', '1/c'], 'Policies': policies, 'Probes': policies}))
            with self.assertRaisesRegex(ValueError, 'semantic layout'): ArchiveVerifier().archive(path)


if __name__ == '__main__': unittest.main()
