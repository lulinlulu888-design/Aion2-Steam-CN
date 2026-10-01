"""Audit every class; apply explicitly reviewed sentences without changing tokens.

Requires a locally supplied aion2_l10n_v2 module and legally obtained data.
Source tables, decoded text and generated payloads must stay out of Git.
"""
import argparse
from collections import Counter, defaultdict
import importlib
import json
from pathlib import Path
import re
import sys

TOKEN = re.compile(r'<[^>]*>|\{[^}]*\}|%\d*\$?[a-zA-Z]|\\[nrt]')
ALLOWED = re.compile(r'(?<![A-Za-z])(?:PvE|PvP|NPC|Lv|MAX)(?![A-Za-z])', re.I)


def visible_english(value):
    return bool(re.search(r'[A-Za-z]{3,}', ALLOWED.sub('', TOKEN.sub('', value))))


def mask(value):
    tokens = []
    def substitute(match):
        tokens.append(match[0])
        return '§' + str(len(tokens) - 1) + '§'
    return TOKEN.sub(substitute, value), tokens


def restore(template, tokens):
    indices = [int(i) for i in re.findall(r'§(\d+)§', template)]
    if sorted(indices) != list(range(len(tokens))):
        raise ValueError('Translation dropped, duplicated or introduced a token')
    return re.sub(r'§(\d+)§', lambda m: tokens[int(m[1])], template)


def translate_sentences(source, translations):
    # Translate only complete, explicitly reviewed sentences. Never word-swap prose.
    parts = re.split(r'((?<=[.!])\s+|\n+)', source)
    for index in range(0, len(parts), 2):
        template, tokens = mask(parts[index])
        if template in translations:
            parts[index] = restore(translations[template], tokens)
    return ''.join(parts)


def restore_token_case(value, source):
    """Restore casing only when the entire ordered token sequence matches."""
    actual, expected = TOKEN.findall(value), TOKEN.findall(source)
    if [t.lower() for t in actual] != [t.lower() for t in expected]:
        return value
    tokens = iter(expected)
    return TOKEN.sub(lambda _: next(tokens), value)


def audit(entries):
    rows = []
    for key, value in entries.items():
        if 'skill' not in key.lower() or not visible_english(value):
            continue
        job = re.search(r'_PC_(ASSASSIN|CHANTER|CLERIC|ELEMENTALIST|GLADIATOR|RANGER|SORCERER|TEMPLAR)_', key)
        rows.append({'key': key, 'class': job[1] if job else 'other', 'value': value})
    return rows


def main():
    parser = argparse.ArgumentParser()
    for flag in ('codec-dir', 'english', 'payload', 'manifest', 'output', 'report'):
        parser.add_argument('--' + flag, type=Path, required=True)
    args = parser.parse_args()
    if args.output.resolve() in {args.payload.resolve(), args.english.resolve(), args.manifest.resolve()}:
        raise ValueError('Output must not overwrite an input')
    sys.path.insert(0, str(args.codec_dir))
    codec = importlib.import_module('aion2_l10n_v2')
    english, _ = codec.decrypt_container(args.english, args.manifest, 'en-US')
    current, prefix = codec.decrypt_container(args.payload, args.manifest, 'en-US')
    if list(english) != list(current):
        raise ValueError('Key set/order does not match original Global data')
    translations = json.loads(Path(__file__).with_name('reviewed_skill_sentences.json').read_text(encoding='utf-8'))
    additions = json.loads(Path(__file__).with_name('reviewed_skill_additions.json').read_text(encoding='utf-8'))
    # Only exact bracketed skill names with an unambiguous same-class mapping.
    names = defaultdict(lambda: defaultdict(set))
    for key, source in english.items():
        job = re.search(r'_PC_(ASSASSIN|CHANTER|CLERIC|ELEMENTALIST|GLADIATOR|RANGER|SORCERER|TEMPLAR)_', key)
        if job and key.endswith('_skill_name') and re.search(r'[A-Za-z]', source) and not visible_english(current[key]) and re.search(r'[\u3400-\u9fff]', current[key]):
            names[job[1]][source].add(current[key])
    output = dict(current)
    changed = []
    family = re.compile(r'^SkillString_STR_SKILL_PC_CLERIC_(?:1707|1708|1716|1730|1740)\d+_(?:skill_desc_effect|specialized_skill_desc)$')
    for key, value in current.items():
        job = re.search(r'_PC_(ASSASSIN|CHANTER|CLERIC|ELEMENTALIST|GLADIATOR|RANGER|SORCERER|TEMPLAR)_', key)
        abnormal = re.match(r'^SkillAbnormalString_SkillAbnormalString_(?:171600[0-5]61|133100[45]01)_', key)
        if not job and not abnormal:
            continue
        selected = value
        if family.match(key) and visible_english(value):
            candidate = translate_sentences(english[key], translations)
            if visible_english(candidate):
                raise ValueError('Unreviewed sentence in reported skill family: ' + key)
            selected = candidate
        def reference(match):
            options = names[job[1]].get(match[1], set()) if job else set()
            return '[' + next(iter(options)) + ']' if len(options) == 1 else match[0]
        # Protect every variable and markup token before editing visible segments.
        parts = re.split('(' + TOKEN.pattern + ')', selected)
        for i in range(0, len(parts), 2):
            parts[i] = re.sub(r'\[([^\[\]]+)\]', reference, parts[i])
        selected = ''.join(parts)
        selected = translate_sentences(selected, additions)
        expected = english[key] if family.match(key) and visible_english(value) else value
        if sorted(TOKEN.findall(selected)) != sorted(TOKEN.findall(expected)):
            raise ValueError('Token mismatch: ' + key)
        selected = restore_token_case(selected, english[key])
        if sorted(TOKEN.findall(selected)) != sorted(TOKEN.findall(english[key])):
            raise ValueError('Player skill differs from Global tokens: ' + key)
        if selected != value:
            output[key] = selected
            changed.append({'key': key, 'before': value, 'after': selected})
    existing_mismatches = [key for key in english if sorted(TOKEN.findall(english[key])) != sorted(TOKEN.findall(current[key]))]
    mismatch_set = set(existing_mismatches)
    for key in english:
        if key not in mismatch_set and sorted(TOKEN.findall(english[key])) != sorted(TOKEN.findall(output[key])):
            raise ValueError('New full-table token mismatch: ' + key)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    unresolved = [r['key'] for r in audit(output) if r['class'] != 'other']
    if unresolved:
        raise ValueError('Unreviewed player skill text: ' + ', '.join(unresolved))
    codec.encrypt_container(args.output, output, args.manifest, 'en-US', prefix)
    verified, _ = codec.decrypt_container(args.output, args.manifest, 'en-US')
    if verified != output:
        raise ValueError('Container roundtrip mismatch')
    before, after = audit(current), audit(output)
    report = {
        'entries': len(output), 'changed': len(changed),
        'preexisting_token_mismatches': existing_mismatches,
        'remaining_token_mismatches': [key for key in english if sorted(TOKEN.findall(english[key])) != sorted(TOKEN.findall(output[key]))],
        'before_by_class': dict(Counter(r['class'] for r in before)),
        'remaining_by_class': dict(Counter(r['class'] for r in after)),
        'remaining': after, 'changes': changed,
        'note': 'Residual candidates require review; not a claim of complete localization or in-game verification.'
    }
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('remaining', 'changes', 'preexisting_token_mismatches', 'remaining_token_mismatches')}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
