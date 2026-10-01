"""Build a local bilingual review inventory; never treat heuristics as approval."""
import argparse
from collections import Counter
import importlib
import json
from pathlib import Path
import re
import sys

from skill_text_review import TOKEN, mask

JOBS = r'ASSASSIN|CHANTER|CLERIC|ELEMENTALIST|GLADIATOR|RANGER|SORCERER|TEMPLAR'
PLAYER = re.compile(r'_PC_(' + JOBS + r')_(\d+)_')
RISKS = [
    ('displacement', r'Magic Displacement', r'魔法位移'),
    ('cooldown_direction', r'cooldown (?:increase|reduction)', r'冷却'),
    ('duration_vs_expiry', r'for the duration|when .* ends|after .* ends', r'结束|期间'),
    ('damage_distribution', r'divided|directing|redirect|absorb|convert|exchange', r'交换|转移|吸收|分割'),
    ('conditional', r'only|unless|if |while |cannot|does not|unaffected', r''),
    ('multiplier', r'multipl|times|double|Multi-Hit', r'倍|双击|多段'),
]


def build_inventory(english, current):
    groups = {}
    for key, source in english.items():
        job = PLAYER.search(key)
        # Includes all player names, specs, descriptions and specializations.
        # Also inventory all status records; they are not assumed player-visible.
        if not job and not key.startswith('SkillAbnormalString_'):
            continue
        translated = current[key]
        source_template = mask(source)[0]
        translated_template = mask(translated)[0]
        identity = (job[1] if job else 'STATUS', source_template, translated_template)
        if identity not in groups:
            flags = [name for name, pattern, _ in RISKS if re.search(pattern, source, re.I)]
            numbers = lambda text: Counter(re.findall(r'\d+(?:\.\d+)?', TOKEN.sub('', text)))
            if numbers(source) != numbers(translated):
                flags.append('literal_number_difference')
            if sorted(TOKEN.findall(source)) != sorted(TOKEN.findall(translated)):
                flags.append('token_difference')
            groups[identity] = dict(job=identity[0], source=source_template,
                                    translation=translated_template, flags=flags, keys=[])
        groups[identity]['keys'].append(key)
    rows = list(groups.values())
    for index, row in enumerate(rows):
        row['id'] = index
    return rows


def main():
    parser = argparse.ArgumentParser()
    for name in ('codec-dir', 'english', 'payload', 'manifest', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    if args.output.resolve() in {p.resolve() for p in (args.english, args.payload, args.manifest)}:
        raise ValueError('Output must not overwrite input')
    sys.path.insert(0, str(args.codec_dir))
    codec = importlib.import_module('aion2_l10n_v2')
    english, _ = codec.decrypt_container(args.english, args.manifest, 'en-US')
    current, _ = codec.decrypt_container(args.payload, args.manifest, 'en-US')
    if list(english) != list(current):
        raise ValueError('Input key/order mismatch')
    rows = build_inventory(english, current)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'groups': dict(Counter(row['job'] for row in rows)),
                      'records': dict(Counter(row['job'] for row in rows for _ in row['keys'])),
                      'flags': dict(Counter(flag for row in rows for flag in row['flags']))}, indent=2))


if __name__ == '__main__':
    main()
