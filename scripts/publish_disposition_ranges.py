"""Publish Table 6 from the Risk Comparison disposition ranges, at three decimals.

python -B scripts/publish_disposition_ranges.py --article-directory PATH
Requires pdflatex and pdftoppm. Reads saved results; does not solve games.
"""
import argparse
from decimal import Decimal, ROUND_HALF_EVEN
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


TITLE = 'Table 6 - Disposition ranges'
STEM = 'cost-1-disposition-ranges'
METRICS = ('Does Not File', 'Does Not Answer', 'Settles',
           'P Abandons (Mutual Give-Up Allocated)', 'D Defaults (Mutual Give-Up Allocated)',
           'P Loses', 'P Wins')
RULES = ('American', 'Trial Fee-Shifting', 'Complete Fee-Shifting')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def number(value):
    result = Decimal(value)
    if not result.is_finite() or not 0 <= result <= 1:
        raise ValueError('Invalid disposition probability: ' + value)
    return result


def formatted(value, places):
    return format(value.quantize(Decimal(1).scaleb(-places), rounding=ROUND_HALF_EVEN), f'.{places}f')


def manuscript_tex(template, rows):
    indexed = {}
    for row in rows:
        risk = {'0': 'Risk Neutral', '2': 'Risk Averse'}[row['CARA Alpha']]
        key = risk, row['Fee Rule']
        if key in indexed:
            raise ValueError('Duplicate disposition-range row: ' + str(key))
        indexed[key] = row
    expected = {(risk, rule) for risk in ('Risk Neutral', 'Risk Averse') for rule in RULES}
    if set(indexed) != expected:
        raise ValueError('Incomplete risk/fee-rule coverage')
    lines, seen = [], set()
    for line in template.splitlines():
        parts = [part.strip() for part in line.split('&')]
        key = tuple(parts[:2])
        if key in indexed:
            if key in seen or len(parts) != 9 or not line.endswith(r'\\'):
                raise ValueError('Unexpected disposition table layout')
            seen.add(key)
            row, cells = indexed[key], []
            for metric, previous in zip(METRICS, parts[2:]):
                low, high = [number(row[prefix + metric]) for prefix in ('Minimum ', 'Maximum ')]
                if low > high:
                    raise ValueError('Reversed disposition range: ' + metric)
                original = re.fullmatch(r'(\d+\.\d+)--(\d+\.\d+)(?:\s*\\\\)?', previous)
                if not original or any(formatted(value, len(text.split('.')[1])) != text
                                       for value, text in zip((low, high), original.groups())):
                    raise ValueError('Source TeX/data mismatch: ' + str(key) + ' ' + metric)
                cells.append(formatted(low, 3) + '--' + formatted(high, 3))
            line = ' & '.join(parts[:2] + cells) + r' \\'
        lines.append(line)
    if seen != expected:
        raise ValueError('Missing disposition rows in source TeX')
    return '\n'.join(lines) + '\n'


def run(command, work):
    result = subprocess.run(command, cwd=work, capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stdout[-5000:] + result.stderr[-2000:])


def publish(article):
    article = article.resolve()
    origin = article / 'Supplemental materials/Multiple equilibria/Risk Comparison/Sources' / STEM
    data_path = origin.with_suffix('.json')
    inputs = [origin.with_suffix(ext) for ext in ('.json', '.tex', '.txt')]
    data = json.loads(data_path.read_text(encoding='utf-8-sig'))
    tex = manuscript_tex(origin.with_suffix('.tex').read_text(encoding='utf-8-sig'), data['Data'])
    data['ManuscriptPresentation'] = {
        'Title': TITLE, 'DecimalPlaces': 3, 'Rounding': 'nearest, ties to even; from full-precision data',
        'Inputs': [{'Path': str(p.relative_to(article)), 'Sha256': sha(p)} for p in inputs],
        'Generator': {'Path': str(Path(__file__).resolve()), 'Sha256': sha(Path(__file__))}}
    table = article / 'Tables'
    sources = table / 'Sources'
    manifest_path = article / 'manuscript-exhibits.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    prior = {Path(e['Output']).suffix: e for e in manifest['Exhibits'] if e['Exhibit'] == TITLE}
    previous_tex = sources / (TITLE + '.tex')
    reuse = (previous_tex.is_file() and previous_tex.read_text(encoding='utf-8-sig') == tex
             and all(ext in prior and Path(prior[ext]['Output']) == path.relative_to(article)
                     and path.is_file() and sha(path) == prior[ext]['Sha256']
                     for ext, path in [('.tex', previous_tex), ('.pdf', table / (TITLE + '.pdf')),
                                       ('.png', table / (TITLE + '.png'))]))
    with tempfile.TemporaryDirectory(prefix='acesim-disposition-ranges-') as directory:
        work = Path(directory)
        (work / (STEM + '.tex')).write_text(tex, encoding='utf-8', newline='\n')
        (work / (STEM + '.json')).write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
        (work / (STEM + '.txt')).write_text(data['Caption'] + ' Displayed endpoints are rounded to three decimal places.\n', encoding='utf-8')
        if not reuse:
            run(['pdflatex', '-interaction=nonstopmode', '-halt-on-error', STEM + '.tex'], work)
            log = (work / (STEM + '.log')).read_text(encoding='utf-8', errors='replace')
            if 'Overfull' in log or 'Missing character' in log:
                raise ValueError('LaTeX layout/font warning; inspect the table')
            run(['pdftoppm', '-singlefile', '-r', '180', '-png', STEM + '.pdf', STEM], work)
        sources.mkdir(parents=True, exist_ok=True)
        entries = []
        for ext in ('.pdf', '.png', '.tex', '.json', '.txt'):
            destination = (table if ext in ('.pdf', '.png') else sources) / (TITLE + ext)
            if not (reuse and ext in ('.pdf', '.png')):
                shutil.copy2(work / (STEM + ext), destination)
            entries.append({'Exhibit': TITLE, 'Source': str(data_path.relative_to(article)),
                            'Derived': True, 'SourceSha256': sha(data_path),
                            'Output': str(destination.relative_to(article)), 'Sha256': sha(destination)})
    manifest['Exhibits'] = [e for e in manifest['Exhibits'] if e['Exhibit'] != TITLE] + entries
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return {'pdf': str(table / (TITLE + '.pdf')), 'rows': len(data['Data']),
            'range_cells': len(data['Data']) * len(METRICS), 'decimal_places': 3}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--article-directory', required=True, type=Path)
    print(json.dumps(publish(parser.parse_args().article_directory), indent=2))
