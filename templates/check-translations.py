"""Her *BusinessMessages.cs'teki hata kodunun (\"özellik.anahtar\") her dilde çevirisi var mı? Eksikleri yazar, varsa 1 döner."""
import glob, json, os, re, sys

app = sys.argv[1]  # .../src/X.Application
def load(path):
    text = re.sub(r'^\s*//.*$', '', open(path, encoding='utf-8').read(), flags=re.M)
    out = {}
    def walk(prefix, node):
        for k, v in node.items():
            key = f'{prefix}.{k}' if prefix else k
            if isinstance(v, dict): walk(key, v)
            else: out[key] = v
    walk('', json.loads(text))
    return out

keys = {}
files = glob.glob(os.path.join(app, 'Features', '*', 'Localization', '*.json')) + glob.glob(os.path.join(app, 'Localization', '*.json'))
for f in files:
    culture = os.path.basename(f).split('.')[-2]
    try:
        keys.setdefault(culture, {}).update(load(f))
    except json.JSONDecodeError as e:
        print(f'  Çeviri dosyası okunamadı: {f}: {e}'); sys.exit(1)

problems = []
if not keys:
    problems.append('  Çeviri dosyası yok (Features/*/Localization/*.json)')
for f in glob.glob(os.path.join(app, 'Features', '*', 'Constants', '*BusinessMessages.cs')):
    source = re.sub(r'//.*$', '', open(f, encoding='utf-8').read(), flags=re.M)  # yorumdaki örnekler sayılmaz
    for name, code in re.findall(r'const string (\w+)\s*=\s*"([a-z0-9_]+\.[a-z0-9_.]+)"', source):
        for culture, texts in sorted(keys.items()):
            if code not in texts:
                problems.append(f'  {culture}: "{code}" çevirisi yok ({os.path.basename(f)}.{name})')
for culture, texts in sorted(keys.items()):
    other = set().union(*(set(t) for c, t in keys.items() if c != culture)) if len(keys) > 1 else set()
    for key in sorted(other - set(texts)):
        problems.append(f'  {culture}: "{key}" başka dilde var, bu dilde yok')
print('\n'.join(problems))
sys.exit(1 if problems else 0)
