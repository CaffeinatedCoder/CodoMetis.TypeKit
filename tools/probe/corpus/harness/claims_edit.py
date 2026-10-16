# Writes the exclude/narrow/excludeWhy entries of the build family's claims into claims.json,
# splicing only those claim objects so the rest of the file keeps its bytes.
import json, re, sys
S = '/private/tmp/claude-501/-Users-ricardo-gross-RiderProjects-CodoMetis-TypeKit/d668d67b-8e0b-41a0-9b94-a0997a1a98d6/scratchpad'
WT = S + '/wt-build'
PATH = WT + '/tools/probe/corpus/claims.json'
U = json.load(open(S + '/state-build/branches/test-probe-gate-small-claims/universe.json'))
AX = {c['id']: c['axes'] for c in U['claims']}

C09_QUOTE = "Over a base record, a value object's equality is its wrapped value's, in a collection of the base too, whatever equality the base declares."
C11_QUOTE = "A wrapped type's `Parse` whose result can't be assigned to it, or that takes its text by `ref`, or an abstract class's string constructor, no longer breaks the build; the value object has no parsing."

NOBASE = "A record struct or a closed set has no base record: C# gives a struct no base type, and a closed set is a sealed class that is never a record (CMTK1020)."
STRUCT_NOBASE = "A record struct has no base type, so it cannot derive from a base record or inherit a base's explicit IEquatable."
IN_GENERIC = "A value object nested in a generic type fails the build with CMTK1005 (Generators README, Build errors), so it has no generated equality or parsing to check."
REFUSED = "A declaration of a refused kind fails the build (CMTK1005, CMTK1006), so it is generated into nothing; .claude/probe-axes.md: shape.refused-kind is taken only where the refusal is the claim."
CLOSED_NO_WRAPPED = "A closed set wraps no type, so a closed set over a wrapped type cannot be declared."

def spec(cid):
    a = AX[cid]
    rules = []  # (list of fixed settings, reason): every pair containing all of them is excluded
    narrow = {}
    if cid == 'C09-base-record-equality':
        narrow = {'shape.position': C09_QUOTE, 'shape.containing': C09_QUOTE}
        for k in ['record-struct', 'validated-struct', 'closed-set']: rules.append(([f'shape.kind={k}'], NOBASE))
        rules.append((['shape.site=in-generic-type'], IN_GENERIC))
        rules.append((['shape.inheritance=none'], "The claim is about a value object over a base record; with no base record it states nothing."))
        for v in a['shape.refused-kind']: rules.append(([f'shape.refused-kind={v}'], REFUSED))
    elif cid == 'C11-parse-shapes-build':
        narrow = {'shape.position': C11_QUOTE, 'shape.containing': C11_QUOTE}
        rules.append((['shape.kind=closed-set'], "A closed set wraps no type, so it has no wrapped type's Parse to take for its parser."))
        rules.append((['shape.site=in-generic-type'], IN_GENERIC))
        for k in ['record-struct', 'validated-struct']:
            for i in ['base-depth-1', 'base-depth-2', 'base-depth-3', 'base-explicit-IEquatable']:
                rules.append(([f'shape.kind={k}', f'shape.inheritance={i}'], STRUCT_NOBASE))
        for p in ['implicit-result', 'ref-text', 'abstract-string-ctor']:
            for w in a['shape.wrapped']:
                rules.append(([f'parse-shape={p}', f'shape.wrapped={w}'], "The parse shape is the wrapped type itself, a class of its own; a value object over one of the catalogue's wrapped types is the ordinary shape."))
        for v in a['shape.refused-kind']: rules.append(([f'shape.refused-kind={v}'], REFUSED))
    elif cid == 'C17-cmtk0014':
        for v in ['(FaultKind)9', 'valid']:
            rules.append(([f'value={v}', 'site=default-literal'], "A default literal is the value 0, so it cannot spell (FaultKind)9 or a member: FaultKind's members start at 1."))
        for k in ['record-struct', 'validated-struct']:
            for i in ['base-depth-1', 'base-depth-2', 'base-depth-3', 'base-explicit-IEquatable']:
                rules.append(([f'shape.kind={k}', f'shape.inheritance={i}'], STRUCT_NOBASE))
        for w in a['shape.wrapped']: rules.append((['shape.kind=closed-set', f'shape.wrapped={w}'], CLOSED_NO_WRAPPED))
    elif cid == 'C18-cmtk1023':
        for k in ['record-struct', 'validated-struct', 'closed-set']: rules.append(([f'shape.kind={k}'], NOBASE + " CMTK1023 is about a base record's equality."))
        rules.append((['shape.site=in-generic-type'], IN_GENERIC))
        rules.append((['shape.inheritance=none'], "Without a base record there is no base to declare Equals, GetHashCode or IEquatable on."))
        for v in a['shape.refused-kind']: rules.append(([f'shape.refused-kind={v}'], "CMTK1023 is 'A warning, and the value object is generated' (Generators README, Build errors); a refused declaration is not generated. " + REFUSED))
    elif cid == 'C19-cmtk0010-kinds':
        for k in ['record-struct', 'validated-struct']:
            for i in ['base-depth-1', 'base-depth-2', 'base-depth-3', 'base-explicit-IEquatable']:
                rules.append(([f'shape.kind={k}', f'shape.inheritance={i}'], STRUCT_NOBASE))
        for w in a['shape.wrapped']: rules.append((['shape.kind=closed-set', f'shape.wrapped={w}'], CLOSED_NO_WRAPPED))
    live = {k: v for k, v in a.items() if k not in narrow}
    pairs = set(); why = []
    for fixed, reason in rules:
        if len(fixed) == 2:
            pairs.add(tuple(sorted(fixed)))
        else:
            ax, val = fixed[0].split('=', 1)
            for other, vals in live.items():
                if other == ax: continue
                for v in vals: pairs.add(tuple(sorted([fixed[0], f'{other}={v}'])))
        why.append((fixed, reason))
    # group reasons
    grouped = {}
    for fixed, reason in why:
        grouped.setdefault(reason, []).append(' + '.join(fixed) if len(fixed) > 1 else fixed[0] + ' with every value of every other axis')
    return narrow, sorted(pairs), [{'pairs': v, 'reason': k} for k, v in grouped.items()]

text = open(PATH).read()
for cid in ['C09-base-record-equality', 'C11-parse-shapes-build', 'C17-cmtk0014', 'C18-cmtk1023', 'C19-cmtk0010-kinds']:
    m = re.search(r'\n  \{\n    "id": "' + re.escape(cid) + r'",.*?\n  \}', text, re.S)
    obj = json.loads(m.group(0)[1:])
    narrow, pairs, why = spec(cid)
    for k in ('narrow', 'exclude', 'excludeWhy'): obj.pop(k, None)
    if narrow: obj['narrow'] = narrow
    if pairs: obj['exclude'] = '@@EXCL@@'
    if why: obj['excludeWhy'] = why
    body = json.dumps(obj, indent=2, ensure_ascii=False)
    if pairs:
        lines = ',\n'.join('    ' + json.dumps(list(p), ensure_ascii=False) for p in pairs)
        body = body.replace('"@@EXCL@@"', '[\n' + lines + '\n  ]')
    body = '\n'.join('  ' + l for l in body.split('\n'))
    text = text[:m.start()] + '\n' + body + text[m.end():]
    print(cid, 'narrow', list(narrow), 'exclude', len(pairs))
open(PATH, 'w').write(text)
json.load(open(PATH))
