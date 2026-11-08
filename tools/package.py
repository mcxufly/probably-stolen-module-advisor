from pathlib import Path
import hashlib
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DIST = (ROOT / 'dist' / 'ModuleAdvisor').resolve()
assert DIST == ROOT / 'dist' / 'ModuleAdvisor' and DIST.parent == ROOT / 'dist'
# Only remove this project's explicitly checked staging directory.
if DIST.exists():
    shutil.rmtree(DIST)
DIST.mkdir(parents=True)
shutil.copy2(ROOT / 'bin/Release/netstandard2.1/ProbablyStolenModuleAdvisor.dll', DIST)
shutil.copytree(ROOT / 'assets', DIST, dirs_exist_ok=True)
shutil.copy2(ROOT / 'README.md', DIST)
license_bytes = (ROOT / 'LICENSE').read_bytes()
assert hashlib.sha256(license_bytes).hexdigest() == '3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986', 'Incomplete GPL v3 license'
shutil.copy2(ROOT / 'LICENSE', DIST)
version = ET.parse(DIST / 'manifest.xml').findtext('ModVersion')
archive = ROOT / f'ModuleAdvisor-{version}.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as package:
    for file in sorted(DIST.rglob('*')):
        if file.is_file():
            package.write(file, file.relative_to(DIST.parent))
print('Staged:', DIST)
print('Package:', archive.name)
print('SHA256:', hashlib.sha256(archive.read_bytes()).hexdigest())
