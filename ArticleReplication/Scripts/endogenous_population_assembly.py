"""Assemble independently audited pooled receipts without rerunning numerical work."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def write_new(path, value):
    with Path(path).open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2, allow_nan=False)


def checked_proofs(audits, expected_ids):
    proofs, runs, initial = {}, [], None
    for folder in map(Path, audits):
        complete = read(folder / 'completed.json')
        require(complete['Passed'], 'An audit did not pass')
        require(sha(folder / 'request.json') == complete['RequestSha256'], 'Audit request changed')
        require(sha(folder / 'initial-context.json') == complete['InitialContextSha256'], 'Initial validation context changed')
        require(sha(folder / 'policy-blobs.jsonl') == complete['PolicyLedgerSha256'], 'Policy verification ledger changed')
        context = read(folder / 'initial-context.json')
        if initial is None:
            initial = context
        require(context == initial, 'Audits used different initial evidence')
        require(complete['Cases'] == len(complete['Proofs']) and complete['PooledResults'] == 2 * complete['Cases'], 'Incorrect audit coverage')
        requested = {x['CaseId']: x for x in read(folder / 'request.json')['Cases']}
        require(len(requested) == complete['Cases'], 'Incorrect audit request coverage')
        for entry in complete['Proofs']:
            cid = entry['CaseId']
            require(cid in requested and cid in expected_ids and cid not in proofs, 'Duplicate or unexpected audited case')
            require(sha(entry['Proof']) == entry['Sha256'], 'Case validation proof changed')
            proof = read(entry['Proof'])
            require(proof['Passed'] and proof['CaseId'] == cid, 'Case proof did not pass')
            require(proof['Receipt'] == requested[cid]['Receipt'] and proof['ReceiptSha256'] == requested[cid]['ReceiptSha256'], 'Proof used a different receipt')
            require(sha(proof['Receipt']) == proof['ReceiptSha256'], 'Final receipt changed')
            for path, digest in proof['FileHashes'].items():
                require(sha(path) == digest, 'Validated result, archive or audit file changed: ' + path)
            for source in proof['SourceArchives']:
                require(sha(source['Path']) == source['Sha256'], 'Validated source archive changed')
            require({r['Selector'] for r in proof['Results']} == {'gini', 'liu'} and len(proof['Results']) == 2, 'Incomplete selector proof')
            require(all(r['IndependentInitialResults'] == 10 and max(r['NormalizedCceGains']) <= 1e-7 for r in proof['Results']), 'Incorrect initialization or final CCE proof')
            proofs[cid] = (proof, entry)
        runs.append({'Directory': str(folder.resolve()), 'CompletedSha256': sha(folder / 'completed.json'),
                     'RequestSha256': complete['RequestSha256'], 'PolicyLedgerSha256': complete['PolicyLedgerSha256']})
    require(set(proofs) == set(expected_ids), 'Not every case has an independent final audit')
    return proofs, runs, initial


def assemble(pool, output, audits, repair_index, expected_cases=2268):
    pool, output = Path(pool).resolve(), Path(output).resolve()
    require(not output.exists(), 'Final assembly output already exists; never overwrite')
    require(pool != output and pool not in output.parents and output not in pool.parents, 'Final assembly must be separate')
    batch = read(pool / 'batch-plan.json'); manifest = read(pool / 'repetitions.json')
    cases = manifest['Cases']; by_id = {c['Id']: c for c in cases}
    require(len(cases) == len(by_id) == expected_cases and len(manifest['Runs']) == 10, 'Unexpected case or repetition coverage')
    proofs, audit_runs, initial = checked_proofs(audits, by_id)
    require(initial['AcceptedInitialSelections'] == expected_cases * 20, 'Incorrect accepted initial count')
    replacements = read(repair_index)['Replacements']; replaced = set()
    for replacement in replacements:
        cid = replacement['CaseId']
        require(cid in by_id and cid not in replaced, 'Duplicate or unexpected pooled repair')
        proof = proofs[cid][0]
        require(Path(proof['Receipt']).resolve() == Path(replacement['Receipt']).resolve() and proof['ReceiptSha256'] == replacement['ReceiptSha256'], 'Pooled repair was not selected and audited')
        replaced.add(cid)
    selected = []
    nash = {'gini': {'At1eMinus7': 0, 'At1eMinus5': 0}, 'liu': {'At1eMinus7': 0, 'At1eMinus5': 0}}
    for cid, case in by_id.items():
        proof, entry = proofs[cid]; receipt = read(proof['Receipt'])
        require(receipt['Passed'] and receipt['Case'] == case and receipt['Repetitions'] == 10, 'Final receipt case identity differs')
        require(receipt['CommonPopulationCounts'] == proof['CommonPopulationCounts'], 'Population counts differ')
        require(receipt['Rounds'][-1]['PoliciesAdded'] == 0, 'Final population was still expanding')
        if cid not in replaced:
            require(Path(proof['Receipt']).resolve() == pool / 'combined' / cid / 'completed.json', 'Unindexed pooled repair')
        selected.append({'CaseId': cid, 'SourceReceipt': proof['Receipt'], 'SourceReceiptSha256': proof['ReceiptSha256'],
                         'ValidationProof': entry['Proof'], 'ValidationProofSha256': entry['Sha256']})
        for result in proof['Results']:
            nash[result['Selector']]['At1eMinus7'] += result['ProductNashAt1eMinus7']
            nash[result['Selector']]['At1eMinus5'] += result['ProductNashAt1eMinus5']
    # Only create the new artifact after coverage and all bound files have passed.
    output.mkdir(parents=True)
    assembled_batch = dict(batch)
    assembled_batch['Schema'] = 'assembled-pooled-population-batch-v1'
    assembled_batch['Settings'] = dict(batch['Settings'], output=str(output))
    assembled_batch['SourceBatch'] = {'Directory': str(pool), 'BatchPlanSha256': sha(pool / 'batch-plan.json')}
    write_new(output / 'batch-plan.json', assembled_batch)
    shutil.copyfile(pool / 'repetitions.json', output / 'repetitions.json')
    final_paths = []
    for entry in selected:
        destination = output / 'combined' / entry['CaseId'] / 'completed.json'
        destination.parent.mkdir(parents=True)
        shutil.copyfile(entry['SourceReceipt'], destination)
        require(sha(destination) == entry['SourceReceiptSha256'], 'Receipt copy changed')
        final_paths.append(str(destination))
    receipt = {'Passed': True, 'CreatedUtc': datetime.now(timezone.utc).isoformat(),
               'SourcePool': str(pool), 'SourceBatchPlanSha256': sha(pool / 'batch-plan.json'),
               'SourceManifestSha256': sha(pool / 'repetitions.json'), 'PooledRepairIndex': str(Path(repair_index).resolve()),
               'PooledRepairIndexSha256': sha(repair_index), 'Audits': audit_runs, 'InitialValidation': initial,
               'SelectedReceipts': selected, 'PooledRepairCases': len(replaced), 'AssemblerSha256': sha(__file__),
               'Note': 'Receipts are exact copies; FinalRun and Audit retain immutable source paths. No numerical solve was rerun.'}
    write_new(output / 'assembly-receipt.json', receipt)
    write_new(output / 'summary.json', {'ArticleCases': expected_cases, 'AcceptedInitialSelections': expected_cases * 20,
               'PooledSelections': expected_cases * 2, 'NashClassificationCounts': nash,
               'Note': 'Nash classifications concern the product of policy marginals; all final joint distributions passed approximate CCE.'})
    write_new(output / 'completed.json', {'Passed': True, 'ArticleCases': expected_cases, 'Repetitions': 10,
               'CombinedRuns': expected_cases * 2, 'FinalResults': final_paths, 'AssemblyReceiptSha256': sha(output / 'assembly-receipt.json')})
    return {'Output': str(output), 'ArticleCases': expected_cases, 'CombinedRuns': expected_cases * 2,
            'PooledRepairCases': len(replaced), 'NashClassificationCounts': nash}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pool', required=True); parser.add_argument('--output', required=True)
    parser.add_argument('--audit', action='append', required=True); parser.add_argument('--repair-index', required=True)
    args = parser.parse_args()
    print(json.dumps(assemble(args.pool, args.output, args.audit, args.repair_index)))


if __name__ == '__main__': main()
