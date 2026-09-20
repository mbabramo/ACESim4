"""Verify the routine collection and refresh numbered exhibits without solving games.

Production records and scientific source JSON retain their original provenance.
Inventory paths are resolved relative to their recorded root after relocation.
"""
from pathlib import Path
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import argparse, csv, hashlib, json, math, re, shutil
from datetime import datetime, timezone
from pypdf import PdfReader


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    text = value if isinstance(value, str) else json.dumps(value, indent=2, ensure_ascii=False)
    path.write_text(text.rstrip() + '\n', encoding='utf-8', newline='\n')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inside(root, relative):
    root = Path(root).resolve()
    path = (root / relative).resolve()
    require(path.is_relative_to(root) and path != root, 'Path leaves collection: ' + str(path))
    return path


def expected_counts(matrix):
    names = [case['OptionSetName'] for case in matrix]
    require(len(names) == len(set(names)), 'Duplicate case matrix entry')
    groups = defaultdict(set)
    for case in matrix:
        groups[case['Transformation'], case['Offers'], case['Cost']].add((case['Risk'], case['FeeRule']))
    strategy_count = welfare_count = 0
    for group, members in groups.items():
        risks = {risk for risk, fee in members}
        for risk in risks:
            require({fee for r, fee in members if r == risk} ==
                    {'American', 'Trial Fee-Shifting', 'Complete Fee-Shifting'}, 'Incomplete fee comparison: ' + str(group))
        strategy_count += len(risks)
        welfare_count += len(risks) + (len(risks) > 1)
    return Counter({'individual-results': 6 * len(matrix), 'selection-offers': strategy_count,
                    'welfare-outcomes': welfare_count, 'dispositions': welfare_count})


def csv_rows(path):
    with Path(path).open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def fingerprints(value):
    if isinstance(value,dict):
        if isinstance(value.get('Path'),str) and isinstance(value.get('Sha256'),str):
            yield value
        for child in value.values():
            yield from fingerprints(child)
    elif isinstance(value,list):
        for child in value:
            yield from fingerprints(child)


def compare_reused(results, existing, matrix):
    current = results / 'Run records/Retained study'
    previous = existing / 'Run records/Retained study'
    comparisons = []
    for case in matrix:
        name = case['EquilibriumFileName']
        before, after = previous / name, current / name
        if not before.exists():
            continue
        def probabilities(path):
            with path.open(encoding='utf-8-sig', newline='') as stream:
                return [float(value) for row in csv.reader(stream) for value in row if value.strip()]
        a, b = probabilities(before), probabilities(after)
        require(len(a) == len(b) and all(math.isfinite(x) for x in a+b), 'Changed equilibrium shape: '+name)
        gap = max((abs(x-y) for x,y in zip(a,b)), default=0)
        require(gap <= 1e-10, 'Changed reused strategy: '+name)
        # Compare all reported scientific numeric cells, excluding computation diagnostics.
        report = case['ReportPrefix'] + ' ' + case['OptionSetName'] + '.csv'
        old, new = csv_rows(previous/report), csv_rows(current/report)
        require([r['Filter'] for r in old] == [r['Filter'] for r in new], 'Changed report filters: '+name)
        max_report_gap = 0
        for left,right in zip(old,new):
            for key,value in left.items():
                if key in {'OptionSet','Exploit','Refine','Seconds'} or not value:
                    continue
                try:
                    x,y=float(value),float(right[key])
                except ValueError:
                    require(value == right[key], 'Changed report label: '+name+' '+key)
                    continue
                require(math.isfinite(x) and math.isfinite(y), 'Nonfinite reused report: '+name)
                max_report_gap=max(max_report_gap,abs(x-y))
                require(abs(x-y) <= 1e-5*max(1,abs(x),abs(y)), 'Changed reused report: '+name+' '+key)
        comparisons.append({'OptionSetName':case['OptionSetName'],'PreviousProfileSha256':sha(before),
                            'CurrentProfileSha256':sha(after),'MaximumProbabilityDifference':gap,
                            'MaximumReportedDifference':max_report_gap})
    require(comparisons, 'No existing profiles found for comparison')
    return comparisons


def verify_completed(results):
    """Audit the resumable individual-case import without claiming full matrix coverage."""
    from PIL import Image
    results=Path(results).resolve()
    cases=read(results/'Run records/completed-cases.json')
    require(cases and len({case['OptionSetName'] for case in cases})==len(cases),'Missing or duplicate completed cases')
    files=[file for case in cases for file in case['Outputs']]
    require(len({file['Path'] for file in files})==len(files),'Duplicate imported output')
    def check(file):
        path=inside(results,file['Path'])
        require(sha(path).lower()==file['Sha256'].lower(),'Changed imported file: '+str(path))
        if path.suffix=='.pdf':require(len(PdfReader(path).pages)==1,'Unexpected individual PDF pages: '+str(path))
        elif path.suffix=='.png':
            with Image.open(path) as image:image.verify()
        return path.suffix
    with ThreadPoolExecutor(max_workers=4) as pool:
        counts=Counter(pool.map(check,files))
    require(counts['.pdf']==counts['.png']==6*len(cases),'Incomplete individual renders')
    summary={'VerifiedUtc':datetime.now(timezone.utc).isoformat(),'CoordinatorCompleteCases':len(cases),
             'SourceAndRenderHashesChecked':len(files),'PDFsChecked':counts['.pdf'],'PNGsChecked':counts['.png'],
             'Errors':[],'Scope':'Individual completed cases only; combined comparisons await full routine verification.'}
    write(results/'Run records/incremental-verification.json',summary)
    print(json.dumps(summary,indent=2))
    return summary


def verify(results, matrix, existing=None):
    from PIL import Image
    results=Path(results).resolve()
    expected=expected_counts(matrix)
    inventory=read(results/'Run records/diagram-inventory.json')
    require(inventory['Compiled'] and inventory['Cases']==len(matrix), 'Routine diagram generation is incomplete')
    require(Counter(a['Kind'] for a in inventory['Artifacts'])==expected, 'Unexpected exhibit coverage')
    raw=results/'Run records/Retained study'
    suite=read(raw/'ALER production suite manifest.json')
    require(suite['AllRequiredArtifactsValidated'] and suite['AllRequiredAggregatesValidated'] and
            not suite['WorkingTreeWasDirty'],'Production suite is not certified from clean source')
    plans=[]
    for prefix in sorted({c['ReportPrefix'] for c in matrix}):
        manifest=read(raw/(prefix+' run manifest.json'))
        names={c['OptionSetName'] for c in matrix if c['ReportPrefix']==prefix}
        require(manifest['Status']=='Aggregated' and set(manifest['OptionSetNames'])==names,
                'Production is not fully aggregated: '+prefix)
        require(manifest['GitCommit']==suite['GitCommit'], 'Mixed production commits')
        suite_plan=next(p for p in suite['RequiredPlans'] if p['ReportName']==prefix)
        require(sha(raw/suite_plan['ManifestFile']).lower()==suite_plan['ManifestSha256'].lower(),
                'Production manifest changed since suite verification: '+prefix)
        plans.append({'ReportPrefix':prefix,'Cases':len(names),'Reused':len(manifest['ReusedEquilibria']),
                      'ProductionCommit':manifest['GitCommit']})
    rows=csv_rows(results/'Aggregated Data/Sources/welfare-outcomes.csv')
    require(Counter(r['OptionSetName'] for r in rows)==Counter(c['OptionSetName'] for c in matrix), 'Welfare matrix mismatch')
    records=[]; numeric=0; sources=set(); verified_inputs=set()
    for artifact in inventory['Artifacts']:
        relative=Path(artifact['Source']).relative_to(Path(inventory['Root']))
        source=inside(results,relative)
        require(source.parent.name=='Sources' and source.is_file(), 'Missing TeX: '+str(source))
        require(source not in sources,'Duplicate artifact: '+str(source)); sources.add(source)
        require('node[midway] {\\huge Costs:' not in source.read_text(encoding='utf-8-sig'), 'Embedded cost title: '+str(source))
        record={'Kind':artifact['Kind'],'Source':str(relative),'SourceSha256':sha(source)}
        for extension in ['.pdf','.png']:
            path=source.parent.parent/(source.stem+extension)
            require(path.is_file() and path.stat().st_size>1000,'Missing render: '+str(path))
            record[extension[1:].upper()]={'Path':str(path.relative_to(results)),'Sha256':sha(path)}
        pdf=PdfReader(source.parent.parent/(source.stem+'.pdf'))
        require(len(pdf.pages)==1,'Unexpected routine page count: '+str(source))
        with Image.open(source.parent.parent/(source.stem+'.png')) as image:
            image.verify()
        record['PageSize']=[float(pdf.pages[0].mediabox.width),float(pdf.pages[0].mediabox.height)]
        if artifact['Kind']!='individual-results':
            for fingerprint in fingerprints(read(source.with_suffix('.json'))):
                original=Path(fingerprint['Path'])
                path=inside(results,original.relative_to(Path(inventory['Root'])))
                identity=(str(path),fingerprint['Sha256'].lower())
                if identity not in verified_inputs:
                    require(path.is_file() and sha(path).lower()==identity[1],'Changed exhibit input: '+str(path))
                    verified_inputs.add(identity)
        if artifact['Kind']=='welfare-outcomes':
            table=read(source.with_suffix('.json'))
            cells=[c for panel in table['Panels'] for row in panel['Rows'] for c in row['Cells'] if c['Value'] is not None]
            printed=re.findall(r'(?<![\d.])-?\d+\.\d{3}(?![\d.])',pdf.pages[0].extract_text())
            require(printed==[c['Latex'] for c in cells],'Printed welfare values differ: '+str(source))
            require(all(abs(float(c['Latex'])-c['Value'])<=.0005000001 for c in cells),'Welfare rounding mismatch')
            numeric+=len(cells)
        records.append(record)
    expected_pdf={results/r['PDF']['Path'] for r in records}
    actual_pdf=set((results/'Aggregated Data').rglob('*.pdf'))|set((results/'Individual simulations').rglob('*.pdf'))
    require(expected_pdf==actual_pdf,'Missing or obsolete routine PDFs')
    expected_png={results/r['PNG']['Path'] for r in records}
    actual_png=set((results/'Aggregated Data').rglob('*.png'))|set((results/'Individual simulations').rglob('*.png'))
    require(expected_png==actual_png,'Missing or obsolete routine PNGs')
    comparisons=compare_reused(results,Path(existing).resolve(),matrix) if existing else None
    summary={'VerifiedUtc':datetime.now(timezone.utc).isoformat(),'Cases':len(matrix),'RoutineExhibits':len(records),
             'Kinds':dict(expected),'WelfareNumericCellsVerified':numeric,'Batches':plans,'Errors':[],
             'ExhibitInputFingerprintsVerified':len(verified_inputs),
             'VisualReview':'Recorded separately; this verification checks data, coverage, document integrity and printed welfare values.',
             'SupplementalAnalyses':'Preserved separately; multiple-equilibrium expansion is not certified by this routine verification.'}
    if comparisons is not None:
        write(results/'Run records/reused-equilibrium-comparison.json',comparisons)
        summary['ReusedProfilesCompared']=len(comparisons)
    write(results/'Run records/final-artifact-hashes.json',records)
    write(results/'Run records/final-verification.json',summary)
    print(json.dumps(summary,indent=2))
    return summary


def refresh_main(article):
    article=Path(article).resolve();results=article/'Results'
    from build_manuscript_dispositions import build as build_dispositions
    dispositions=build_dispositions(article)
    manifest_path=article/'manuscript-exhibits.json'
    manifest=read(manifest_path)
    specs=[('Figures','Figure 4 - Participation and offers','Risk Neutral','cost-1-participation-and-offers'),
           ('Figures','Figure 3 - Dispositions','Risk Neutral','cost-1-dispositions'),
           ('Figures','Figure 5 - Risk-averse dispositions','Risk Averse','cost-1-dispositions'),
           ('Figures','Figure 6 - Risk-averse participation and offers','Risk Averse','cost-1-participation-and-offers'),
           ('Tables','Table 4 - Welfare outcomes','Risk Comparison','cost-1-welfare-outcomes')]
    reused=[]
    # Preserve separate analyses and their complete main-exhibit copies, including the selected mechanism table.
    for folder,title,risk,stem in specs:
        source=dispositions[risk] if stem=='cost-1-dispositions' else results/'Aggregated Data/Baseline'/risk/'Sources'/stem
        source_stem=source.name
        previous_tex=article/folder/'Sources'/(title+'.tex')
        prior={Path(e['Output']).suffix:e for e in manifest['Exhibits'] if e['Exhibit']==title}
        # An unchanged, self-contained TeX document may keep its verified renders.
        # This also permits refresh while a reader has an unchanged main PDF open.
        extensions=['.tex','.pdf','.png']
        reusable=all(ext in prior and inside(article,prior[ext]['Output']).is_file() and
                     sha(inside(article,prior[ext]['Output']))==prior[ext]['Sha256'] for ext in extensions)
        reusable=source.is_relative_to(results) and reusable and sha(source.with_suffix('.tex'))==sha(previous_tex)
        reusable=reusable and not re.search(r'\\(?:input|include|includegraphics)\b',source.with_suffix('.tex').read_text(encoding='utf-8-sig'))
        if reusable:
            for extension in ['.pdf','.png']:
                previous=inside(article,prior[extension]['Output'])
                canonical=source.parent.parent/(source_stem+extension)
                shutil.copy2(previous,canonical)
                reused.append({'Exhibit':title,'Path':str(canonical.relative_to(results)),
                               'Sha256':sha(canonical),'TexSha256':sha(previous_tex),
                               'Basis':'Unchanged standalone TeX and previously recorded source/render hashes.'})
        canonical_caption=source.with_suffix('.txt')
        previous_caption=article/folder/'Sources'/(title+'.txt')
        if stem == 'cost-1-participation-and-offers':
            preference='risk neutrality' if risk=='Risk Neutral' else 'symmetric CARA risk aversion with coefficient 2'
            caption=read(source.with_suffix('.json'))['Caption']+f' All three core rules are shown under {preference}, cost multiplier 1, standard noise 0.20 and the ten-offer grid.'
        elif stem == 'cost-1-dispositions':
            caption=read(source.with_suffix('.json'))['Caption']
        elif stem == 'cost-1-welfare-outcomes':
            caption='Welfare outcomes under risk neutrality and symmetric CARA risk aversion with coefficient 2, per potential dispute at cost multiplier 1, standard noise 0.20 and ten offers. The three net-burden measures include legal costs and fee transfers. Gross outcome error measures the difference between the base payment and the payment warranted by true liability, before costs and separate fee transfers. Real expenditures exclude transfers. All measures include unfiled disputes. The five measures are distinct and should not be added together. Values are rounded to three decimals.'
        else:
            require(previous_caption.is_file(),'Missing manuscript caption: '+str(previous_caption))
            caption=previous_caption.read_text(encoding='utf-8-sig')
        write(canonical_caption,caption)
        manifest['Exhibits']=[e for e in manifest['Exhibits'] if e['Exhibit']!=title]
        for extension in ['.pdf','.png','.tex','.json','.txt']:
            origin=(source.parent.parent/(source_stem+extension) if extension in {'.pdf','.png'} else source.with_suffix(extension))
            destination=article/folder/(title+extension) if extension in {'.pdf','.png'} else article/folder/'Sources'/(title+extension)
            destination.parent.mkdir(parents=True,exist_ok=True)
            if not destination.exists() or sha(origin)!=sha(destination):shutil.copy2(origin,destination)
            manifest['Exhibits'].append({'Exhibit':title,'Source':str(origin.relative_to(article)),
                                         'Output':str(destination.relative_to(article)),'Sha256':sha(destination)})
    # The model-primitives table is unchanged, but its references now identify the expanded datasets.
    primitives=article/'Supplemental materials/Game tree diagrams/Sources/model-primitives.json'
    if primitives.exists():
        data=read(primitives)
        for record in data.get('Sources',[]):
            path=inside(article,record['Path']);record['Sha256']=sha(path)
        write(primitives,data)
        for record in manifest['Exhibits']:
            if record['Exhibit']=='Table 1 - Model primitives' and record['Output'].endswith('.json'):
                shutil.copy2(primitives,inside(article,record['Output']))
                record['Sha256']=sha(primitives)
    write(manifest_path,manifest)
    for record in manifest['Exhibits']:
        destination=inside(article,record['Output'])
        require(sha(destination)==record['Sha256'],'Changed numbered exhibit: '+str(destination))
        origin=article/record['Source']
        if origin.is_file():require(sha(origin)==sha(destination),'Numbered/canonical mismatch: '+str(destination))
    for folder,count in [('Figures',6),('Tables',4)]:
        require(len(list((article/folder).glob('*.pdf')))==count,'Unexpected main-exhibit count')
    if reused:
        records_path=results/'Run records/final-artifact-hashes.json'
        records=read(records_path)
        for reuse in reused:
            kind=Path(reuse['Path']).suffix[1:].upper()
            matches=[record for record in records if Path(record[kind]['Path'])==Path(reuse['Path'])]
            require(len(matches)==1,'Reused main render missing from inventory')
            matches[0][kind]['Sha256']=reuse['Sha256']
        write(records_path,records)
        write(results/'Run records/main-render-reuse.json',reused)
    print('Refreshed Figures 3-6 and the combined welfare Table 4; preserved strategy Tables 2/3 and the model exhibits.')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('command',choices=['verify','verify-completed','refresh-main'])
    parser.add_argument('--results',type=Path)
    parser.add_argument('--matrix',type=Path)
    parser.add_argument('--existing',type=Path)
    parser.add_argument('--article',type=Path)
    args=parser.parse_args()
    if args.command=='verify':
        require(args.results is not None and args.matrix is not None,'Supply --results and --matrix')
        verify(args.results,read(args.matrix),args.existing)
    elif args.command=='verify-completed':
        require(args.results is not None,'Supply --results')
        verify_completed(args.results)
    else:
        require(args.article is not None,'Supply --article')
        refresh_main(args.article)
