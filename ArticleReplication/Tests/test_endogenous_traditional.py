import copy
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
import endogenous_traditional as run


def config():
    return dict(Schema=run.SCHEMA,Workers=10,Repetitions=10,Iterations=1000,CheckEvery=100,Cases=['one'],
        Seeds=[1729+989*i for i in range(10)],NormalizedNashTarget=1e-7,EconomicScreenFractionOfBenefit=.001,
        Initialization='dirichlet-one',Flavor='Fast',InputDirectory='input',CatalogSha256='hash',RuntimeSha256={'a.dll':'hash'})


class Traditional(unittest.TestCase):
    def test_cap_and_real_independent_seed_coverage(self):
        run.validate_configuration(config())
        for changes in ({'Workers':11},{'Workers':0},{'Workers':2.5},{'Seeds':[1729]*10},{'Cases':['one','one']},{'Iterations':999},{'NormalizedNashTarget':float('nan')}):
            c=dict(config(),**changes)
            with self.assertRaises(ValueError):run.validate_configuration(c)

    def test_output_location_is_not_scientific_identity(self):
        a=run.job_plan(config(),'one',1,Path('a'));b=run.job_plan(config(),'one',1,Path('b'))
        self.assertTrue(run.compatible(a,b))
        for field,value in [('Seed',2718),('Iterations',2000),('Initialization','uniform'),('CatalogSha256','different')]:
            changed=dict(b,**{field:value});self.assertFalse(run.compatible(a,changed))
        self.assertEqual(run.job_plan(config(),'one',10,Path('c'))['Seed'],10630)

    def test_immutable_records_and_atomic_telemetry(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'record.json';run.write(p,{'Passed':True})
            with self.assertRaises(FileExistsError):run.write(p,{'Passed':False})
            self.assertTrue(run.read(p)['Passed'])
            status=Path(t)/'status.json';run.write(status,{'n':1},replace=True);run.write(status,{'n':2},replace=True)
            self.assertEqual(run.read(status),{'n':2})

    def test_live_coordinator_is_never_duplicated(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);c=config();run.write(root/'batch.json',c)
            process=run.identity(os.getpid());self.assertIsNotNone(process)
            run.write(root/'run.lock.json',{'Process':process})
            with patch.object(run,'check_frozen'),self.assertRaisesRegex(RuntimeError,'live verified coordinator'):
                run.run(type('Args',(),{'run':t,'resume':True})())

    def test_runtime_change_is_rejected_before_dispatch(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);(root/'catalog.json').write_text('{}');(root/'a.dll').write_text('one')
            c=config();c.update(InputDirectory=t,CatalogSha256=run.sha(root/'catalog.json'),Assembly=str(root/'a.dll'),
                RuntimeSha256={'a.dll':run.sha(root/'a.dll')},RunnerSha256=run.sha(run.__file__))
            run.check_frozen(c);(root/'a.dll').write_text('changed')
            with self.assertRaisesRegex(ValueError,'Frozen runtime changed'):run.check_frozen(c)

    def test_corrupt_completion_is_not_imported(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);plan=run.job_plan(config(),'one',1,root)
            run.write(root/'plan.json',plan);(root/'result.json').write_text('original')
            run.write(root/'completed.json',{'Passed':True,'Files':{'result.json':run.sha(root/'result.json')}})
            (root/'result.json').write_text('changed')
            with self.assertRaisesRegex(ValueError,'evidence changed'):run.validate_result(root,plan)

    def test_optional_progress_lock_does_not_stop_supervision(self):
        with patch.object(run,'write',side_effect=PermissionError('sharing lock')):
            run.telemetry('unused',{'State':'Running'})


if __name__=='__main__':unittest.main()
