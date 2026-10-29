# Gera a tabela de IDs PCI de src/HWSuite.Core/Gpu/GpuDeviceIds.cs a partir da pci.ids.
# Uso: python3 tools/gen-gpu-device-ids.py pci.ids src/HWSuite.Core/Gpu/GpuDatabase.cs saida.txt
# e cole a saída no dicionário de GpuDeviceIds.cs (atualize também a versão da pci.ids no comentário).
import re, sys
pci, db, out = sys.argv[1:4]
src = open(db).read()
models = []
for m in re.finditer(r'new\("([^"]+)", "([^"]+)", (\w+),', src):
    if (m.group(1), m.group(2)) not in models:
        models.append((m.group(1), m.group(2)))
vendors = {'1002': [], '10de': [], '8086': []}
cur = None
for line in open(pci, encoding='utf-8', errors='replace'):
    if line.startswith('#') or not line.strip(): continue
    mm = re.match(r'^([0-9a-f]{4})  (.*)', line)
    if mm: cur = mm.group(1) if mm.group(1) in vendors else None; continue
    dm = re.match(r'^\t([0-9a-f]{4})  (.*)', line)
    if cur and dm: vendors[cur].append((dm.group(1), dm.group(2).strip()))
skip = re.compile(r'Audio|USB|SRIOV|MxGPU|Virtual', re.I)
def vendor_of(model):
    return '1002' if model.startswith('RX') else '10de' if model.startswith(('RTX', 'GTX', 'GT ')) else '8086'
def mentions(name, model):
    br = re.search(r'\[(.*)\]', name)
    if not br: return False
    text = br.group(1)
    for m in re.finditer(r'(?<![\w])' + re.escape(model) + r'(?![\w])', text):
        rest = text[m.end():].lstrip()
        if re.match(r'(Ti|SUPER|D|XT|GRE|M|Max-Q|Laptop|E)\b', rest): continue
        return True
    return False
table = {}
for model, chip in models:
    v = vendor_of(model)
    # pci.ids names Polaris dies by their code names.
    alias = {'Polaris 10': 'Ellesmere', 'Polaris 20': 'Ellesmere', 'Polaris 30': 'Ellesmere',
             'Polaris 11': 'Baffin', 'Polaris 21': 'Baffin', 'Polaris 12': 'Lexa'}
    chips = [alias.get(c.strip(), c.strip()) for c in chip.split('/')]
    for dev, name in vendors[v]:
        if skip.search(name): continue
        ok = False
        for c in chips:
            if v == '8086': continue
            if re.match(re.escape(c) + r'(?![0-9])', name): ok = True
        if mentions(name, model.replace('Arc ', 'Arc ')): ok = True
        if ok: table.setdefault((v, dev), set()).add(model)
lines = []
for (v, dev), ms in sorted(table.items()):
    lines.append(f'        [(0x{v.upper()}, 0x{dev.upper()})] = [{", ".join(chr(34)+m+chr(34) for m in sorted(ms))}],')
covered = {m for ms in table.values() for m in ms}
missing = [m for m,_ in models if m not in covered]
print('entries', len(lines), 'missing', missing, file=sys.stderr)
open(out, 'w').write('\n'.join(lines) + '\n')
