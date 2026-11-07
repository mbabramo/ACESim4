import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
from endogenous_story_report import contrast_envelope, joint_pairs, quantile, safe_ratio, span


class StoryComparisons(unittest.TestCase):
    def test_contrast_envelope_all_observed_combinations(self):
        self.assertEqual(contrast_envelope([1,2],[3,5]),{'Lower':1,'Upper':4,'Direction':'positive'})
        self.assertEqual(contrast_envelope([3,5],[1,2])['Direction'],'negative')
        self.assertEqual(contrast_envelope([1,4],[3,5])['Direction'],'overlap')
        self.assertEqual(contrast_envelope([None],[3])['Direction'],'unavailable')

    def test_zero_and_missing_are_distinct(self):
        self.assertEqual(span([0,0])['Width'],0)
        self.assertIsNone(span([0,0])['RelativeWidth'])
        self.assertEqual(span([None,None])['Available'],0)
        self.assertEqual(span([0,None,2])['Available'],2)

    def test_quantiles_are_descriptive_inclusive_interpolation(self):
        self.assertEqual(quantile([0,10],.9),9)
        self.assertEqual(quantile([1],.9),1)
        self.assertIsNone(quantile([None,float('nan')],.5))

    def test_tiny_denominators_do_not_create_infinite_comparisons(self):
        self.assertIsNone(safe_ratio(1e-4,1e-320))
        self.assertIsNone(safe_ratio(1,0))
        self.assertEqual(safe_ratio(1e-4,1e-4),1)

    def test_joint_comparisons_hold_other_parameters_fixed(self):
        cases=[{'Id':'a','Parameters':{'P':'1','D':'1','Cost':'1'}},
               {'Id':'b','Parameters':{'P':'.5','D':'2','Cost':'1'}},
               {'Id':'c','Parameters':{'P':'.5','D':'2','Cost':'4'}}]
        pairs=joint_pairs(cases,['P','D'],['1','1'],'Joint noise')
        self.assertEqual([(p['ReferenceCase'],p['ComparisonCase']) for p in pairs],[('a','b')])


if __name__=='__main__':unittest.main()
