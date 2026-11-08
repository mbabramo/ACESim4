import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
from endogenous_paired_comparisons import build_pairwise, paired_outcomes


class PairedComparisons(unittest.TestCase):
    def case(self, level, cost='1'):
        values=[float(i+1)+float(level) for i in range(10)]
        return dict(Id=cost+'-'+level,Parameters={'Fee Shifting Multiplier':level,'Damages Multiplier':'1',
            'Liability Threshold':'1','Risk Aversion':'Risk Neutral','Costs Multiplier':cost},
            ProvisionalCandidate=False,RepairedRepetitions=[],Published={'SocialWelfareLoss':values[0]},
            Selectors={m:{'InitialValues':[{'SocialWelfareLoss':v} for v in values],
                          'Pooled':{'SocialWelfareLoss':values[0]}} for m in ('gini','liu')})

    def test_all_pairs_include_nonbaseline_transitions_without_reverse_duplicates(self):
        cases=[self.case(x) for x in ['0','0.5','1','2','5']]
        payload=build_pairwise({'Repetitions':10,'Cases':cases},list(range(10)),{})
        pairs=payload['Groups'][0]['Pairs']
        self.assertEqual(len(pairs),10)
        self.assertIn(('1','2'),{(p['From'],p['To']) for p in pairs})
        self.assertEqual(len({frozenset((p['A'],p['B'])) for p in pairs}),10)

    def test_other_parameters_never_get_pooled_and_missing_levels_stay_missing(self):
        cases=[self.case(x) for x in ['0','1','2']]+[self.case(x,'4') for x in ['0','2']]
        payload=build_pairwise({'Repetitions':10,'Cases':cases},list(range(10)),{})
        fee=[g for g in payload['Groups'] if g['Parameter']=='Fee Shifting Multiplier']
        self.assertEqual(sorted(len(g['Pairs']) for g in fee),[1,3])
        for g in fee:
            self.assertTrue(all(p['A'].split('-')[0]==p['B'].split('-')[0] for p in g['Pairs']))

    def test_ten_of_ten_same_seed_agreement_does_not_require_separated_ranges(self):
        before=[float(i) for i in range(1,11)];after=[v-.5 for v in before]
        result=paired_outcomes(before,after)
        self.assertEqual((result['BLower'],result['ALower'],result['ExactTies']),(10,0,0))
        self.assertEqual(result['SeparatedRanges'],'Overlap or touch')
        self.assertEqual(result['MeanDelta'],-.5)

    def test_ties_stay_in_denominator_and_zero_cost_percent_is_undefined(self):
        result=paired_outcomes([0.,2.,4.,6.],[0.,1.,5.,6.])
        self.assertEqual((result['BLower'],result['ALower'],result['ExactTies']),(1,1,2))
        self.assertEqual(result['BLowerFraction'],.25)
        self.assertIsNone(result['RelativeChanges'][0])
        with self.assertRaises(ValueError):paired_outcomes([1.,None],[0.,2.])

    def test_ambiguous_cases_missing_repetitions_and_duplicate_seeds_fail(self):
        cases=[self.case('0'),self.case('1')]
        with self.assertRaises(ValueError):build_pairwise({'Repetitions':10,'Cases':cases},[1]*10,{})
        duplicate=copy.deepcopy(cases[0]);duplicate['Id']='different-id'
        with self.assertRaises(ValueError):build_pairwise({'Repetitions':10,'Cases':cases+[duplicate]},list(range(10)),{})
        cases[0]['Selectors']['gini']['InitialValues'].pop()
        with self.assertRaises(ValueError):build_pairwise({'Repetitions':10,'Cases':cases},list(range(10)),{})


if __name__=='__main__':unittest.main()
