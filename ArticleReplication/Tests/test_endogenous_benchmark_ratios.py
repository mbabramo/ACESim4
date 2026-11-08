import copy
from pathlib import Path
import statistics
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
from endogenous_benchmark_ratios import build_analysis, describe, match_key
from render_endogenous_story import BASE


def case(name,values,**parameters):
    p=dict(BASE,**parameters)
    return dict(Id=name,OptionSetName=name,Parameters=p,ProvisionalCandidate=False,RepairedRepetitions=[],
        Published={'SocialWelfareLoss':values[0]},Selectors={m:dict(InitialValues=[{'SocialWelfareLoss':v} for v in values],Pooled={'SocialWelfareLoss':values[0]}) for m in ('gini','liu')})


def ideal(name,values,**p):
    return case(name,values,**dict({'Adjudication Mode':'Perfect','Costs Multiplier':'0','Court Noise':'Perfect','Noise Multiplier P':'0','Noise Multiplier D':'0'},**p))


class Ratios(unittest.TestCase):
    def test_fixed_shared_denominator_and_sample_sd(self):
        ref=ideal('ref',[2.,4.]);ref['Selectors']['liu']['InitialValues']=[{'SocialWelfareLoss':v} for v in (4.,6.)]
        target=case('target',[4.,8.])
        result=build_analysis(dict(Repetitions=2,Cases=[ref,target]),[1,2],BASE)
        row=next(r for r in result['Rows'] if r['CaseId']=='target' and r['Method']=='gini')
        self.assertEqual(row['BenchmarkCost'],4.)
        self.assertEqual(row['Ratios'],[1.,2.])
        self.assertAlmostEqual(row['Ratio']['SD'],statistics.stdev([4.,8.])/4)
        self.assertEqual(describe([1.,3.])['SD'],2**.5)

    def test_legal_contrast_uses_one_common_denominator(self):
        cases=[ideal('refA',[2.,2.]),ideal('refB',[10.,10.],**{'Damages Multiplier':'2'}),
               case('a',[4.,6.]),case('b',[6.,8.],**{'Damages Multiplier':'2'})]
        result=build_analysis(dict(Repetitions=2,Cases=cases),[1,2],BASE)
        comparison=next(r for r in result['Comparisons'] if r['Method']=='gini')
        self.assertEqual(comparison['CommonReference'],'refA')
        self.assertEqual(comparison['PairedDifferences'],[1.,1.])
        # B's own ratio is lower, but its actual cost is higher. Preserve the latter sign.
        self.assertGreater(comparison['Difference']['Mean'],0)

    def test_matching_and_missing_coverage(self):
        a=case('a',[1.,2.]);b=ideal('b',[1.,2.])
        self.assertEqual(match_key(a),match_key(b))
        c=case('c',[1.,2.],**{'Unit Precaution Cost':'2E-05'})
        self.assertNotEqual(match_key(a),match_key(c))
        result=build_analysis(dict(Repetitions=2,Cases=[a,b,c]),[1,2],BASE)
        self.assertEqual(result['Coverage']['UnmatchedSettings'],1)
        self.assertEqual(result['Unmatched'][0]['CaseId'],'c')

    def test_invalid_reference_and_duplicate_identity(self):
        for cases in ([ideal('zero',[0.,0.])],[ideal('r',[1.,1.]),ideal('duplicate',[1.,1.])],
                      [case('same',[1.,2.]),case('same',[1.,2.])]):
            with self.assertRaises(ValueError):build_analysis(dict(Repetitions=2,Cases=cases),[1,2],BASE)


if __name__=='__main__':unittest.main()
