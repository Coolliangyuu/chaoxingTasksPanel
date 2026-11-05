import importlib.util
import json
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
module_spec = importlib.util.spec_from_file_location('startup_check', root / 'Common' / 'add-startup-check.py')
module = importlib.util.module_from_spec(module_spec)
module_spec.loader.exec_module(module)
policy = json.loads((root / 'compatibility-policy.json').read_text(encoding='utf-8-sig'))
target = Path(sys.argv[1]).resolve()
target.mkdir(parents=True, exist_ok=True)
cases = [('win10_1507', 10, 10240, 0, False), ('win10_21h2', 10, 19044, 0, False),
         ('win10_22h2', 10, 19045, 0, True), ('win11_21h2', 10, 22000, 0, True),
         ('win11_24h2', 10, 26100, 0, True), ('win81', 6, 9600, 0, False),
         ('version_query_failed', 10, 26100, -1, False), ('version_api_missing', 10, 26100, 0, False)]
reports = []
for name, major, build, status, allowed in cases:
    original = target / f'{name}-original.exe'
    checked = target / f'{name}.exe'
    command = ['g++.exe', '-O2', '-static', '-s', f'-DOS_BUILD={build}', f'-DOS_MAJOR={major}', f'-DRTL_STATUS={status}']
    if name == 'version_api_missing': command += ['-DMISSING_RTL']
    subprocess.run(command + [str(root / 'verification' / 'native-guard-host.cpp'), '-o', str(original)], check=True)
    pe = module.PE(bytearray(original.read_bytes()))
    exports = pe.exports()
    size = module.patch(original, checked, policy, {'GetProcAddress': exports['guard_getproc'],
                                                  'MessageBoxW': exports['guard_message']})
    process = subprocess.run([str(checked)], capture_output=True, text=True, timeout=15)
    marker = 'APP_ENTRY' if allowed else 'BLOCKED_BEFORE_APP'
    expected = 0 if allowed else 1150
    if process.returncode != expected or marker not in process.stdout or (not allowed and 'APP_ENTRY' in process.stdout):
        raise AssertionError((name, process.returncode, process.stdout, process.stderr))
    reports.append(dict(case=name, allowed=allowed, exit=process.returncode, addedBytes=size['finalBytes']-size['originalBytes']))
    print(f'PASS: {name} -> {"starts application" if allowed else "blocked before application entry"}')
(target / 'results.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')
