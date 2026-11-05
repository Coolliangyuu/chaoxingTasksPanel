"""Add a tiny, position-independent OS check before the .NET apphost entry point.

Keeps the original bundle offsets, file identity, arguments, process and cache behavior.
Unsigned x64 PE files only; sign the resulting file after this build step if needed.
"""
import argparse
import ctypes
import json
import os
import struct
from pathlib import Path


def align(value, size):
    return (value + size - 1) // size * size


class PE:
    def __init__(self, data, allow_checked=False):
        self.data = data
        self.pe = self.u32(0x3c)
        if data[:2] != b'MZ' or data[self.pe:self.pe + 4] != b'PE\0\0':
            raise ValueError('Not a Windows executable')
        self.coff = self.pe + 4
        self.opt = self.coff + 20
        if self.u16(self.coff) != 0x8664 or self.u16(self.opt) != 0x20b:
            raise ValueError('Startup check requires an x64 PE32+ apphost')
        self.table = self.opt + self.u16(self.coff + 16)
        self.count = self.u16(self.coff + 2)
        self.sections = []
        for n in range(self.count):
            offset = self.table + 40 * n
            name, virtual_size, rva, raw_size, raw = struct.unpack_from('<8sIIII', data, offset)
            self.sections.append(dict(name=name.rstrip(b'\0'), vs=virtual_size, rva=rva,
                                      size=raw_size, raw=raw, header=offset))
        if not allow_checked and any(s['name'] == b'.osguard' for s in self.sections):
            raise ValueError('Startup check already added')

    def u16(self, offset): return struct.unpack_from('<H', self.data, offset)[0]
    def u32(self, offset): return struct.unpack_from('<I', self.data, offset)[0]
    def p32(self, offset, value): struct.pack_into('<I', self.data, offset, value)

    def offset(self, rva):
        if rva < self.u32(self.opt + 60): return rva
        for section in self.sections:
            delta = rva - section['rva']
            if 0 <= delta < section['size']: return section['raw'] + delta
        raise ValueError(f'RVA is not backed by file data: {rva:#x}')

    def cstring(self, offset):
        return bytes(self.data[offset:self.data.index(0, offset)]).decode('ascii')

    def imports(self):
        result = {}
        rva = self.u32(self.opt + 112 + 8)
        cursor = self.offset(rva)
        while any(self.data[cursor:cursor + 20]):
            original, _, _, _, first = struct.unpack_from('<5I', self.data, cursor)
            names = self.offset(original or first)
            n = 0
            while (value := struct.unpack_from('<Q', self.data, names + 8 * n)[0]):
                if value < (1 << 63):
                    name = self.cstring(self.offset(value) + 2)
                    result[name] = first + 8 * n
                n += 1
            cursor += 20
        return result

    def exports(self):
        rva = self.u32(self.opt + 112)
        if not rva: return {}
        table = self.offset(rva)
        count = self.u32(table + 24)
        functions = self.offset(self.u32(table + 28))
        names = self.offset(self.u32(table + 32))
        ordinals = self.offset(self.u32(table + 36))
        result = {}
        for n in range(count):
            name = self.cstring(self.offset(self.u32(names + 4 * n)))
            ordinal = self.u16(ordinals + 2 * n)
            result[name] = self.u32(functions + 4 * ordinal)
        return result


class Code:
    def __init__(self, rva):
        self.rva, self.data, self.labels, self.fixups = rva, bytearray(), {}, []

    def emit(self, value): self.data.extend(value)
    def label(self, name): self.labels[name] = len(self.data)
    def relative(self, opcode, target):
        self.emit(opcode)
        self.fixups.append((len(self.data), target))
        self.emit(b'\0' * 4)
    def finish(self):
        for offset, target in self.fixups:
            destination = self.rva + self.labels[target] if isinstance(target, str) else target
            struct.pack_into('<i', self.data, offset, destination - (self.rva + offset + 4))
        return self.data


def build_check(rva, entry, imports, policy):
    c = Code(rva)
    c.emit(b'\x48\x81\xec' + struct.pack('<I', 0x158))  # shadow space, version structure, saved arguments
    for opcode, offset in [(b'\x48\x89\x8c\x24', 0x138), (b'\x48\x89\x94\x24', 0x140),
                           (b'\x4c\x89\x84\x24', 0x148), (b'\x4c\x89\x8c\x24', 0x150)]:
        c.emit(opcode + struct.pack('<I', offset))
    c.emit(b'\x66\x0f\xef\xc0')  # pxor xmm0,xmm0
    for offset in range(0x20, 0x130, 16):
        c.emit(b'\xf3\x0f\x7f\x84\x24' + struct.pack('<I', offset))
    c.emit(b'\xc7\x84\x24' + struct.pack('<II', 0x130, 0))
    c.emit(b'\xc7\x44\x24\x20' + struct.pack('<I', 276))
    c.relative(b'\x48\x8d\x0d', 'ntdll')
    c.relative(b'\xff\x15', imports['GetModuleHandleW'])
    c.emit(b'\x48\x85\xc0'); c.relative(b'\x0f\x84', 'blocked')
    c.emit(b'\x48\x89\xc1')
    c.relative(b'\x48\x8d\x15', 'rtl')
    c.relative(b'\xff\x15', imports['GetProcAddress'])
    c.emit(b'\x48\x85\xc0'); c.relative(b'\x0f\x84', 'blocked')
    c.emit(b'\x48\x8d\x4c\x24\x24\x48\x8d\x54\x24\x28\x4c\x8d\x44\x24\x2c\xff\xd0')
    c.emit(b'\x81\x64\x24\x2c\xff\xff\xff\x0f')  # strip checked/free kernel build flags
    c.emit(b'\x81\x7c\x24\x24' + struct.pack('<I', policy['minimumMajor']))
    c.relative(b'\x0f\x82', 'blocked'); c.relative(b'\x0f\x87', 'allowed')
    c.emit(b'\x81\x7c\x24\x2c' + struct.pack('<I', policy['minimumBuild']))
    c.relative(b'\x0f\x83', 'allowed')
    c.label('blocked')
    c.emit(b'\x31\xc9')
    c.relative(b'\x48\x8d\x15', 'message'); c.relative(b'\x4c\x8d\x05', 'title')
    c.emit(b'\x41\xb9\x30\x00\x00\x00')
    c.relative(b'\xff\x15', imports['MessageBoxW'])
    c.emit(b'\xb9' + struct.pack('<I', 1150))
    c.relative(b'\xff\x15', imports['ExitProcess']); c.emit(b'\x0f\x0b')
    c.label('allowed')
    for opcode, offset in [(b'\x48\x8b\x8c\x24', 0x138), (b'\x48\x8b\x94\x24', 0x140),
                           (b'\x4c\x8b\x84\x24', 0x148), (b'\x4c\x8b\x8c\x24', 0x150)]:
        c.emit(opcode + struct.pack('<I', offset))
    c.emit(b'\x48\x81\xc4' + struct.pack('<I', 0x158)); c.relative(b'\xe9', entry)
    code_length = len(c.data)
    c.label('rtl'); c.emit(b'RtlGetNtVersionNumbers\0')
    if len(c.data) % 2: c.emit(b'\0')
    for label, text in [('ntdll', 'ntdll.dll'), ('title', '系统版本不受支持 · 学习通作业看板'), ('message', policy['message'])]:
        c.label(label); c.emit((text + '\0').encode('utf-16-le'))
    while len(c.data) % 4: c.emit(b'\0')
    unwind_offset = len(c.data)
    c.emit(bytes([1, 7, 2, 0, 7, 1, 0x2b, 0]))  # UWOP_ALLOC_LARGE: 344-byte stack frame
    return c.finish(), code_length, unwind_offset


def patch(source, destination, policy, overrides=None):
    data = bytearray(Path(source).read_bytes())
    pe = PE(data)
    if pe.u32(pe.opt + 112 + 4 * 8 + 4):
        raise ValueError('Patch before signing; signed executables must not be modified')
    end = pe.table + 40 * (pe.count + 1)
    if end > min(pe.u32(pe.opt + 60), min(s['raw'] for s in pe.sections if s['raw'])):
        raise ValueError('Not enough unused PE header space')
    if any(data[pe.table + 40 * pe.count:end]):
        raise ValueError('Additional section would overwrite nonzero header data')
    original_entry = pe.u32(pe.opt + 16)
    rva = align(max(s['rva'] + max(s['vs'], s['size']) for s in pe.sections), pe.u32(pe.opt + 32))
    imports = pe.imports(); imports.update(overrides or {})
    payload, code_size, unwind = build_check(rva, original_entry, imports, policy)
    # MSVC apphosts use Control Flow Guard. Register the new entry in a copied
    # sorted GFID table instead of disabling CFG or any process mitigation.
    load_config_rva = pe.u32(pe.opt + 112 + 10 * 8)
    if load_config_rva:
        load_config = pe.offset(load_config_rva)
        if pe.u32(load_config) >= 148:
            table_va, count = struct.unpack_from('<QQ', data, load_config + 128)
            flags = pe.u32(load_config + 144)
            if table_va and count:
                image_base = struct.unpack_from('<Q', data, pe.opt + 24)[0]
                stride = 4 + ((flags >> 28) & 15)
                table_offset = pe.offset(table_va - image_base)
                table = data[table_offset:table_offset + count * stride]
                if len(table) != count * stride or struct.unpack_from('<I', table, (count - 1) * stride)[0] >= rva:
                    raise ValueError('Unexpected CFG function table')
                while len(payload) % 4: payload.extend(b'\0')
                new_table_rva = rva + len(payload)
                payload.extend(table)
                payload.extend(struct.pack('<I', rva) + b'\0' * (stride - 4))
                struct.pack_into('<QQ', data, load_config + 128, image_base + new_table_rva, count + 1)
    raw = align(len(data), pe.u32(pe.opt + 36)); raw_size = align(len(payload), pe.u32(pe.opt + 36))
    exception_rva = pe.u32(pe.opt + 112 + 3 * 8)
    exception_size = pe.u32(pe.opt + 112 + 3 * 8 + 4)
    if not exception_rva or exception_size % 12: raise ValueError('Unexpected x64 unwind table')
    insertion = pe.offset(exception_rva) + exception_size
    section = next(s for s in pe.sections if s['raw'] <= insertion < s['raw'] + s['size'])
    if insertion + 12 > section['raw'] + section['size'] or any(data[insertion:insertion + 12]):
        raise ValueError('Not enough unused unwind-table padding')
    if pe.u32(insertion - 12) >= rva: raise ValueError('Unwind table would not be sorted')
    struct.pack_into('<III', data, insertion, rva, rva + code_size, rva + unwind)
    pe.p32(section['header'] + 8, max(section['vs'], insertion + 12 - section['raw']))
    pe.p32(pe.opt + 112 + 3 * 8 + 4, exception_size + 12)
    struct.pack_into('<8sIIIIIIHHI', data, pe.table + 40 * pe.count, b'.osguard', len(payload), rva,
                     raw_size, raw, 0, 0, 0, 0, 0x60000020)
    struct.pack_into('<H', data, pe.coff + 2, pe.count + 1)
    pe.p32(pe.opt + 16, rva)
    pe.p32(pe.opt + 56, align(rva + len(payload), pe.u32(pe.opt + 32)))
    pe.p32(pe.opt + 4, pe.u32(pe.opt + 4) + raw_size)
    pe.p32(pe.opt + 64, 0)
    data.extend(b'\0' * (raw - len(data))); data.extend(payload); data.extend(b'\0' * (raw_size - len(payload)))
    Path(destination).write_bytes(data)
    if os.name == 'nt':
        old, computed = ctypes.c_uint32(), ctypes.c_uint32()
        api = ctypes.WinDLL('imagehlp').MapFileAndCheckSumW
        api.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_uint32)]
        if api(str(Path(destination).resolve()), ctypes.byref(old), ctypes.byref(computed)) == 0:
            with Path(destination).open('r+b') as file:
                file.seek(pe.opt + 64); file.write(struct.pack('<I', computed.value))
    return dict(originalBytes=Path(source).stat().st_size, finalBytes=len(data), guardBytes=raw_size,
                originalEntryRva=original_entry, checkedEntryRva=rva)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path); parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    policy = json.loads((Path(__file__).resolve().parent.parent / 'compatibility-policy.json').read_text(encoding='utf-8-sig'))
    print(json.dumps(patch(args.source, args.destination, policy)))
