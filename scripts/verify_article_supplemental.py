"""Verify saved supplemental calculations and rendered tables without re-solving.

Requires pypdf. --changes-only supports auditing the completed comparison stage
while independent exact replays and multiple-start production remain active.
"""
from pathlib import Path
from functools import lru_cache
from collections import Counter
import argparse,hashlib,json,math,re
from pypdf import PdfReader

def read(p):return json.loads(Path(p).read_text(encoding='utf-8-sig'))
@lru_cache(None)
def sha(p):
    with Path(p).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def fingerprints(j):
    if isinstance(j,dict):
        if 'Path' in j and 'Sha256' in j:yield j
        for v in j.values():yield from fingerprints(v)
    elif isinstance(j,list):
        for v in j:yield from fingerprints(v)
def check(f):
    assert Path(f['Path']).is_file(),f['Path']
    assert sha(f['Path']).lower()==f['Sha256'].lower(),f['Path']
def close(a,b):assert math.isfinite(a) and math.isfinite(b) and abs(a-b)<=1e-6,(a,b)

def verify_manuscript(changes):
    stem='manuscript-strategy-mechanisms'
    packet=read(changes/'Sources/Json'/(stem+'.json'))
    config=read(Path(__file__).resolve().parents[1]/'LitigCharts/ArticleStrategyComparisons.json')
    assert [p['Comparison'] for p in packet['Panels']]==config
    counts=Counter()
    for panel in packet['Panels']:
        for fingerprint in panel['Inputs']:check(fingerprint)
        individual=read(changes/'Sources/Json'/(panel['Contrast']['Id']+'.json'))
        assert panel['SelectedRows']==individual['SelectedRows'], 'Manuscript must retain every qualifying coordinate'
        counts['Coordinates']+=len(panel['SelectedRows'])
        counts['DisplayedRows']+=panel['DisplayedRows']
    document=PdfReader(changes/'Tables'/(stem+'.pdf'))
    tex=(changes/'Sources/Tex'/(stem+'.tex')).read_text(encoding='utf-8-sig')
    rows='\n'.join(line for line in tex.splitlines() if re.match(r'^[PD] ',line))
    printed='\n'.join(p.extract_text() for p in document.pages)
    def signed(s):
        s=s.replace('\u2212','-').replace('\u2013',' ').replace('--',' ')
        s=re.sub(r'(?<=\d)\s*\.\s*(?=\d)', '.', s)
        return [float(re.sub(r'\s','',v)) for v in re.findall(r'[+-]?\s*\d+(?:\.\d+)?',s)]
    assert signed(printed)==signed(rows), 'Manuscript printed values differ from full TeX rows'
    for n in range(1,len(document.pages)+1):
        png=changes/'Tables'/(stem+('' if n==1 else f'-page-{n:02}')+'.png')
        assert png.is_file() and png.stat().st_size>1000
    summary={'Panels':len(packet['Panels']),'Pages':len(document.pages),**dict(counts)}
    selections=[('selected-strategy-mechanisms',3,7,
                 ['american-to-trial-risk-neutral','trial-to-complete-risk-neutral']),
                ('selected-risk-averse-strategy-mechanisms',11,15,
                 ['risk-neutral-to-risk-averse-american','risk-neutral-to-risk-averse-trial',
                  'risk-neutral-to-risk-averse-complete','trial-to-complete-risk-averse'])]
    for selected_stem,displayed,coordinates,comparison_ids in selections:
        selected_path=changes/'Sources/Json'/(selected_stem+'.json')
        assert selected_path.is_file(), 'Missing manuscript selection: '+selected_stem
        selected=read(selected_path)
        check(selected['FullAnalysis'])
        assert [p['Comparison']['Id'] for p in selected['Panels']]==comparison_ids
        for panel in selected['Panels']:
            original=next(p for p in packet['Panels'] if p['Comparison']==panel['Comparison'])
            assert all(row in original['SelectedRows'] for row in panel['SelectedRows'])
            contrast=original['Contrast']['Id']
            calculated=read(changes/'Data/cost-1'/contrast/(contrast+'.json'))
            endpoints=[{i['Key']:i for i in calculated[name]['InformationSets']}
                       for name in ['SourceEquilibrium','TargetEquilibrium']]
            for row in panel['SelectedRows']:
                assert not row['CounterfactualUndefined']
                assert all(not endpoint[row['Key']]['ActualOffPath'] and
                           endpoint[row['Key']]['ActualReach']>0 for endpoint in endpoints)
                if selected_stem=='selected-strategy-mechanisms':
                    assert not row['UnreachedCoalitions']
                if 'Opponent exit' in selected['OmittedZeroColumns']:close(row['Allocation']['Exit'],0)
                close(row['Allocation']['SelectionResidual'],0)
        assert sum(p['DisplayedRows'] for p in selected['Panels'])==displayed
        assert sum(len(p['SelectedRows']) for p in selected['Panels'])==coordinates
        selected_tex=(changes/'Sources/Tex'/(selected_stem+'.tex')).read_text(encoding='utf-8-sig')
        assert 'Sensitive' not in selected_tex and '^{*}' not in selected_tex
        selected_pdf=PdfReader(changes/'Tables'/(selected_stem+'.pdf'))
        assert len(selected_pdf.pages)==1
        rows='\n'.join(line for line in selected_tex.splitlines() if re.match(r'^[PD] ',line))
        assert signed(selected_pdf.pages[0].extract_text())==signed(rows)
        summary[selected_stem]={'DisplayedRows':displayed,'Coordinates':coordinates,'Pages':1}
    return summary

def verify(output,changes_only=False):
    plan=read(output/'supplemental-plan.json');cases=plan['Cases']
    pairs={(a['id'],b['id']) for a in cases for b in cases if a['cost']==b['cost'] and
           ((a['fee']!=b['fee']) != (a['alpha']!=b['alpha']))}
    assert len(pairs)==plan['DirectedContrasts']
    changes=output/'Equilibrium strategy changes'; counts=Counter();selected=[]; checks=0
    assert not (changes/'Calculations').exists() and not (changes/'Tables/Sources').exists()
    assert {p.name for p in (changes/'Data').iterdir() if p.is_dir()}=={'cost-'+c['cost'] for c in cases}
    seen=set()
    for p in (changes/'Data').glob('cost-*/*/equilibrium-changes-manifest.json'):
        manifest=read(p);assert manifest['Schema']=='2'
        for f in fingerprints(manifest):check(f);checks+=1
        request=read(p.parent/'equilibrium-changes.request.json')
        assert request['CheckOffPathCompletions'] and request['CheckTieSensitivity']
        assert all(s.get('ProfileFile') is None for s in request['Sources'])
        for name in manifest['OutputJsonFiles']:
            result=read(name);contrast=result['Contrast'];seen.add((contrast['Source'],contrast['Target']))
            for row in result['Changes']:
                counts['Full coordinates']+=1
                if not row['CounterfactualUndefined']:
                    a=row['Allocation'];close(a['Change'],a['Direct']+a['Entry']+a['Offers']+a['Exit']+a['SelectionResidual'])
    assert seen==pairs,(len(seen),len(pairs))
    numeric=0;empty=[]
    sources=changes/'Sources/Json';tex_sources=changes/'Sources/Tex'
    assert (changes/'Methodology and Explanation.md').is_file()
    assert not list(sources.glob('*.txt')) and not list(tex_sources.glob('*.txt')), 'Per-table explanations belong in the shared methodology'
    publication_pairs=set()
    for p in sources.glob('*-cost-*.json'):
        j=read(p);publication_pairs.add((j['Contrast']['Source'],j['Contrast']['Target']))
        assert len(j['Inputs'])==1 and 'saved equilibria' in j['Selection']
        assert (p.parent/j['Methodology']).resolve()==(changes/'Methodology and Explanation.md').resolve()
        for f in j['Inputs']:check(f);checks+=1
        pdf=changes/'Tables'/(p.stem+'.pdf');png=pdf.with_suffix('.png')
        assert png.is_file() and png.stat().st_size>1000,str(png)
        document=PdfReader(pdf);assert len(document.pages)==1,str(pdf)
        tex=(tex_sources/(p.stem+'.tex')).read_text(encoding='utf-8-sig')
        body='\n'.join(line for line in tex.splitlines() if re.match(r'^[PD] ',line))
        # Compare printed magnitudes and signs in reading order, including signal ranges.
        numbers=lambda s:re.findall(r'\d+(?:\.\d+)?',re.sub(r'(?<=\d)\s*\.\s*(?=\d)', '.', s))
        printed=document.pages[0].extract_text()
        actual=numbers(printed)
        expected=numbers(body)
        assert actual==expected,(str(pdf),actual,expected)
        def signed(s):
            s=s.replace('\u2212','-').replace('\u2013',' ').replace('--',' ')
            s=re.sub(r'(?<=\d)\s*\.\s*(?=\d)', '.', s)
            return [float(re.sub(r'\s','',v)) for v in re.findall(r'[+-]?\s*\d+(?:\.\d+)?',s)]
        assert signed(printed)==signed(body),('Printed signs',str(pdf))
        numeric+=len(expected)
        if not j['SelectedRows']:empty.append(j['Contrast']['Id'])
        selected.extend(j['SelectedRows']);counts['Tables']+=1
        counts['Displayed rows']+=len(body.splitlines()) if body else 0
    assert publication_pairs==pairs
    report={'CoreProfiles':len(cases),'DirectedContrasts':len(pairs),'Counts':dict(counts),
        'PublicationNumericEntriesVerified':numeric,'SelectedCoordinates':len(selected),
        'SelectedResidualCoordinates':sum(abs(r['Allocation']['SelectionResidual'])>1e-6 for r in selected),
        'SelectedSensitiveCoordinates':sum(r['TieSensitive'] or r['CompletionSensitive'] for r in selected),
        'EmptySelections':empty,'FingerprintsVerified':checks,'Scope':'strategy comparisons only' if changes_only else 'complete supplemental expansion'}
    if any(j['id']=='table-manuscript' for j in plan.get('Jobs',[])) or (sources/'manuscript-strategy-mechanisms.json').exists():
        report['ManuscriptTable']=verify_manuscript(changes)
    if not changes_only:
        paths=output/'Equilibrium solution paths'
        collection=read(paths/'equilibrium-paths-collection-manifest.json')
        for f in fingerprints(collection):check(f)
        path_results=[]
        for c in cases:
            if c['cost']!='1':continue
            p=paths/'Sources/Traces'/c['id']/(c['id']+'.json');j=read(p)
            for f in fingerprints(j):check(f)
            assert j['Exact'] and j['Seed']==0 and j['Pivots']==j['OriginalPivots'] and j['Steps']==j['Pivots']+1
            assert j['MaximumSavedPolicyDifference']<=1e-10 and j['FinalEpsilon']<=1e-7
            assert (paths/(c['id']+'.html')).is_file()
            path_results.append({k:j[k] for k in ['Id','Pivots','Steps','MaximumSavedPolicyDifference','FinalEpsilon']})
        report['Paths']=path_results
        me=read(output/'Multiple equilibria/multiple-equilibria-exhibits.json')
        assert me['Compiled'] and me['Validation']['OptionSetCount']==len(path_results)
        for f in fingerprints(me):check(f)
        report['MultipleEquilibria']=me['Validation']
        report['MultipleEquilibriaExhibits']=len(me['Artifacts'])
    destination=changes/'Sources'/('comparison-verification.json' if changes_only else 'supplemental-verification.json')
    destination.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(report,indent=2))
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--changes-only',action='store_true')
    args=parser.parse_args();verify(args.output.resolve(),args.changes_only)
