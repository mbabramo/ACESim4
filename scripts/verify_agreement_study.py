"""Independent accounting and source checks for the single-equilibrium study.

Recomputes the four-factor allocation from saved coalition coordinates using
subset weights, independently of the C# permutation implementation. No solves.
"""
import argparse
from collections import Counter
import csv
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import re


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2, allow_nan=False) + '\n', encoding='utf-8')


@lru_cache(None)
def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fingerprints(obj):
    if isinstance(obj, dict):
        if 'Path' in obj and 'Sha256' in obj:
            yield obj
        for value in obj.values():
            yield from fingerprints(value)
    elif isinstance(obj, list):
        for value in obj:
            yield from fingerprints(value)


def check_fingerprint(f):
    if sha(f['Path']).lower() != f['Sha256'].lower():
        raise ValueError('Changed source/output: ' + f['Path'])


def near(left, right, label, tolerance=1e-6):
    if not math.isfinite(left) or not math.isfinite(right) or abs(left - right) > tolerance:
        raise ValueError(f'{label}: {left!r} versus {right!r}')


def allocate(original, target, values):
    if len(values) != 16:
        raise ValueError('All sixteen agreement-game coalitions are required')
    effects = []
    for bit in range(4):
        effect = 0.0
        for subset in range(16):
            if subset & (1 << bit):
                continue
            size = subset.bit_count()
            weight = math.factorial(size) * math.factorial(3 - size) / math.factorial(4)
            effect += weight * (values[subset | (1 << bit)] - values[subset])
        effects.append(effect)
    result = dict(zip(['Entry', 'Offers', 'Exit', 'Agreement'], effects))
    result.update(Original=original, Target=target, Direct=values[0] - original,
                  SelectionResidual=target - values[-1], Change=target - original)
    result['Explained'] = result['Direct'] + sum(effects)
    near(result['Change'], result['Explained'] + result['SelectionResidual'], 'Allocation accounting')
    return result


def verify_row(row, result, scenarios, endpoints):
    key, player = row['Key'], row['Player']
    old, new = (endpoint[key] for endpoint in endpoints)
    if old['ActualOffPath'] or new['ActualOffPath']:
        raise ValueError('A decomposition row lacks two reached endpoints: ' + key)
    for actual, expected in [(row['OriginalPolicy'], old['Actions']), (row['TargetPolicy'], new['Actions'])]:
        for probability, action in zip(actual, expected, strict=True):
            near(probability, action['Probability'], 'Endpoint policy', 1e-10)

    def value(info):
        actions = info['Actions']
        if row['Metric'] == 'offer amount':
            if sum(a['Probability'] > 1e-6 for a in actions) != 1:
                raise ValueError('A mixed offer was reduced to an amount')
            return sum(a['Probability'] * v for a, v in zip(actions, result['OfferValues'], strict=True))
        if row['Metric'] == 'offer action probability':
            return 100 * actions[row['Action'] - 1]['Probability']
        return 100 * next(a['Probability'] for a in actions if a['Label'] == 'Yes')

    primary = [scenarios[('coalition', str(mask), player)][key] for mask in range(16)]
    if row['CounterfactualUndefined'] != any(i['CounterfactuallyUnreachable'] for i in primary):
        raise ValueError('Incorrect undefined-conditional flag')
    if row['UnreachedCoalitions'] != [m for m, info in enumerate(primary) if info['ActualOffPath']]:
        raise ValueError('Incorrect intermediate reach flags')
    expected = allocate(value(old), value(new), [value(i) for i in primary])
    for name, number in expected.items():
        near(row['Allocation'][name], number, key + ': ' + name)
    return expected


def csv_write(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = list(dict.fromkeys(k for row in rows for k in row))
    with path.open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def verify_parameters(study):
    enabled = read(study / 'Sources/parameters.json')
    disabled = read(study / 'Baseline/Sources/parameters.json')
    baseline = {p['Name']: p for p in disabled['Parameters']}

    def model(options):
        if isinstance(options, dict):
            return {k: model(v) for k, v in options.items() if k != 'LitigGameDefinition'}
        if isinstance(options, list):
            return [model(v) for v in options]
        return options

    verified = []
    for case in enabled['Parameters']:
        name = case['Name'].removeprefix('Agreement-Enabled__')
        old = baseline[name]
        left, right = model(old['Options']), model(case['Options'])
        if left['IncludeAgreementToBargainDecisions'] or not right['IncludeAgreementToBargainDecisions']:
            raise ValueError('Unexpected agreement option')
        for options in [left, right]:
            options.pop('Name')
            options.pop('IncludeAgreementToBargainDecisions')
            for metadata in ['Initialization Starts', 'Agreement To Bargain']:
                options['VariableSettings'].pop(metadata, None)
        if left != right:
            raise ValueError('Unexpected model parameter difference: ' + name)
        for numerical in ['MaxIntegralUtility', 'RoundOffChanceDigits']:
            if old[numerical] != case[numerical]:
                raise ValueError('Changed numerical convention: ' + numerical)
        verified.append({'Enabled': case['Name'], 'Disabled': name, 'EnabledTree': case['Tree'],
                         'DisabledTree': old['Tree']})
    if len(verified) != 30 or len(baseline) != 30:
        raise ValueError('Thirty parameter matches are required')
    report = {'Cases': verified, 'SubstantiveModelDifference': 'IncludeAgreementToBargainDecisions: false to true',
              'ExcludedFromParameterComparison': ['Names and two study metadata labels',
                                                   'Dispute-generator back-reference to its initialized runtime game'],
              'Compared': 'All other exported option fields, nested calculators and distributions, integral utility precision and chance rounding'}
    write(study / 'Sources/parameter-comparison-verification.json', report)
    return {'ParameterMatches': len(verified)}


def flat_row(row, result, selected):
    return {'Contrast': result['Contrast']['Id'], 'Selected': selected,
            'SourceOptionSet': result['SourceOptionSet'], 'TargetOptionSet': result['TargetOptionSet'],
            **{k: row[k] for k in ['Key', 'Player', 'Decision', 'Signal', 'SignalValue', 'ExitCommitment',
                                  'Metric', 'Action', 'CounterfactualUndefined', 'EndpointSelection',
                                  'TieSensitive', 'CompletionSensitive', 'MaximumSensitivity']},
            'UnreachedCoalitions': json.dumps(row['UnreachedCoalitions']),
            'OriginalPolicy': json.dumps(row['OriginalPolicy']), 'TargetPolicy': json.dumps(row['TargetPolicy']),
            'ActionLabels': json.dumps(row['ActionLabels']), **row['Allocation']}


def signed_numbers(text):
    text = text.replace('\u2212', '-').replace('\u2013', ' ').replace('--', ' ')
    text = re.sub(r'(?<=\d)\s*\.\s*(?=\d)', '.', text)
    return [float(re.sub(r'\s', '', value)) for value in re.findall(r'[+-]?\s*\d+(?:\.\d+)?', text)]


def verify_decompositions(study):
    from pypdf import PdfReader
    plan = read(study / 'agreement-decomposition-plan.json')
    expected = {(a['id'], b['id']) for a in plan['Cases'] for b in plan['Cases']
                if a['cost'] == b['cost'] and ((a['fee'] != b['fee']) != (a['alpha'] != b['alpha']))}
    if len(expected) != 90:
        raise ValueError('Expected ninety directed contrasts')
    root = study / 'Equilibrium strategy changes'
    seen, table_seen, full_rows, selected_rows = set(), set(), [], []
    counts = Counter()
    maximum_action_error = 0.0
    for path in sorted((root / 'Data').glob('cost-*/*/equilibrium-changes-manifest.json')):
        manifest = read(path)
        if manifest['Schema'] != '3':
            raise ValueError('Agreement decomposition schema required')
        request = read(path.parent / 'equilibrium-changes.request.json')
        if not request['CheckOffPathCompletions'] or not request['CheckTieSensitivity']:
            raise ValueError('Sensitivity checks were disabled')
        for f in fingerprints(manifest):
            check_fingerprint(f)
            counts['FingerprintsChecked'] += 1
        for name in manifest['OutputJsonFiles']:
            result = read(name)
            pair = (result['Contrast']['Source'], result['Contrast']['Target'])
            if pair in seen:
                raise ValueError('Duplicate contrast')
            seen.add(pair)
            scenarios = {}
            for scenario in result['Scenarios']:
                r = scenario['Result']
                near(r['ResponseUtility'], r['BestResponseUtility'], 'Independent response replay', 1e-7)
                near(r['TerminalProbability'], 1, 'Response terminal probability', 1e-9)
                maximum_action_error = max(maximum_action_error, abs(r['MaxActionValueError']))
                if abs(r['MaxActionValueError']) > 1e-7:
                    raise ValueError('Conditional action value replay failed')
                index = (scenario['Panel'], scenario['Component'], r['Player'])
                if index in scenarios:
                    raise ValueError('Duplicate coalition/check')
                scenarios[index] = {i['Key']: i for i in r['InformationSets']}
                counts['DiagnosticResponses'] += 1
            for player in [0, 1]:
                for mask in range(16):
                    for panel in ['coalition', 'tie-low', 'tie-high']:
                        if (panel, str(mask), player) not in scenarios:
                            raise ValueError('Missing coalition or tie check')
            endpoints = [{i['Key']: i for i in result[k]['InformationSets']}
                         for k in ['SourceEquilibrium', 'TargetEquilibrium']]
            for row in result['Changes']:
                verify_row(row, result, scenarios, endpoints)
                full_rows.append(flat_row(row, result, False))
            selected_path = root / 'Sources/Json' / (result['Contrast']['Id'] + '.json')
            selected = read(selected_path)
            if selected['Contrast'] != result['Contrast']:
                raise ValueError('Table references a different contrast')
            table_seen.add(pair)
            for f in fingerprints(selected):
                check_fingerprint(f)
            for row in selected['SelectedRows']:
                verify_row(row, result, scenarios, endpoints)
                selected_rows.append(flat_row(row, result, True))
            stem = result['Contrast']['Id']
            tex = (root / 'Sources/Tex' / (stem + '.tex')).read_text(encoding='utf-8-sig')
            if 'Opponent\\\\agreement' not in tex:
                raise ValueError('Table omits the agreement contribution')
            pdf, png = root / 'Tables' / (stem + '.pdf'), root / 'Tables' / (stem + '.png')
            if not png.is_file() or png.stat().st_size < 1000:
                raise ValueError('Missing PNG preview')
            document = PdfReader(pdf)
            if len(document.pages) != 1:
                raise ValueError('Unexpected multipage individual table')
            body = '\n'.join(line for line in tex.splitlines() if re.match(r'^[PD] ', line))
            cost = next(c['cost'] for c in plan['Cases'] if c['id'] == pair[0])
            expected_numbers = signed_numbers(cost + '\n' + body)
            actual_numbers = signed_numbers(document.pages[0].extract_text())
            if actual_numbers != expected_numbers:
                raise ValueError('Printed numbers/signs differ from TeX: ' + str(pdf))
            counts['PrintedNumbersChecked'] += len(expected_numbers)
            counts['Tables'] += 1
    if seen != expected or table_seen != expected:
        raise ValueError(f'Incomplete decompositions: {len(seen)} calculations, {len(table_seen)} tables')
    csv_write(root / 'Sources/all-decomposition-coordinates.csv', full_rows)
    csv_write(root / 'Sources/selected-decomposition-coordinates.csv', selected_rows)
    report = {'DirectedContrasts': len(seen), **dict(counts), 'FullCoordinates': len(full_rows),
              'SelectedCoordinates': len(selected_rows), 'MaximumActionValueError': maximum_action_error,
              'Method': 'Recomputed four-factor allocations from all 16 subsets; endpoint policies, reach flags, response replay, source hashes, printed PDF numbers and signs checked'}
    write(root / 'Sources/comparison-verification.json', report)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', type=Path, required=True)
    parser.add_argument('--parameters-only', action='store_true')
    args = parser.parse_args()
    study = args.study.resolve()
    result = verify_parameters(study)
    if not args.parameters_only:
        result.update(verify_decompositions(study))
    print(json.dumps(result, indent=2))
