"""Audit Complete Fee-Shifting answering strategies in the saved routine cases."""
import argparse
import csv
from decimal import Decimal
import hashlib
import json
from pathlib import Path


FOLDERS = {
    'baseline': 'Baseline', 'baseline-offers-15': 'Offer-grid sensitivity',
    'low-noise': 'Low noise', 'high-noise': 'High noise',
    'direct-binary-state-signals': 'Direct binary-state signals',
    'truth-conditioned-latent-merits': 'Truth-conditioned latent merits',
    'center-weighted-continuous-merits': 'Center-weighted continuous merits',
    'polarized-continuous-merits': 'Polarized continuous merits',
    'all-costs-avoidable': 'All litigation costs avoidable at bargaining',
    'all-costs-sunk': 'All litigation costs sunk before bargaining',
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rows(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def audit(article):
    article = article.resolve()
    source = article / 'Results/Aggregated Data/Sources/welfare-outcomes.csv'
    cases = [r for r in rows(source) if r['Comparison Fee Rule'] == 'Complete Fee-Shifting']
    if not cases or len({r['OptionSetName'] for r in cases}) != len(cases):
        raise ValueError('Missing or duplicate Complete Fee-Shifting cases')
    records = []
    for case in cases:
        family = case['Comparison Family']
        risk = {'Risk Neutral': 'Risk Neutral', 'Moderately Risk Averse': 'Risk Averse'}[case['Risk Aversion']]
        cost = format(Decimal(case['Costs Multiplier']).normalize(), 'f')
        path = (article / 'Results/Individual simulations' / FOLDERS[family] / risk
                / 'Complete Fee-Shifting/Sources' / f'cost-{cost}-InformationSetActions.csv')
        answers = [r for r in rows(path) if r['Decision'] == 'D Answers' and r['Action Label'] == 'Yes']
        if (len(answers) != int(case['Number of Signals'])
                or len({r['Information Set Number'] for r in answers}) != len(answers)
                or {r['OptionSetName'] for r in answers} != {case['OptionSetName']}):
            raise ValueError('Incomplete or mismatched answering strategies: ' + str(path))
        probabilities = [Decimal(r['Equilibrium Action Probability']) for r in answers]
        if any(p != 1 for p in probabilities):
            raise ValueError('Answering is not certain in every information set: ' + str(path))
        reached = [r for r in answers if r['Off Path'].lower() == 'false']
        records.append({'family': family, 'risk': case['Risk Aversion'], 'cost': cost,
                        'option_set': case['OptionSetName'], 'filing': case['P Files'],
                        'answer_sets': len(answers), 'reached_sets': len(reached),
                        'minimum_all_answer_prob': str(min(probabilities)),
                        'minimum_reached_answer_prob': '1' if reached else None,
                        'source': str(path.relative_to(article)), 'source_sha256': sha(path)})
    output = article / 'Supplemental materials/Generated pairwise comparisons/Sources/answering-strategy-audit.json'
    result = {'source': str(source.relative_to(article)), 'source_sha256': sha(source),
              'generator': {'path': str(Path(__file__).resolve()), 'sha256': sha(Path(__file__))},
              'cases': len(records), 'answering_information_sets': sum(r['answer_sets'] for r in records),
              'every_answer_probability_is_one': True, 'records': records}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return {'output': str(output), 'cases': result['cases'],
            'answering_information_sets': result['answering_information_sets']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--article-directory', required=True, type=Path)
    print(json.dumps(audit(parser.parse_args().article_directory), indent=2))
