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

def verify(output,changes_only=False):
    plan=read(output/'supplemental-plan.json');cases=plan['Cases']
    pairs={(a['id'],b['id']) for a in cases for b in cases if a['cost']==b['cost'] and
           ((a['fee']!=b['fee']) != (a['alpha']!=b['alpha']))}
    assert len(pairs)==plan['DirectedContrasts']
    changes=output/'Equilibrium strategy changes'; counts=Counter();selected=[]; checks=0
    for rep in ['Original','Mixed','Mixed tighter check']:
        seen=set()
        for p in (changes/'Calculations'/rep).glob('cost-*/*/equilibrium-changes-manifest.json'):
            manifest=read(p);assert manifest['Schema']=='2'
            for f in fingerprints(manifest):check(f);checks+=1
            for name in manifest['OutputJsonFiles']:
                result=read(name);contrast=result['Contrast'];seen.add((contrast['Source'],contrast['Target']))
                for row in result['Changes']:
                    counts[rep+' full coordinates']+=1
                    if not row['CounterfactualUndefined']:
                        a=row['Allocation'];close(a['Change'],a['Direct']+a['Entry']+a['Offers']+a['Exit']+a['SelectionResidual'])
        assert seen==pairs,(rep,len(seen),len(pairs))
    for rep in ['Mixed','Mixed tighter check']:
        for case in cases:
            directory=changes/'Calculations/Mixing'/rep/case['id'];mix=read(directory/(case['id']+'.json'))
            for f in fingerprints(mix):check(f);checks+=1
            assert mix['ValidatedRows']>0
            chosen=next(r for r in mix['Runs'] if r['Order']==mix['SelectedOrder'])
            assert all(math.isfinite(v) and v<=mix['Settings']['ValidationTolerance'] for v in chosen['FinalGains'])
            counts['Mixing checks']+=1
    numeric=0;empty=[]
    sources=changes/'Tables/Sources'
    assert (changes/'Methodology and Explanation.md').is_file()
    assert not list(sources.glob('*.txt')), 'Per-table explanations belong in the shared methodology'
    publication_pairs=set()
    for p in sources.glob('*-cost-*.json'):
        j=read(p);publication_pairs.add((j['Contrast']['Source'],j['Contrast']['Target']))
        assert (p.parent/j['Methodology']).resolve()==(changes/'Methodology and Explanation.md').resolve()
        for f in j['Inputs']:check(f);checks+=1
        pdf=p.parent.parent/(p.stem+'.pdf');png=pdf.with_suffix('.png')
        assert png.is_file() and png.stat().st_size>1000,str(png)
        document=PdfReader(pdf);assert len(document.pages)==1,str(pdf)
        tex=p.with_suffix('.tex').read_text(encoding='utf-8-sig')
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
        selected.extend(j['SelectedRows']);counts['Publication tables']+=1
    assert publication_pairs==pairs
    report={'CoreProfiles':len(cases),'DirectedContrasts':len(pairs),'Counts':dict(counts),
        'PublicationNumericEntriesVerified':numeric,'SelectedCoordinates':len(selected),
        'SelectedResidualCoordinates':sum(abs(r['Allocation']['SelectionResidual'])>1e-6 for r in selected),
        'SelectedSensitiveCoordinates':sum(r['TieSensitive'] or r['CompletionSensitive'] for r in selected),
        'EmptySelections':empty,'FingerprintsVerified':checks,'Scope':'strategy comparisons only' if changes_only else 'complete supplemental expansion'}
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
