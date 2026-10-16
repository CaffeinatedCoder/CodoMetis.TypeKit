# Appends a `prober rows` remainder to a claim's rows file. Where a remainder row needs shape.wrapped pairs but carries a value
# that cannot be declared with a wrapped type (a closed set; a parse shape that is its own class), and that axis is in none of
# the row's needed pairs, the axis is set to a value that can.
import json, sys
wt, claim, more = sys.argv[1:4]
p = f'{wt}/tools/probe/corpus/rows/{claim}.json'
d = json.load(open(p)); m = json.load(open(more))
fixed = 0
for r in m['rows']:
    needed = {s.split('=')[0] for pair in r['pairs'] for s in pair}
    row = r['row']
    if 'shape.wrapped' in needed:
        if row.get('shape.kind') == 'closed-set' and 'shape.kind' not in needed:
            row['shape.kind'] = 'sealed-record-class'; fixed += 1
        if row.get('parse-shape', 'ordinary') != 'ordinary' and 'parse-shape' not in needed:
            row['parse-shape'] = 'ordinary'; fixed += 1
    if row.get('shape.kind') in ('record-struct', 'validated-struct') and row.get('shape.inheritance', 'none') not in ('none', 'explicit-interface-member') \
            and 'shape.kind' not in needed:
        row['shape.kind'] = 'sealed-record-class'; fixed += 1
d['rows'].extend(m['rows'])
json.dump(d, open(p, 'w'), indent=2)
print(claim, 'appended', len(m['rows']), 'adjusted', fixed, 'total', len(d['rows']))
