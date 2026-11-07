"""Independent, bounded-memory validation of policy archive indices and blobs."""
import gzip
import hashlib
import json
import math
from pathlib import Path
import struct

import population_archives as archives


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical_policy(player, rows):
    if player not in (0, 1):
        raise ValueError('Invalid player')
    encoded = bytearray(struct.pack('<4sBi', b'POL1', player, len(rows)))
    shape = []
    for row in rows:
        if not 1 <= len(row) <= 255 or any(not math.isfinite(x) or x < 0 for x in row):
            raise ValueError('Invalid probabilities')
        if abs(math.fsum(row) - 1) > 1e-10:
            raise ValueError('Probability row does not sum to one')
        shape.append(len(row))
        # Equality follows population_combined.policy_key, including +0 == -0.
        encoded += struct.pack('<i' + 'd' * len(row), len(row), *(0.0 if x == 0 else float(x) for x in row))
    shape_hash = hashlib.sha256(bytes(shape)).hexdigest()
    return (player, hashlib.sha256(encoded).hexdigest()), len(rows), shape_hash


def decode_policy(data, expected_player):
    if len(data) < 9:
        raise ValueError('Truncated policy header')
    magic, player, count = struct.unpack_from('<4sBi', data)
    if magic != b'POL1' or player != expected_player or not 0 <= count <= 1_000_000:
        raise ValueError('Invalid policy header')
    offset = 9
    rows = []
    for _ in range(count):
        if offset + 4 > len(data):
            raise ValueError('Truncated policy row')
        actions, = struct.unpack_from('<i', data, offset); offset += 4
        if not 1 <= actions <= 255 or offset + 8 * actions > len(data):
            raise ValueError('Invalid action count or truncated probabilities')
        rows.append(struct.unpack_from('<' + 'd' * actions, data, offset)); offset += 8 * actions
    if offset != len(data):
        raise ValueError('Trailing policy bytes')
    return canonical_policy(player, rows)


class ArchiveVerifier:
    """Cache small verified identities, never full policies or joint weights.

    The optional ledger records every physical blob read, so an audit can bind
    both its compressed file identity and its decompressed content identity.
    """
    def __init__(self, ledger=None):
        self.cache = {}
        self.ledger = ledger
        self.blobs = 0
        self.archives = 0
        self.evidence = hashlib.sha256()

    def blob(self, store, ref):
        expected = ref['Sha256']
        if len(expected) != 64 or any(c not in '0123456789abcdef' for c in expected):
            raise ValueError('Invalid policy hash')
        path = (store / expected[:2] / (expected + '.bin.gz')).resolve()
        key = (str(path), expected, ref['Player'])
        if key not in self.cache:
            compressed = path.read_bytes(); data = gzip.decompress(compressed)
            if hashlib.sha256(data).hexdigest() != expected:
                raise ValueError('Changed policy blob')
            value = decode_policy(data, ref['Player'])
            record = {'Path': str(path), 'CompressedSha256': hashlib.sha256(compressed).hexdigest(),
                      'RawSha256': expected, 'Player': value[0][0], 'SemanticSha256': value[0][1],
                      'Rows': value[1], 'ShapeSha256': value[2]}
            line = json.dumps(record, sort_keys=True, separators=(',', ':')) + '\n'
            self.evidence.update(line.encode())
            if self.ledger is not None:
                self.ledger.write(line)
            self.cache[key] = value
            self.blobs += 1
        return self.cache[key]

    def archive(self, path):
        path = archives.physical_path(path).resolve()
        raw = path.read_bytes()
        value = json.loads(gzip.decompress(raw) if path.suffix == '.gz' else raw)
        schema = value.get('Schema')
        if schema == 'population-policy-reference-v1':
            store = (path.parent / value['StoreRelativePath']).resolve()
            restore = lambda ref: self.blob(store, ref)
        elif schema == 'population-policy-archive-v1':
            restore = lambda policy: canonical_policy(policy['Player'], policy['Probabilities'])
        else:
            raise ValueError('Unknown policy archive schema')
        policies = [restore(p) for p in value['Policies']]
        probes = [restore(p) for p in value['Probes']]
        expected_rows = {i: sum(str(key).startswith(str(i) + '/') for key in value['Keys']) for i in (0, 1)}
        shapes = {}
        for key, rows, shape in policies + probes:
            player = key[0]
            if rows != expected_rows[player]:
                raise ValueError('Policy rows differ from semantic layout')
            if player in shapes and shapes[player] != shape:
                raise ValueError('Action layout differs within player')
            shapes[player] = shape
        keys = [p[0] for p in policies]
        if not all(any(k[0] == i for k in keys) for i in (0, 1)):
            raise ValueError('Both policy populations must be nonempty')
        self.archives += 1
        return {'Path': str(path), 'Sha256': hashlib.sha256(raw).hexdigest(), 'GameHash': value['GameHash'],
                'LayoutSha256': hashlib.sha256(json.dumps(value['Keys'], separators=(',', ':')).encode()).hexdigest(),
                'Policies': keys, 'PolicySet': set(keys), 'Probes': [p[0] for p in probes],
                'Counts': [sum(k[0] == i for k in keys) for i in (0, 1)], 'Shapes': shapes}

    def summary(self):
        return {'PhysicalBlobsVerified': self.blobs, 'ArchivesVerified': self.archives,
                'BlobEvidenceSha256': self.evidence.hexdigest()}


def same_layout(left, right):
    for key in ('GameHash', 'LayoutSha256', 'Probes', 'Shapes'):
        if left[key] != right[key]:
            raise ValueError('Different game, layout or probes: ' + key)
