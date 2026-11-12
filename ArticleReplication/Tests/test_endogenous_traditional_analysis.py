import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'Scripts'))
import endogenous_traditional_analysis as a


class Review(unittest.TestCase):
    def test_resource_split_does_not_double_count_opportunity(self):
        v=a.split_cost(dict(Engagement=.9,OpportunityCost=.00015))
        self.assertAlmostEqual(v['ForegoneActivityCost'],.0001)
        self.assertAlmostEqual(v['PrecautionCost'],.00005)
        self.assertIsNone(a.split_cost(dict(Engagement=.9,OpportunityCost=.00001))['PrecautionCost'])

    def test_stable_total_can_hide_component_reallocation(self):
        obs=[dict(SocialWelfareLoss=10,ForegoneActivityCost=0,PrecautionCost=care,HarmCost=10-care,LitigationExpenditures=0) for care in (2,4,6)]
        r=a.composition(obs)
        self.assertEqual(r['TotalSDRelativeToMean'],0)
        self.assertGreater(r['MeanPairwiseComponentMovementRelativeToMean'],0)
        self.assertEqual(r['ComponentMovementCancelledInTotal'],1)

    def test_paired_screen_keeps_same_repetitions_and_exact_ties(self):
        before=[{'Values':{'x':x}} for x in (10,20,30)]
        after=[{'Values':{'x':x}} for x in (11,18,30)]
        r=a.contrasts(before,after,[False,True,True],'x')
        self.assertEqual(r['Pairs'],2);self.assertEqual(r['BLower'],1);self.assertEqual(r['ExactTies'],1)
        self.assertEqual(r['Difference']['Mean'],-1)
        self.assertAlmostEqual(r['MeanPerSeedRelativeChange'],-.05)

    def test_singleton_and_empty_groups_do_not_invent_sd(self):
        self.assertIsNone(a.distribution([])['Mean'])
        self.assertIsNone(a.distribution([3])['SD'])
        self.assertEqual(a.distribution([3,3])['SD'],0)

    def test_legal_pairs_include_every_endpoint_pair_and_hold_background(self):
        cases=[dict(Id=str(i),Parameters={'Fee Shifting Multiplier':str(i),'Risk Aversion':'RN'}) for i in (0,1,2)]
        cases.append(dict(Id='other',Parameters={'Fee Shifting Multiplier':'2','Risk Aversion':'RA'}))
        pairs=list(a.exact_pairs(cases,'Fee Shifting Multiplier'))
        self.assertEqual([(p['From'],p['To']) for p in pairs],[('0','1'),('0','2'),('1','2')])
        self.assertTrue(all(p['Background']=={'Risk Aversion':'RN'} for p in pairs))

    def test_nash_and_money_screens_remain_separate(self):
        row=dict(EconomicScreenPassed=False,NashAtTarget=True,NashAt1eMinus5=True)
        self.assertTrue(a.accepted(row,'all'));self.assertFalse(a.accepted(row,'strict-money'))
        row.update(EconomicScreenPassed=True,NashAtTarget=False)
        self.assertTrue(a.accepted(row,'loose-money'));self.assertFalse(a.accepted(row,'strict-money'))


if __name__=='__main__':unittest.main()
