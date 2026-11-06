from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]

def table(language):
    data = {}
    for line in (ROOT / 'assets' / 'Localization' / (language + '.csv')).read_text().splitlines():
        if not line or line.startswith('#'):
            continue
        key, value = line.split(',', 1)
        assert key not in data, f'duplicate key {key}'
        data[key] = value
    return data

zh, en = table('zh'), table('en')
assert zh.keys() == en.keys(), 'locale key mismatch'
for key in zh:
    assert sorted(re.findall(r'\{\d+\}', zh[key])) == sorted(re.findall(r'\{\d+\}', en[key])), key
used = set()
for file in (ROOT / 'src').glob('*.cs'):
    used.update(re.findall(r'L\.Text\("([a-z_]+)"', file.read_text()))
assert used <= en.keys(), f'missing translations: {used - en.keys()}'
print(f'Localization passed: {len(en)} matching keys; {len(used)} source keys covered.')
