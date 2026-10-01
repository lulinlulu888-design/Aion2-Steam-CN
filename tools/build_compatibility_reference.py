"""Build private runtime data from legally obtained local files; never commit output."""
import argparse
import gzip
import hashlib
import importlib
from pathlib import Path
import re
import struct
import sys


def main():
    parser = argparse.ArgumentParser()
    for name in ('codec-dir', 'manifest', 'english', 'pak-key-source', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    if args.output.resolve() in {p.resolve() for p in (args.manifest, args.english, args.pak_key_source)}:
        raise ValueError('Output must not overwrite inputs')
    sys.path.insert(0, str(args.codec_dir))
    codec = importlib.import_module('aion2_l10n_v2')
    entries, _ = codec.decrypt_container(args.english, args.manifest, 'en-US')
    source = args.pak_key_source.read_text(encoding='utf-8-sig')
    match = re.search(r'const string AesKey = "([^"]+)"', source)
    if not match:
        raise ValueError('Local PAK key configuration unavailable')
    key = match[1].removeprefix('0x')
    if not re.fullmatch(r'[0-9a-fA-F]{64}', key):
        raise ValueError('Expected a hexadecimal PAK key')
    seed = codec.hash64(b'L10NString_en-US')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(args.output, 'wb') as output:
        output.write(b'A2CR\x01')
        output.write(bytes.fromhex(key))
        output.write(codec.load_manifest(args.manifest)[seed])
        output.write(codec.header_key(seed)[:16])
        output.write(struct.pack('<i', len(entries)))
        for name, value in entries.items():
            encoded = name.encode('utf-8')
            output.write(struct.pack('<i', len(encoded)))
            output.write(encoded)
            output.write(hashlib.sha256(value.encode('utf-8')).digest())
    print(f'Built reference fingerprints for {len(entries)} entries; no source values written.')


if __name__ == '__main__':
    main()
