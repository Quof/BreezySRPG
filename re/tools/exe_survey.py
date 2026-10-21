"""Survey a PE executable: sections, imports, and cp932/ASCII strings with their virtual addresses.

    python re/tools/exe_survey.py EXE [--strings PATTERN]
"""
import re
import sys

import pefile


def main():
    path = sys.argv[1]
    pat = sys.argv[sys.argv.index('--strings') + 1] if '--strings' in sys.argv else None
    pe = pefile.PE(path)
    base = pe.OPTIONAL_HEADER.ImageBase
    print(f'image base 0x{base:08X}, entry 0x{base + pe.OPTIONAL_HEADER.AddressOfEntryPoint:08X}, '
          f'timestamp 0x{pe.FILE_HEADER.TimeDateStamp:08X}, machine 0x{pe.FILE_HEADER.Machine:X}')
    for s in pe.sections:
        print(f'  {s.Name.rstrip(b"\\0").decode(errors="replace"):8} va 0x{base + s.VirtualAddress:08X} '
              f'vsize 0x{s.Misc_VirtualSize:06X} raw 0x{s.SizeOfRawData:06X}')
    if not pat:
        for entry in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', []):
            names = [i.name.decode() if i.name else f'#{i.ordinal}' for i in entry.imports]
            print(f'  {entry.dll.decode()}: {len(names)}: ' + ', '.join(names))
        return
    data = pe.__data__
    rx = re.compile(pat)
    for m in re.finditer(rb'(?:[\x20-\x7e]|[\x81-\x9f\xe0-\xfc][\x40-\xfc]){4,}', data):
        try:
            s = m.group().decode('cp932')
        except UnicodeDecodeError:
            continue
        if rx.search(s):
            off = m.start()
            sec = next((x for x in pe.sections if x.PointerToRawData <= off < x.PointerToRawData + x.SizeOfRawData), None)
            va = base + sec.VirtualAddress + off - sec.PointerToRawData if sec else None
            print(f'  0x{va:08X} {s}' if va else f'  file 0x{off:X} {s}')


if __name__ == '__main__':
    main()
