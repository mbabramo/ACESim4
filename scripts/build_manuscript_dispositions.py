"""Prepare single-risk manuscript figures from saved disposition charts.

Numerical inputs and routine charts are preserved. The only presentation change
is removal of the redundant risk heading, which belongs in the manuscript caption.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tempfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build(article):
    article = Path(article).resolve()
    root = article / 'Supplemental materials/Outcome summaries'
    sources = root / 'Sources'
    sources.mkdir(parents=True, exist_ok=True)
    outputs = {}
    for risk, stem in [('Risk Neutral', 'risk-neutral-dispositions'), ('Risk Averse', 'risk-averse-dispositions')]:
        origin = article / 'Results/Aggregated Data/Baseline' / risk / 'Sources/cost-1-dispositions'
        json_path, tex_path = origin.with_suffix('.json'), origin.with_suffix('.tex')
        data = json.loads(json_path.read_text(encoding='utf-8-sig'))
        assert {b['Group'].lower() for b in data['Data']['Bars']} == {risk.lower()}
        tex = tex_path.read_text(encoding='utf-8-sig')
        # Match the generated group-heading node only, never the data bars or labels.
        tex, count = re.subn(r'^\\node\[anchor=west,font=\\bfseries\].*\{\\mbox\{Risk\} \\mbox\{(?:neutral|averse)\}\};\n', '', tex, flags=re.M)
        assert count == 1, 'Expected exactly one redundant risk heading'
        # End vertical grid lines just above the first bar after removing the heading.
        first_bar = re.search(r'\\node\[anchor=east\] at \(4\.95,([0-9.]+)\)', tex)
        assert first_bar
        top = f'{float(first_bar[1]) + .4:.6f}'.rstrip('0').rstrip('.')
        tex, count = re.subn(r'(\\draw\[black!15,line width=\.2pt\] \([0-9.]+,\.65\) -- \([0-9.]+,)[0-9.]+(\);)', lambda m: m[1] + top + m[2], tex)
        assert count == 6
        caption = (f'Dispositions with {risk.lower()} under the three fee rules at ordinary costs, '
                   'noise 0.20 and ten offers. Each bar includes all potential disputes, including unfiled cases. '
                   'Mutual exit is allocated equally to abandonment and default. Trial outcomes denote court findings, '
                   'not true liability. Results describe selected equilibria.')
        if risk == 'Risk Averse':
            caption += ' Both parties have CARA risk aversion with coefficient 2.'
        data['Caption'] = caption
        data['ManuscriptPresentation'] = {
            'RiskHeadingShown': False,
            'Inputs': [{'Path': str(p.relative_to(article)), 'Sha256': sha(p)} for p in [json_path, tex_path]],
            'Generator': {'Path': str(Path(__file__).resolve()), 'Sha256': sha(Path(__file__))}}
        target = sources / stem
        target.with_suffix('.tex').write_text(tex, encoding='utf-8', newline='\n')
        target.with_suffix('.json').write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8', newline='\n')
        target.with_suffix('.txt').write_text(caption + '\n', encoding='utf-8', newline='\n')
        with tempfile.TemporaryDirectory(prefix='acesim-manuscript-dispositions-') as scratch:
            scratch = Path(scratch)
            result = subprocess.run(['pdflatex', '-interaction=nonstopmode', '-halt-on-error',
                                     '-output-directory=' + str(scratch), str(target.with_suffix('.tex'))],
                                    capture_output=True, text=True)
            if result.returncode or 'Overfull' in result.stdout:
                raise RuntimeError(result.stdout[-6000:] + result.stderr)
            subprocess.run(['pdftoppm', '-png', '-singlefile', '-r', '150', str(scratch / (stem + '.pdf')),
                            str(scratch / stem)], check=True, capture_output=True)
            for extension in ['.pdf', '.png']:
                shutil.copyfile(scratch / (stem + extension), root / (stem + extension))
        outputs[risk] = target
    return outputs


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--article', required=True)
    print(build(parser.parse_args().article))
