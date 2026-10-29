"""Index the endogenous article's saved current policies, logs, and printed results."""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--article-results', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    source, output = Path(args.article_results).resolve(), Path(args.output).resolve()
    if output.exists() or output == source or source in output.parents or output in source.parents:
        raise ValueError('Use a new output directory separate from the article.')
    table = source / 'PREC010 output.csv'
    with table.open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    all_rows = {r['OptionSetName']: r for r in rows if r['Filter'] == 'All'}
    conditional = {r['OptionSetName']: r for r in rows if r['Filter'] == 'Dispute Arises'}
    profiles = {}
    for p in (source / 'Individual Simulation Results').rglob('* -equ.csv'):
        name = p.name.removeprefix('PREC010 ').removesuffix(' -equ.csv').replace(',', '-')
        if name in profiles:
            raise ValueError('Duplicate saved profile: ' + name)
        profiles[name] = p
    if set(profiles) != set(all_rows) or set(conditional) != set(all_rows):
        raise ValueError('Article rows and saved profiles do not have identical coverage.')
    cases = []
    for name, row in sorted(all_rows.items()):
        p = profiles[name]
        log = p.parent.parent / 'Logs' / p.name.replace(' -equ.csv', ' -log.txt')
        family = p.relative_to(source / 'Individual Simulation Results').parts[0]
        parameters = {k: row[k] for k in list(row)[:17] if k != 'Equilibrium Type'}
        cases.append({'Id': 'endogenous-' + hashlib.sha256(name.encode()).hexdigest()[:16],
                      'Family': family, 'OptionSetName': name, 'Parameters': parameters,
                      'SavedProfile': str(p), 'SavedProfileSha256': sha(p),
                      'SourceLog': str(log), 'SourceLogSha256': sha(log),
                      'Historical': row, 'HistoricalGivenDispute': conditional[name]})
    output.mkdir(parents=True)
    (output / 'catalog.json').write_text(json.dumps({'Schema': 'endogenous-population-inputs-v1',
        'ArticleCsv': str(table), 'ArticleCsvSha256': sha(table), 'Cases': cases}, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'Cases': len(cases), 'Catalog': str(output / 'catalog.json')}))


if __name__ == '__main__':
    main()
