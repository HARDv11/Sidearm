"""Adds or changes Ukrainian interface strings in ui/lang/uk.js (the one dictionary the shell and the pages share).

    python3 tools/uk_add.py new-strings.json

new-strings.json:
    {
      "exact":     { "Pop out video": "Винести відео" },
      "templates": [ ["Timer set: {1}", "Таймер на {1}"] ],
      "page":      { "Close (Esc)": "Закрити (Esc)" },
      "remove":    [ "An English string that no longer exists" ]
    }

exact = whole strings; templates = strings with variable parts {1}, {2}; page = strings used inside web pages
(inject.js, reader). Templates are re-sorted so the most specific ones are tried first.
"""
import json, sys

P = 'ui/lang/uk.js'
src = open(P, encoding='utf-8').read()
i = src.index('{', src.index('='))
d = json.loads(src[i:src.rstrip().rstrip(';').rindex('}') + 1])
new = json.load(open(sys.argv[1], encoding='utf-8'))
changed = 0
for k in new.get('remove', []):
    if d['exact'].pop(k, None) is not None: changed += 1
for k, v in new.get('exact', {}).items():
    if d['exact'].get(k) != v: d['exact'][k] = v; changed += 1
for k, v in new.get('page', {}).items():
    if d['page'].get(k) != v: d['page'][k] = v; changed += 1
have = {a: n for n, (a, b) in enumerate(d['templates'])}
for a, b in new.get('templates', []):
    if a in have: d['templates'][have[a]] = [a, b]
    else: d['templates'].append([a, b])
    changed += 1
d['templates'].sort(key=lambda t: -len(t[0].replace('{1}', '').replace('{2}', '').replace('{3}', '')))

def obj(o):
    return '{\n' + ',\n'.join(json.dumps(k, ensure_ascii=False) + ': ' + json.dumps(v, ensure_ascii=False) for k, v in o.items()) + '\n}'
out = ('/* Sidearm in Ukrainian. exact: whole strings; templates: {1}, {2} keep their part as is; page: used inside web pages. */\n'
       'window.SIDEARM_UK = {\n"exact": ' + obj(d['exact']) + ',\n"templates": [\n' +
       ',\n'.join(json.dumps(t, ensure_ascii=False) for t in d['templates']) + '\n],\n"page": ' + obj(d['page']) + '\n};\n')
open(P, 'w', encoding='utf-8').write(out)
print('changed', changed, '· exact', len(d['exact']), '· templates', len(d['templates']), '· page', len(d['page']))
