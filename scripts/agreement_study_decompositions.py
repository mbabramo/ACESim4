"""Plan and resume the 90 directed, within-model agreement-study decompositions.

Uses the existing process scheduler and saved-profile best-response workflow.
No equilibrium search is performed here.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import rebuild_article_supplemental as scheduler


def prepare(study, exe):
    audit = scheduler.read(study / 'Sources/strategy-verification.json')
    profiles = [scheduler.read(p) for p in sorted((study / 'Sources/Profiles').glob('*.json'))]
    if len(profiles) != 30 or len(audit['Profiles']) != 30:
        raise ValueError('Audit all 30 individual new equilibria before decomposing them')
    expected = {(c, a, f) for c in [.25, .5, 1, 2, 4] for a in [0, 2]
                for f in ['American', 'Trial Fee-Shifting', 'Complete Fee-Shifting']}
    if {(p['CostMultiplier'], p['Alpha'], p['FeeRule']) for p in profiles} != expected:
        raise ValueError('Incomplete or duplicate cost/risk/fee cases')
    changes = study / 'Equilibrium strategy changes'
    frozen = changes / 'Sources/Profiles'
    frozen.mkdir(parents=True, exist_ok=True)
    cases = []
    for p in profiles:
        a = next(x for x in audit['Profiles'] if x['OptionSet'] == p['OptionSet'])
        if a['MaximumGain'] > 1e-7 or p['Equilibrium'] != 1 or not p['AgreementEnabled']:
            raise ValueError('Only individually validated agreement-enabled profiles are allowed')
        fee = {'American': 'american', 'Trial Fee-Shifting': 'trial', 'Complete Fee-Shifting': 'complete'}[p['FeeRule']]
        cost = scheduler.number(p['CostMultiplier'])
        risk = 'risk-neutral' if p['Alpha'] == 0 else 'risk-averse'
        case = {'id': risk + '-' + fee + '-cost-' + cost, 'option': p['OptionSet'],
                'cost': cost, 'alpha': str(p['Alpha']), 'fee': fee, 'risk': risk}
        for key, original in [('equilibrium', a['ProfileFile']), ('actions', a['ActionReport'])]:
            original = Path(original)
            saved = frozen / original.name
            if saved.exists() and scheduler.sha(saved) != scheduler.sha(original):
                raise ValueError('A frozen decomposition input changed: ' + str(saved))
            if not saved.exists():
                shutil.copy2(original, saved)
            case[key] = str(saved)
        cases.append(case)
    pairs = scheduler.contrasts(cases)
    if len(pairs) != 90:
        raise ValueError('Expected 90 directed fee/risk contrasts')
    jobs = []

    def job(id, phase, command, dependencies, inputs, expected_files, outputs):
        jobs.append({'id': id, 'phase': phase, 'command': [str(x) for x in command],
                     'dependencies': dependencies, 'inputs': [str(x) for x in inputs],
                     'expected': [str(x) for x in expected_files], 'outputs': outputs})

    for a, b in pairs:
        id = (a['fee'] + '-to-' + b['fee'] + '-' + a['risk'] if a['fee'] != b['fee']
              else a['risk'] + '-to-' + b['risk'] + '-' + a['fee']) + '-cost-' + a['cost']
        directory = changes / 'Data' / ('cost-' + a['cost']) / id
        request = directory / 'equilibrium-changes.request.json'
        sources = [{'Id': c['id'], 'OptionSetName': c['option'],
                    'EquilibriumFile': scheduler.relative(c['equilibrium'], directory),
                    'ActionReportFile': scheduler.relative(c['actions'], directory), 'EquilibriumNumber': 1}
                   for c in [a, b]]
        scheduler.write(request, {'OutputDirectory': '.', 'Sources': sources,
                                 'Contrasts': [{'Id': id, 'Label': id, 'Source': a['id'], 'Target': b['id']}],
                                 'CheckOffPathCompletions': True, 'CheckTieSensitivity': True})
        manifest = directory / 'equilibrium-changes-manifest.json'
        result = directory / (id + '.json')
        calculation = 'change-' + id
        job(calculation, 'calculations', [exe, 'equilibrium-changes', '--request', request, '--calculate-only'], [],
            [request] + [c[k] for c in [a, b] for k in ['equilibrium', 'actions']], [manifest, result],
            [(str(directory), '*.json')])
        tables = changes / 'Tables'
        job('table-' + id, 'tables', [exe, 'equilibrium-publication', '--request', request, '--output', tables],
            [calculation], [request, manifest, result],
            [tables / (id + '.pdf'), tables / (id + '.png'), changes / 'Sources/Tex' / (id + '.tex'),
             changes / 'Sources/Json' / (id + '.json')],
            [(str(tables), id + '.*'), (str(changes / 'Sources/Tex'), id + '.tex'),
             (str(changes / 'Sources/Json'), id + '.json')])
    plan = {'Schema': 1, 'Study': 'Agreement enabled; one equilibrium per case', 'Cases': cases,
            'DirectedContrasts': 90, 'OpponentComponents': ['participation', 'offers', 'exit', 'agreement'],
            'CoalitionsPerPlayer': 16, 'ReplacementOrders': 24, 'NewEquilibriumSearches': 0, 'Jobs': jobs}
    scheduler.write(study / 'agreement-decomposition-plan.json', plan)
    shutil.copy2(Path(__file__).parent / 'templates/agreement-decomposition-methodology.md',
                 changes / 'Methodology and Explanation.md')
    return plan


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', type=Path, required=True)
    parser.add_argument('--exe', type=Path, required=True)
    parser.add_argument('--jobs', type=int, default=min(30, os.cpu_count() or 1))
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args()
    if args.jobs < 1:
        parser.error('--jobs must be positive')
    os.environ['DOTNET_PROCESSOR_COUNT'] = '1'
    plan = prepare(args.study.resolve(), args.exe.resolve())
    if not args.prepare_only:
        scheduler.run(plan, args.study.resolve(), args.jobs, None)
