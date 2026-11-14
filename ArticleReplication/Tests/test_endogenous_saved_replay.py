from pathlib import Path
from types import SimpleNamespace
import gzip
import json
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
import endogenous_saved_replay as r


class SavedReplay(unittest.TestCase):
    def test_frozen_renderer_retains_its_browser_dependency(self):
        # A real renderer reads this sibling at authoring time; Python-only
        # packaging would pass imports but fail the final report generation.
        with tempfile.TemporaryDirectory() as t:
            target=Path(t)/'frozen'
            r.freeze_postprocessors(Path(r.__file__).parent,target)
            self.assertIn("with_name('endogenous_traditional_review.js')",(target/'render_endogenous_traditional_review.py').read_text(encoding='utf-8'))
            self.assertEqual((target/'endogenous_traditional_review.js').read_bytes(),Path(r.__file__).with_name('endogenous_traditional_review.js').read_bytes())

    def test_table_comparison_does_not_hide_sign_or_coverage_changes(self):
        a={'values':[.0001,0.0],'direction':'positive','included':True}
        b={'values':[.0001+1e-14,0.0],'direction':'negative','included':False}
        differences=r.scientific_differences(a,b)
        self.assertEqual({x['Kind'] for x in differences},{'classification','value'})
        self.assertTrue(r.scientific_differences([1,2],[1]))

    def batch(self,root,cases):
        return dict(Workers=20,RunnerSha256=r.sha(r.__file__),HelperSha256=r.sha(Path(r.__file__).with_name('endogenous_traditional.py')),
            Assembly=str(root/'unused.dll'),RuntimeSha256={},PostprocessSha256={},OriginalFiles={},Cases=cases,Finalize=False)

    def test_exact_compressed_path_is_not_replaced_by_uncompressed_sibling(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'result.json';p.write_text('{"kind":"raw"}')
            z=Path(str(p)+'.gz');z.write_bytes(gzip.compress(b'{"kind":"compressed"}'))
            self.assertEqual(r.physical(z),z);self.assertEqual(r.read(z)['kind'],'compressed')

    def test_worker_cap_rejected_before_any_source_or_output_write(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t)/'new'
            with self.assertRaises(ValueError):r.prepare(SimpleNamespace(workspace=t,output=str(root),assembly=str(Path(t)/'a.dll'),workers=21))
            self.assertFalse(root.exists())

    def test_existing_output_root_is_never_replaced(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'keep';p.write_bytes(b'equilibrium bytes')
            with self.assertRaises(FileExistsError):r.prepare(SimpleNamespace(workspace=t,output=t,assembly=str(Path(t)/'a.dll'),workers=20))
            self.assertEqual(p.read_bytes(),b'equilibrium bytes')

    def test_receipt_revalidation_has_identical_shape_and_rejects_changed_bytes(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);out=root/'out';out.mkdir();plan=root/'request.json';r.write(plan,dict(OutputDirectory=str(out),Items=[{'Id':'one'}]))
            (out/'replay.json.gz').write_bytes(gzip.compress(b'{"Passed":true}'))
            r.write(out/'completed.json',dict(PlanSha256=r.sha(plan),CaseId='one',Rows=1,ReplaySha256=r.sha(out/'replay.json.gz'),Passed=True,Seconds=1))
            entry=dict(CaseId='one',Path=str(plan),Sha256=r.sha(plan));verified=r.checked_case(entry)
            self.assertEqual(verified,r.checked_case(verified))
            (out/'replay.json.gz').write_bytes(b'changed')
            with self.assertRaises(ValueError):r.checked_case(entry)

    def test_live_coordinator_cannot_be_duplicated(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);r.write(root/'batch.json',self.batch(root,[]));r.write(root/'run.lock.json',{'Process':r.identity(__import__('os').getpid())})
            with self.assertRaisesRegex(RuntimeError,'Live replay'):r.run(SimpleNamespace(run=t,resume=True))

    def test_interrupted_attempt_preserved_without_automatic_retry(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);out=root/'jobs'/'one'/'attempt-001';out.mkdir(parents=True);(out/'keep').write_bytes(b'preserve')
            plan=out.parent/'request.json';r.write(plan,dict(OutputDirectory=str(out),Items=[{'Id':'one'}]))
            entry=dict(CaseId='one',Path=str(plan),Sha256=r.sha(plan));r.write(root/'batch.json',self.batch(root,[entry]))
            r.run(SimpleNamespace(run=t,resume=False))
            self.assertFalse(r.read(root/'completed.json')['Passed']);self.assertEqual((out/'keep').read_bytes(),b'preserve')
            self.assertEqual(len(list(out.parent.glob('attempt-*'))),1)


if __name__=='__main__':unittest.main()
