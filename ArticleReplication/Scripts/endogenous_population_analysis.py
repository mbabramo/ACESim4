"""Build auditable report data for the endogenous ten-start pooled experiment.

Published CSV numbers and unrounded saved-policy replays are separate comparators.
Initialization ranges are descriptive ranges, not confidence intervals.
"""
import argparse
from collections import defaultdict
from decimal import Decimal
import hashlib
import json
import math
from pathlib import Path
import statistics

import population_archives as archives


MEASURES = {
    'PrecautionLevel': ('Precaution Level', 'mean precaution level'),
    'Engagement': ('Engages in Activity', 'probability'),
    'HarmCost': ('Harm Cost', 'expected loss per opportunity'),
    'LitigationExpenditures': ('Expenditures', 'expected expenditure per opportunity'),
    'SocialWelfareLoss': ('Social Welfare Loss', 'expected loss per opportunity'),
    'OpportunityCost': ('Opportunity Cost', 'expected cost per opportunity'),
    'DisputeProbability': ('Accident Occurs', 'probability including attributed accidents'),
    'WrongfulAttribution': ('Wrongful Attribution', 'probability'),
    'Filing': ('P Files', 'unconditional probability'),
    'AnsweringUnconditional': ('D Answers', 'unconditional probability'),
    'Trial': ('Trial', 'unconditional probability'),
    'Settlement': ('Settles', 'unconditional probability'),
    'FilingGivenDispute': ('P Files', 'probability given a dispute'),
    'AnsweringGivenDispute': ('D Answers', 'probability given a dispute'),
    'TrialGivenDispute': ('Trial', 'probability given a dispute'),
    'SettlementGivenDispute': ('Settles', 'probability given a dispute'),
}
ANCHORS = {
    'Fee Shifting Multiplier': '0', 'Damages Multiplier': '1',
    'Liability Threshold': '1', 'Costs Multiplier': '1',
    'Risk Aversion': 'Moderately Risk Averse', 'Unit Precaution Cost': '1E-05',
    'Cost Misestimation': '1', 'Wrongful Attribution Probability': 'Normal',
    'Relative Costs': '1', 'Court Noise': 'Baseline',
    'Noise Multiplier P': '1', 'Noise Multiplier D': '1',
    'Proportion of Costs at Beginning': '0.5', 'Num Signals and Offers': 'Baseline',
}


def read(path):
    return archives.read(path)


def sha(path):
    return hashlib.sha256(archives.physical_path(path).read_bytes()).hexdigest()


def printed_number(value):
    """Return the printed value and half its last displayed decimal place.

    This is a rounding-resolution indicator, not an accuracy guarantee or a
    confidence interval. An empty source field remains unavailable.
    """
    if value is None or not str(value).strip():
        return None, None
    number = Decimal(str(value))
    if not number.is_finite():
        raise ValueError('Non-finite published value')
    return float(number), float(Decimal(10) ** number.as_tuple().exponent / 2)


def published_metrics(case):
    values, resolution = {}, {}
    for metric, (column, _) in MEASURES.items():
        table = case['HistoricalGivenDispute'] if metric.endswith('GivenDispute') else case['Historical']
        values[metric], resolution[metric] = printed_number(table[column])
    return values, resolution


def outcome_metrics(outcomes):
    values = {k: outcomes['Measures'][k] for k in MEASURES if k in outcomes['Measures']}
    for key in ('Filing', 'AnsweringUnconditional', 'Trial', 'Settlement'):
        values[key] = outcomes[key]
    for key in ('Filing', 'Answering', 'Trial', 'Settlement'):
        values[key + 'GivenDispute'] = outcomes['GivenDispute'][key]
    if set(values) != set(MEASURES):
        raise ValueError('Incomplete outcome measures')
    if any(v is not None and not math.isfinite(v) for v in values.values()):
        raise ValueError('Non-finite outcome')
    social = values['OpportunityCost'] + values['HarmCost'] + values['LitigationExpenditures']
    if not math.isclose(social, values['SocialWelfareLoss'], rel_tol=1e-10, abs_tol=1e-12):
        raise ValueError('Social-cost decomposition does not reconcile')
    dispute = values['DisputeProbability']
    for numerator, conditional in [('Filing', 'FilingGivenDispute'),
                                   ('AnsweringUnconditional', 'AnsweringGivenDispute'),
                                   ('Trial', 'TrialGivenDispute'), ('Settlement', 'SettlementGivenDispute')]:
        expected = values[numerator] / dispute if dispute > 0 else None
        actual = values[conditional]
        if expected is None:
            if actual is not None:
                raise ValueError('Zero-denominator conditional must remain unavailable')
        elif actual is None or not math.isclose(expected, actual, rel_tol=1e-9, abs_tol=1e-12):
            raise ValueError('Conditional outcome denominator differs: ' + conditional)
    return values


def matched_pairs(cases, parameter, anchor):
    """Pairs hold every other recorded setting fixed, regardless of family label."""
    groups = defaultdict(dict)
    for case in cases:
        settings = case['Parameters']
        key = tuple(sorted((k, v) for k, v in settings.items() if k != parameter))
        level = settings[parameter]
        if level in groups[key]:
            raise ValueError('Ambiguous matched setting: ' + parameter)
        groups[key][level] = case['Id']
    pairs = []
    for group in groups.values():
        if anchor in group:
            pairs.extend({'Parameter': parameter, 'ReferenceLevel': anchor, 'ComparisonLevel': level,
                          'ReferenceCase': group[anchor], 'ComparisonCase': cid}
                         for level, cid in group.items() if level != anchor)
    return sorted(pairs, key=lambda p: (p['ReferenceCase'], p['ComparisonLevel'], p['ComparisonCase']))


def checked_rows(run, cases, repetitions_seed=None):
    plan = read(run / 'plan.json')
    if plan['Model'] != 'endogenous' or plan['Settings']['MaximumNormalizedGain'] != 1e-7:
        raise ValueError('Wrong model or numerical tolerance')
    if repetitions_seed is not None and plan['InitialSeed'] != repetitions_seed:
        raise ValueError('Wrong independent seed')
    if {c['Id'] for c in plan['Cases']} != set(cases):
        raise ValueError('Population plan has different cases')
    done = read(run / 'completed.json')
    if not done['Passed'] or done['Cases'] != len(cases) or done['Runs'] != 2 * len(cases):
        raise ValueError('Incomplete population run')
    rows = {(r['CaseId'], r['Selector']): r for r in done['Results']}
    if len(rows) != len(done['Results']) or set(rows) != {(cid, s) for cid in cases for s in ('gini', 'liu')}:
        raise ValueError('Missing or duplicate selection')
    return rows


def build_analysis(root, expected_cases=2268, expected_repetitions=10):
    root = Path(root).resolve()
    complete = read(root / 'completed.json')
    if not complete['Passed'] or complete['ArticleCases'] != expected_cases or complete['CombinedRuns'] != 2 * expected_cases:
        raise ValueError('The requested pooled experiment is not complete')
    batch = read(root / 'batch-plan.json')
    catalog_path = Path(batch['Settings']['input']) / 'catalog.json'
    catalog = read(catalog_path)
    if sha(catalog['ArticleCsv']) != catalog['ArticleCsvSha256']:
        raise ValueError('Published CSV identity changed')
    case_catalog = {c['Id']: c for c in catalog['Cases']}
    manifest = read(root / 'repetitions.json')
    cases = manifest['Cases']
    if len(cases) != expected_cases or len(manifest['Runs']) != expected_repetitions:
        raise ValueError('Unexpected experiment dimensions')
    if len({c['Id'] for c in cases}) != len(cases) or any(c != case_catalog[c['Id']] for c in cases):
        raise ValueError('Pooled case catalog differs from published-input catalog')
    ids = {c['Id'] for c in cases}
    initial, repaired = {}, set()
    for rep, path in enumerate(manifest['Runs'], 1):
        run = Path(path)
        seed = batch['Settings']['initial_seed'] + (rep - 1) * batch['Settings']['seed_stride']
        initial[rep] = checked_rows(run, ids, seed)
    for replacement in manifest['Replacements']:
        rep, run = replacement['repetition'], Path(replacement['run'])
        plan = read(run / 'plan.json')
        replacement_ids = {c['Id'] for c in plan['Cases']}
        if not replacement_ids <= ids or not 1 <= rep <= expected_repetitions:
            raise ValueError('Unexpected replacement')
        for cid in replacement_ids:
            if (rep, cid) in repaired:
                raise ValueError('Duplicate replacement')
            repaired.add((rep, cid))
        seed = batch['Settings']['initial_seed'] + (rep - 1) * batch['Settings']['seed_stride']
        initial[rep].update(checked_rows(run, replacement_ids, seed))
    for rows in initial.values():
        if any(r['Status'] != 'FullGameCceVerified' or max(r['NormalizedGains']) > 1e-7 for r in rows.values()):
            raise ValueError('Every initial selection must have an accepted result')
    result_cases = []
    for case in cases:
        cid = case['Id']; receipt_path = root / 'combined' / cid / 'completed.json'; receipt = read(receipt_path)
        if not receipt['Passed'] or receipt['Case'] != case or receipt['Repetitions'] != expected_repetitions:
            raise ValueError('Invalid pooled receipt')
        final = Path(receipt['FinalRun']); rows = checked_rows(final, {cid})
        audit_path = Path(receipt['Audit']); audit = read(audit_path)
        if not audit['Passed'] or len(audit['Rows']) != 2:
            raise ValueError('Invalid independent final audit')
        audits = {a['Selector']: a for a in audit['Rows']}
        published, printed_resolution = published_metrics(case)
        replay = outcome_metrics(rows[cid, 'gini']['Baseline'])
        if replay != outcome_metrics(rows[cid, 'liu']['Baseline']):
            raise ValueError('Selector baselines differ')
        selectors = {}
        for selector in ('gini', 'liu'):
            row = rows[cid, selector]; a = audits[selector]
            if row['Status'] != 'FullGameCceVerified' or max(row['NormalizedGains']) > 1e-7:
                raise ValueError('Unverified final selection')
            if (a['Status'] != 'FullGameCceVerified' or max(a['NormalizedCceGains']) > 1e-7
                    or a['MaximumNormalizedGainTarget'] != 1e-7):
                raise ValueError('Independent CCE audit does not pass the target')
            details_path = final / row['DetailsFile'] if 'DetailsFile' in row else final / cid / selector / 'result.json'
            details_hash = sha(details_path)
            if a['CaseId'] != cid or a['SourceResultSha256'] != details_hash:
                raise ValueError('Audit/result identity differs')
            if 'DetailsSha256' in row and details_hash != row['DetailsSha256']:
                raise ValueError('Full result hash differs')
            full = read(details_path)
            if any(full[k] != v for k, v in row.items() if k not in ('DetailsFile', 'DetailsSha256')):
                raise ValueError('Compact/full result differs')
            starts = [outcome_metrics(initial[rep][cid, selector]['Outcomes']) for rep in range(1, expected_repetitions + 1)]
            ranges = {}
            for metric in MEASURES:
                values = [s[metric] for s in starts if s[metric] is not None]
                ranges[metric] = {'Minimum': min(values) if values else None, 'Maximum': max(values) if values else None,
                                  'Median': statistics.median(values) if values else None, 'Available': len(values)}
            nash = max(a['NormalizedProductNashGains'])
            if a['ProductIsApproximateNashAtRunTolerance'] != (nash <= 1e-7) or a['ProductIsApproximateNashAt1eMinus5'] != (nash <= 1e-5):
                raise ValueError('Nash classification differs from measured gains')
            selectors[selector] = {'Pooled': outcome_metrics(row['Outcomes']), 'InitialValues': starts,
                'InitializationRanges': ranges, 'RawCceGains': row['RawGains'], 'NormalizedCceGains': row['NormalizedGains'],
                'NormalizedProductNashGains': a['NormalizedProductNashGains'],
                'ProductNashAt1eMinus7': nash <= 1e-7, 'ProductNashAt1eMinus5': nash <= 1e-5,
                'CommonPopulationCounts': receipt['CommonPopulationCounts'], 'ResultSha256': details_hash}
        result_cases.append({'Id': cid, 'Family': case['Family'], 'OptionSetName': case['OptionSetName'],
            'Parameters': case['Parameters'], 'Published': published, 'PublishedHalfPrintedUnit': printed_resolution,
            'SavedPolicyReplay': replay, 'Selectors': selectors, 'ReceiptSha256': sha(receipt_path), 'AuditSha256': sha(audit_path),
            'RepairedRepetitions': [rep for rep in range(1, expected_repetitions + 1) if (rep, cid) in repaired]})
    return {'Schema': 'endogenous-pooled-report-analysis-v1', 'ArticleCases': expected_cases,
        'Repetitions': expected_repetitions, 'AcceptedInitialSelections': expected_cases * expected_repetitions * 2,
        'PooledSelections': expected_cases * 2, 'ReplacementCases': len(repaired),
        'Provenance': {'CompletedSha256': sha(root / 'completed.json'), 'BatchPlanSha256': sha(root / 'batch-plan.json'),
                       'RepetitionManifestSha256': sha(root / 'repetitions.json'), 'CatalogSha256': sha(catalog_path),
                       'PublishedCsvSha256': catalog['ArticleCsvSha256']},
        'Measures': {k: {'PublishedColumn': v[0], 'Unit': v[1]} for k, v in MEASURES.items()},
        'Cases': result_cases,
        'MatchedPairs': [pair for parameter, anchor in ANCHORS.items() for pair in matched_pairs(cases, parameter, anchor)],
        'Interpretation': {'Baseline': 'Published CSV is the primary comparator; unrounded saved-policy replay is reported separately.',
                           'Ranges': 'Ten accepted independent-seed starts, with documented within-seed repairs. Ranges are not confidence intervals.',
                           'Matching': 'Every other recorded parameter is fixed within each pair. Missing matches are not imputed.',
                           'PrintedResolution': 'Half the last printed decimal place is a display-resolution indicator, not an accuracy guarantee.',
                           'Equilibrium': 'Normal-form approximate CCE. Product-marginal Nash is a separate check.'}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--expected-cases', type=int, default=2268)
    parser.add_argument('--expected-repetitions', type=int, default=10)
    args = parser.parse_args()
    data = build_analysis(args.run, args.expected_cases, args.expected_repetitions)
    with Path(args.output).open('x', encoding='utf-8') as f:
        json.dump(data, f, allow_nan=False, separators=(',', ':'))
    print(json.dumps({k: data[k] for k in ('ArticleCases', 'Repetitions', 'AcceptedInitialSelections', 'PooledSelections', 'ReplacementCases')}))


if __name__ == '__main__':
    main()
