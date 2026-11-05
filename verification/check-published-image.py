"""Check apphost compatibility flags without changing the executable."""
import importlib.util
import struct
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('guard', root / 'Common' / 'add-startup-check.py')
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)
pe = guard.PE(bytearray(Path(sys.argv[1]).read_bytes()), allow_checked=True)
assert sum(s['name'] == b'.osguard' for s in pe.sections) == 1, 'Native OS guard missing'
characteristics = pe.u16(pe.opt + 70)
for mask, name in [(0x4000, 'CFG'), (0x100, 'DEP'), (0x40, 'ASLR'), (0x20, 'High-entropy ASLR')]:
    assert characteristics & mask, name + ' disabled'
    print('PASS:', name, 'retained')
debug_rva = pe.u32(pe.opt + 112 + 6 * 8)
debug_size = pe.u32(pe.opt + 112 + 6 * 8 + 4)
for n in range(debug_size // 28):
    _, _, _, _, kind, size, _, raw = struct.unpack_from('<IIHHIIII', pe.data, pe.offset(debug_rva) + 28 * n)
    if kind == 20:
        assert size >= 4 and not pe.u32(raw) & 1, 'CET-compatible flag still enabled'
print('PASS: CET apphost opt-out for early Windows 10 22H2')
entry = pe.u32(pe.opt + 16)
load = pe.offset(pe.u32(pe.opt + 112 + 10 * 8))
table_va, count = struct.unpack_from('<QQ', pe.data, load + 128)
image_base = struct.unpack_from('<Q', pe.data, pe.opt + 24)[0]
stride = 4 + ((pe.u32(load + 144) >> 28) & 15)
table = pe.offset(table_va - image_base)
assert entry in [pe.u32(table + stride * n) for n in range(count)], 'Guard entry missing from CFG table'
exception_rva, exception_size = struct.unpack_from('<II', pe.data, pe.opt + 112 + 3 * 8)
exception_table = pe.offset(exception_rva)
assert entry in [pe.u32(exception_table + n * 12) for n in range(exception_size // 12)], 'Guard unwind record missing'
print('PASS: Native guard registered in CFG and x64 unwind tables')
