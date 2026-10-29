"""Lossless policy archives, including shared compressed binary policy storage."""
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import uuid


def physical_path(path):
    path = Path(path)
    return path if path.exists() else Path(str(path) + '.gz')


def read(path):
    actual = physical_path(path)
    opener = gzip.open if actual.suffix == '.gz' else open
    with opener(actual, 'rt', encoding='utf-8-sig') as f:
        value = json.load(f)
    if not isinstance(value, dict) or value.get('Schema') != 'population-policy-reference-v1':
        return value
    store = (actual.parent / value['StoreRelativePath']).resolve()
    def restore(ref):
        digest = ref['Sha256']
        if len(digest) != 64 or any(c not in '0123456789abcdef' for c in digest):
            raise ValueError('Invalid policy hash.')
        data = gzip.decompress((store / digest[:2] / (digest + '.bin.gz')).read_bytes())
        if hashlib.sha256(data).hexdigest() != digest:
            raise ValueError('Changed policy blob.')
        stream = io.BytesIO(data)
        magic, player, count = struct.unpack('<4sBi', stream.read(9))
        if magic != b'POL1' or player != ref['Player'] or not 0 <= count <= 1_000_000:
            raise ValueError('Invalid policy blob header.')
        rows = []
        for _ in range(count):
            actions, = struct.unpack('<i', stream.read(4))
            if not 1 <= actions <= 255:
                raise ValueError('Invalid action count.')
            rows.append(list(struct.unpack('<' + 'd' * actions, stream.read(8 * actions))))
        if stream.read(1):
            raise ValueError('Trailing policy bytes.')
        return {'Name': ref['Name'], 'Player': player, 'Probabilities': rows}
    return {'Schema': 'population-policy-archive-v1', 'GameHash': value['GameHash'], 'Keys': value['Keys'],
            'Policies': list(map(restore, value['Policies'])), 'Probes': list(map(restore, value['Probes']))}


def save_archive(path, archive, store):
    path, store = Path(path), Path(store).resolve()
    def keep(policy):
        rows = policy['Probabilities']
        data = bytearray(struct.pack('<4sBi', b'POL1', policy['Player'], len(rows)))
        for row in rows:
            data += struct.pack('<i' + 'd' * len(row), len(row), *row)
        digest = hashlib.sha256(data).hexdigest()
        destination = store / digest[:2] / (digest + '.bin.gz')
        if not destination.exists():
            destination.parent.mkdir(parents=True, exist_ok=True)
            temporary = destination.with_name(destination.name + '.' + uuid.uuid4().hex + '.tmp')
            temporary.write_bytes(gzip.compress(data, compresslevel=1, mtime=0))
            # Identical content is safe if another worker published the same hash.
            try:
                os.link(temporary, destination)
            except FileExistsError:
                pass
            finally:
                temporary.unlink()
        return {'Name': policy['Name'], 'Player': policy['Player'], 'Sha256': digest}
    value = {'Schema': 'population-policy-reference-v1', 'GameHash': archive['GameHash'], 'Keys': archive['Keys'],
             'StoreRelativePath': os.path.relpath(store, path.parent),
             'Policies': list(map(keep, archive['Policies'])), 'Probes': list(map(keep, archive['Probes']))}
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(str(path) + '.gz', 'xb') as f:
        f.write(gzip.compress(json.dumps(value, separators=(',', ':')).encode(), compresslevel=1, mtime=0))
