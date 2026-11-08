import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
from endogenous_magnitude import distribution
from endogenous_paired_comparisons import paired_outcomes, transition_summary


class Magnitude(unittest.TestCase):
    def test_unordered_pairs_and_scale_invariance(self):
        result=distribution([1.,2.,3.])
        self.assertEqual(result['Pairs'],3)
        self.assertAlmostEqual(result['MeanAbsolutePairwiseGap'],4/3)
        self.assertAlmostEqual(result['PairwiseGapFractionOfMean'],2/3)
        scaled=distribution([10.,20.,30.])
        self.assertAlmostEqual(scaled['MeanAbsolutePairwiseGap'],40/3)
        self.assertAlmostEqual(scaled['PairwiseGapFractionOfMean'],2/3)
        self.assertEqual(distribution([2.]*10)['Pairs'],45)

    def test_signed_and_absolute_effects_do_not_cancel_together(self):
        result=paired_outcomes([1.,100.],[2.,99.])
        self.assertEqual(result['MeanDelta'],0)
        self.assertAlmostEqual(result['MeanRelativeChange'],.495)
        self.assertAlmostEqual(result['MeanAbsoluteRelativeChange'],.505)
        self.assertEqual(result['MeanAbsoluteDelta'],1)
        group=dict(Id='one',Parameter='fee',Pairs=[dict(From='0',To='1',gini=result,liu=result)])
        summary=transition_summary([group])[0]['gini']
        self.assertAlmostEqual(summary['MeanSignedRelativeEffect'],.495)
        self.assertAlmostEqual(summary['MeanAbsoluteRelativeEffect'],.505)

    def test_backgrounds_receive_equal_weight_after_percentages(self):
        groups=[]
        for i,(before,after) in enumerate([([100.]*10,[110.]*10),([1.]*10,[.8]*10)]):
            result=paired_outcomes(before,after)
            groups.append(dict(Id=str(i),Parameter='fee',Pairs=[dict(From='0',To='1',gini=result,liu=result)]))
        summary=transition_summary(groups)[0]['gini']
        self.assertAlmostEqual(summary['MeanSignedRelativeEffect'],-.05)
        self.assertAlmostEqual(summary['MeanAbsoluteRelativeEffect'],.15)
        self.assertEqual(summary['MeanWithinSettingSpreadA'],0)

    def test_zero_reference_and_missing_outcomes_are_explicit(self):
        self.assertEqual(distribution([0.]*10)['PairwiseGapFractionOfMean'],0)
        conditional=distribution([None,0.,2.])
        self.assertEqual((conditional['Observations'],conditional['ExpectedObservations']),(2,3))
        self.assertEqual(conditional['Mean'],1)
        self.assertIsNone(distribution([None])['Mean'])
        result=paired_outcomes([0.,1.],[0.,2.])
        self.assertEqual(result['RelativeChangeCount'],1)
        self.assertIsNone(result['MeanRelativeChange'])
        self.assertIsNone(result['MeanAbsoluteRelativeChange'])
        with self.assertRaises(ValueError):distribution([0.,math.inf])


if __name__=='__main__':unittest.main()
