import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parents[1] / 'Scripts'))
import endogenous_population_analysis as report


class EndogenousAnalysisTests(unittest.TestCase):
    def test_missing_published_value_is_not_zero_and_precision_is_preserved(self):
        self.assertEqual((None, None), report.printed_number(''))
        self.assertEqual((0.0, 5e-9), report.printed_number('0.00000000'))
        value, half_unit = report.printed_number('1.451E-07')
        self.assertEqual(1.451e-7, value)
        self.assertAlmostEqual(5e-11, half_unit, places=20)

    def test_pairs_do_not_confound_cost_or_risk_and_ambiguous_cases_fail(self):
        def case(cid, fee, cost, risk):
            return {'Id': cid, 'Parameters': {'fee': fee, 'cost': cost, 'risk': risk}}
        cases = [case('a', '0', '1', 'RN'), case('b', '1', '1', 'RN'),
                 case('c', '1', '4', 'RN'), case('d', '1', '1', 'MRA')]
        pairs = report.matched_pairs(cases, 'fee', '0')
        self.assertEqual([('a', 'b')], [(p['ReferenceCase'], p['ComparisonCase']) for p in pairs])
        with self.assertRaisesRegex(ValueError, 'Ambiguous'):
            report.matched_pairs(cases + [case('duplicate', '0', '1', 'RN')], 'fee', '0')

    def outcomes(self):
        return {'Measures': {'PrecautionLevel': 2, 'Engagement': 0.8, 'HarmCost': .003,
            'LitigationExpenditures': .002, 'SocialWelfareLoss': .006, 'OpportunityCost': .001,
            'DisputeProbability': .1, 'WrongfulAttribution': .01},
            'Filing': .08, 'AnsweringUnconditional': .06, 'Trial': .02, 'Settlement': .03,
            'GivenDispute': {'Filing': .8, 'Answering': .6, 'Trial': .2, 'Settlement': .3}}

    def test_conditional_rates_use_disputes_and_cost_components_reconcile(self):
        outcomes = self.outcomes()
        result = report.outcome_metrics(outcomes)
        self.assertEqual(.6, result['AnsweringGivenDispute'])
        wrong = copy.deepcopy(outcomes)
        wrong['GivenDispute']['Answering'] = .75  # answering given filing, a different denominator
        with self.assertRaisesRegex(ValueError, 'denominator'):
            report.outcome_metrics(wrong)
        wrong = copy.deepcopy(outcomes)
        wrong['Measures']['SocialWelfareLoss'] += .001
        with self.assertRaisesRegex(ValueError, 'decomposition'):
            report.outcome_metrics(wrong)

    def test_zero_disputes_preserves_unavailable_conditionals(self):
        outcomes = self.outcomes()
        outcomes['Measures']['DisputeProbability'] = 0
        for k in ('Filing', 'AnsweringUnconditional', 'Trial', 'Settlement'):
            outcomes[k] = 0
        outcomes['GivenDispute'] = dict.fromkeys(outcomes['GivenDispute'])
        self.assertIsNone(report.outcome_metrics(outcomes)['TrialGivenDispute'])
        outcomes['GivenDispute']['Trial'] = 0
        with self.assertRaisesRegex(ValueError, 'Zero-denominator'):
            report.outcome_metrics(outcomes)


if __name__ == '__main__':
    unittest.main()
