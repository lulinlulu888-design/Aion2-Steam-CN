"""Repack a reviewed payload only when new source text and key order are identical."""
import argparse
import importlib
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    for name in ('codec-dir', 'manifest', 'old-source', 'new-source', 'payload', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    inputs = [args.manifest, args.old_source, args.new_source, args.payload]
    if args.output.resolve() in [p.resolve() for p in inputs]:
        raise ValueError('Output must not overwrite an input')
    sys.path.insert(0, str(args.codec_dir))
    codec = importlib.import_module('aion2_l10n_v2')
    old, _ = codec.decrypt_container(args.old_source, args.manifest, 'en-US')
    new, prefix = codec.decrypt_container(args.new_source, args.manifest, 'en-US')
    translated, _ = codec.decrypt_container(args.payload, args.manifest, 'en-US')
    if list(old.items()) != list(new.items()):
        raise ValueError('Source changed; translation review required')
    if list(translated) != list(new):
        raise ValueError('Payload key order mismatch')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    codec.encrypt_container(args.output, translated, args.manifest, 'en-US', prefix)
    actual, actual_prefix = codec.decrypt_container(args.output, args.manifest, 'en-US')
    if actual != translated or actual_prefix != prefix:
        raise ValueError('Repacked payload verification failed')
    print('PASS: identical source; preserved translations; new container prefix; roundtrip verified')


if __name__ == '__main__':
    main()
