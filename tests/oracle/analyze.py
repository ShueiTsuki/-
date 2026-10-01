import json, collections, sys
r = json.load(open('out/report.json', encoding='utf-8'))
cases = {c['id']: c for c in json.load(open('cases/basic.json', encoding='utf-8'))['cases']}
groups = collections.defaultdict(list)
for d in r['diffs']:
    groups[d['action']].append(d)
def short(steps):
    out = []
    for s in steps:
        out.append({k: s[k] for k in ('res', 'stack', 'mishaps', 'media', 'ops') if k in s})
    return json.dumps(out, ensure_ascii=False)[:400]
only = sys.argv[1:] or None
for act, ds in sorted(groups.items(), key=lambda kv: -len(kv[1])):
    if only and act not in only:
        continue
    print('==', act, len(ds), collections.Counter(d['where'] for d in ds).most_common(3))
    for d in ds[:int(2 if not only else 6)]:
        print('  ', d['id'], 'stack=', json.dumps(cases[d['id']]['stack'], ensure_ascii=False)[:160])
        print('     orig', short(d['original']['steps']))
        print('     port', short(d['port']['steps']))
