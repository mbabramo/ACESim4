import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Scripts'))
from endogenous_population_assembly import assemble, checked_proofs, sha


class AssemblyGuards(unittest.TestCase):
    def fixture(self, root):
        root = Path(root); audit = root / 'audit'; audit.mkdir()
        result = root / 'result'; result.write_text('immutable result')
        receipt = root / 'receipt.json'; receipt.write_text('{}')
        proof = audit / 'case.json'
        proof.write_text(json.dumps({'Passed': True, 'CaseId': 'case', 'Receipt': str(receipt), 'ReceiptSha256': sha(receipt),
            'FileHashes': {str(result): sha(result)}, 'SourceArchives': [], 'Results': [
                {'Selector': s, 'IndependentInitialResults': 10, 'NormalizedCceGains': [1e-8, 1e-8]} for s in ('gini', 'liu')]}))
        (audit / 'request.json').write_text(json.dumps({'Cases': [{'CaseId': 'case', 'Receipt': str(receipt), 'ReceiptSha256': sha(receipt)}]}))
        (audit / 'initial-context.json').write_text('{}'); (audit / 'policy-blobs.jsonl').write_text('verified ledger\n')
        (audit / 'completed.json').write_text(json.dumps({'Passed': True, 'Cases': 1, 'PooledResults': 2,
            'RequestSha256': sha(audit / 'request.json'), 'InitialContextSha256': sha(audit / 'initial-context.json'),
            'PolicyLedgerSha256': sha(audit / 'policy-blobs.jsonl'), 'Proofs': [{'CaseId': 'case', 'Proof': str(proof), 'Sha256': sha(proof)}]}))
        return audit, result, proof

    def test_complete_coverage(self):
        with tempfile.TemporaryDirectory() as root:
            audit, _, _ = self.fixture(root)
            self.assertEqual(set(checked_proofs([audit], {'case'})[0]), {'case'})
            with self.assertRaisesRegex(ValueError, 'Not every case'):
                checked_proofs([audit], {'case', 'missing'})

    def test_duplicate_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            audit, _, _ = self.fixture(root)
            with self.assertRaisesRegex(ValueError, 'Duplicate'):
                checked_proofs([audit, audit], {'case'})

    def test_changed_result_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            audit, result, _ = self.fixture(root); result.write_text('changed')
            with self.assertRaisesRegex(ValueError, 'file changed'):
                checked_proofs([audit], {'case'})

    def test_changed_evidence_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            audit, _, proof = self.fixture(root); proof.write_text('{}')
            with self.assertRaisesRegex(ValueError, 'proof changed'):
                checked_proofs([audit], {'case'})

    def test_assembly_preserves_receipt_bytes_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); audit, _, proof_path = self.fixture(root)
            pool = root / 'pool'; pool.mkdir(); case = {'Id': 'case'}
            (pool / 'batch-plan.json').write_text(json.dumps({'Settings': {'output': str(pool)}}))
            (pool / 'repetitions.json').write_text(json.dumps({'Cases': [case], 'Runs': [str(i) for i in range(10)], 'Replacements': []}))
            receipt_path = pool / 'combined/case/completed.json'; receipt_path.parent.mkdir(parents=True)
            receipt_path.write_text(json.dumps({'Passed': True, 'Case': case, 'Repetitions': 10,
                'CommonPopulationCounts': [1, 1], 'Rounds': [{'PoliciesAdded': 0}]}))
            proof = json.loads(proof_path.read_text()); proof.update(Receipt=str(receipt_path), ReceiptSha256=sha(receipt_path), CommonPopulationCounts=[1, 1])
            for row in proof['Results']:
                row.update(ProductNashAt1eMinus7=False, ProductNashAt1eMinus5=True)
            proof_path.write_text(json.dumps(proof))
            (audit / 'request.json').write_text(json.dumps({'Cases': [{'CaseId': 'case', 'Receipt': str(receipt_path), 'ReceiptSha256': sha(receipt_path)}]}))
            (audit / 'initial-context.json').write_text(json.dumps({'AcceptedInitialSelections': 20}))
            complete = json.loads((audit / 'completed.json').read_text())
            complete.update(RequestSha256=sha(audit / 'request.json'), InitialContextSha256=sha(audit / 'initial-context.json'))
            complete['Proofs'][0]['Sha256'] = sha(proof_path)
            (audit / 'completed.json').write_text(json.dumps(complete))
            index = root / 'repairs.json'; index.write_text('{"Replacements":[]}')
            output = root / 'assembled'
            result = assemble(pool, output, [audit], index, expected_cases=1)
            self.assertEqual(result['CombinedRuns'], 2)
            self.assertEqual(receipt_path.read_bytes(), (output / 'combined/case/completed.json').read_bytes())
            self.assertTrue(json.loads((output / 'completed.json').read_text())['Passed'])
            with self.assertRaisesRegex(ValueError, 'already exists'):
                assemble(pool, output, [audit], index, expected_cases=1)


if __name__ == '__main__': unittest.main()
