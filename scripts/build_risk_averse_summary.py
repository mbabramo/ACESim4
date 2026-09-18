"""Build the compact manuscript table from saved risk-averse outcome exhibits."""
from pathlib import Path
import argparse
import hashlib
import json
import math
import shutil
import subprocess
import tempfile

STEM = 'risk-averse-outcomes'
CAPTION = ('Risk-averse outcomes under the three fee rules at ordinary costs, with symmetric CARA coefficient 2, '
           'noise 0.20 and ten offers. Dispositions are percentages of all potential disputes, including unfiled cases; '
           'mutual exit is allocated equally to abandonment and default. Welfare measures are amounts per potential '
           'dispute, in units of damages, rounded to three decimals. The five welfare measures are distinct and should '
           'not be added together. Results describe selected equilibria; outcome variation across recovered equilibria '
           'is reported separately.')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build(article):
    article = Path(article).resolve()
    inputs = article / 'Results/Aggregated Data/Baseline/Risk Averse/Sources'
    disposition_path = inputs / 'cost-1-dispositions.json'
    welfare_path = inputs / 'cost-1-welfare-outcomes.json'
    dispositions = read(disposition_path)['Data']
    welfare = read(welfare_path)
    rules = ['American', 'Trial Fee-Shifting', 'Complete Fee-Shifting']
    bars = dispositions['Bars']
    assert len(bars) == 3 and [b['Regime'] for b in bars] == rules
    assert all(b['Group'] == 'Risk averse' and math.isclose(sum(b['Values']), 1, abs_tol=1e-6) for b in bars)
    assert len(welfare['Panels']) == 1 and welfare['Panels'][0]['Title'] == 'Risk averse'
    welfare_rows = welfare['Panels'][0]['Rows']
    assert [r['Cells'][0]['Latex'] for r in welfare_rows] == rules
    rows = []
    for index, label in enumerate(dispositions['Categories']):
        rows.append({'Panel': 'Dispositions (percent of potential disputes)', 'Label': label,
                     'Values': [b['Values'][index] for b in bars], 'Format': 'percent-one-decimal'})
    labels = ['Meritorious-P shortfall', 'Nonliable-D burden', 'Liable-D excess burden',
              'Gross outcome error', 'Real expenditures']
    for index, label in enumerate(labels, 1):
        rows.append({'Panel': 'Welfare (amount per potential dispute)', 'Label': label,
                     'Values': [r['Cells'][index]['Value'] for r in welfare_rows], 'Format': 'amount-three-decimals'})
    root = article / 'Supplemental materials/Outcome summaries'
    sources = root / 'Sources'
    sources.mkdir(parents=True, exist_ok=True)
    tex = [r'\documentclass[10pt,border=5pt,varwidth=6.4in]{standalone}',
           r'\usepackage[T1]{fontenc}', r'\usepackage{lmodern,booktabs,tabularx,array}',
           r'\begin{document}\begin{minipage}{6.4in}',
           r'\setlength{\tabcolsep}{5pt}\renewcommand{\arraystretch}{1.12}',
           r'\begin{tabularx}{\linewidth}{@{}>{\raggedright\arraybackslash}X*{3}{>{\centering\arraybackslash}p{1.0in}}@{}}',
           r'\toprule',
           r'Outcome & American & \shortstack{Trial Fee-\\Shifting} & \shortstack{Complete Fee-\\Shifting} \\']
    last_panel = None
    for row in rows:
        if row['Panel'] != last_panel:
            tex += [r'\midrule', r'\multicolumn{4}{@{}l}{\textit{' + row['Panel'] + r'}}\\[2pt]']
            last_panel = row['Panel']
        values = [f'{100*v:.1f}\\%' if row['Format'].startswith('percent') else f'{v:.3f}' for v in row['Values']]
        tex.append(row['Label'] + ' & ' + ' & '.join(values) + r' \\')
    tex.append(r'\bottomrule\end{tabularx}\end{minipage}\end{document}')
    tex_path = sources / (STEM + '.tex')
    tex_path.write_bytes(('\n'.join(tex) + '\n').encode())
    source_data = {'Schema': '1', 'Title': 'Risk-averse outcomes', 'Caption': CAPTION,
                   'Cost': 1, 'CARAAlpha': 2, 'Rules': rules, 'Rows': rows,
                   'Inputs': [{'Path': str(p.relative_to(article)), 'Sha256': sha(p)} for p in [disposition_path, welfare_path]],
                   'Generator': {'Path': str(Path(__file__).resolve()), 'Sha256': sha(Path(__file__))}}
    (sources / (STEM + '.json')).write_bytes((json.dumps(source_data, indent=2) + '\n').encode())
    (sources / (STEM + '.txt')).write_bytes((CAPTION + '\n').encode())
    # Only this invocation's temporary compiler directory is cleaned up.
    with tempfile.TemporaryDirectory(prefix='acesim-ra-summary-') as scratch:
        scratch = Path(scratch)
        result = subprocess.run(['pdflatex', '-interaction=nonstopmode', '-halt-on-error',
                                 '-output-directory=' + str(scratch), str(tex_path)], capture_output=True, text=True)
        if result.returncode or 'Overfull' in result.stdout:
            raise RuntimeError(result.stdout[-6000:] + result.stderr)
        pdf = scratch / (STEM + '.pdf')
        subprocess.run(['pdftoppm', '-png', '-singlefile', '-r', '150', str(pdf), str(scratch / STEM)], check=True,
                       capture_output=True)
        for extension in ['.pdf', '.png']:
            shutil.copyfile(scratch / (STEM + extension), root / (STEM + extension))
    return sources / STEM


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--article', required=True)
    print(build(parser.parse_args().article))
