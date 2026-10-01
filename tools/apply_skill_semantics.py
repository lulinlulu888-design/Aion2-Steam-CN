"""Apply reviewed Global-source corrections; this is not full semantic approval."""
import argparse
from collections import Counter
import importlib
import json
from pathlib import Path
import re
import sys

from audit_skill_semantics import PLAYER
from skill_text_review import TOKEN, mask, restore


def literal_numbers(value):
    return Counter(re.findall(r'\d+(?:\.\d+)?', TOKEN.sub('', value)))


def literal_numbers_match(key, source, translated):
    expected, actual = literal_numbers(source), literal_numbers(translated)
    if expected == actual:
        return True
    # Global English repeats the same restore sentence; same-build Korean
    # states it once. Full source/key gating still happens before this check.
    if key == 'SkillString_STR_SKILL_PC_CLERIC_17080130_skill_desc_effect':
        return (source.count('Restores 150 MP.') == 2
                and translated.count('恢复150精神力') == 1
                and expected == actual + Counter({'150': 1}))
    return False


def review_coverage(english, reviewed, clauses):
    """Count only explicit full-record approvals, never heuristic passes."""
    result = {}
    partial = set(clauses) - reviewed
    for key in english:
        match = PLAYER.search(key)
        if not match:
            continue
        row = result.setdefault(match[1], dict(total=0, full=0, partial=0, remaining=0))
        row['total'] += 1
        row['full' if key in reviewed else 'partial' if key in partial else 'remaining'] += 1
    return result


def apply_overrides(english, current, rules):
    output = dict(current)
    reviewed = set()
    for rule in rules:
        for key in rule['keys']:
            if key in reviewed:
                raise ValueError('Duplicate reviewed key: ' + key)
            template, tokens = mask(english[key])
            if template != rule['source']:
                raise ValueError('Reviewed source changed: ' + key)
            value = restore(rule['translation'], tokens)
            if not literal_numbers_match(key, english[key], value):
                raise ValueError('Literal number mismatch: ' + key)
            if TOKEN.sub('', value).count('%') != TOKEN.sub('', english[key]).count('%'):
                raise ValueError('Percentage unit mismatch: ' + key)
            if sorted(TOKEN.findall(value)) != sorted(TOKEN.findall(english[key])):
                raise ValueError('Token mismatch: ' + key)
            output[key] = value
            reviewed.add(key)
    # Explicitly reviewed clause, gated on both original mechanic and old wording.
    # Does NOT approve the rest of the record's meaning.
    clause_changes = []
    for key, source in english.items():
        if not PLAYER.search(key):
            continue
        if not re.search(r'Increases damage by 30%(?: and engraves 2 Insignias for .*? on landing as a Back attack| on landing as a Back attack| if attacking from behind the target)\.', source):
            continue
        before = '若从目标后方攻击，伤害增加50%'
        if before in output[key]:
            output[key] = output[key].replace(before, '若从目标后方攻击，伤害增加30%')
            clause_changes.append(key)
    changes = []
    for key, value in output.items():
        if value == current[key]:
            continue
        if sorted(TOKEN.findall(value)) != sorted(TOKEN.findall(english[key])):
            raise ValueError('Changed record differs from source tokens: ' + key)
        changes.append(dict(key=key, before=current[key], after=value))
    return output, reviewed, clause_changes, changes


def main():
    parser = argparse.ArgumentParser()
    for name in ('codec-dir', 'english', 'payload', 'manifest', 'output', 'report'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    paths = [args.english, args.payload, args.manifest, args.output, args.report]
    if len({p.resolve() for p in paths}) != len(paths):
        raise ValueError('All input/output paths must be distinct')
    sys.path.insert(0, str(args.codec_dir))
    codec = importlib.import_module('aion2_l10n_v2')
    english, _ = codec.decrypt_container(args.english, args.manifest, 'en-US')
    current, prefix = codec.decrypt_container(args.payload, args.manifest, 'en-US')
    if list(english) != list(current):
        raise ValueError('Input key/order mismatch')
    rules = json.loads(Path(__file__).with_name('reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
    output, reviewed, clauses, changes = apply_overrides(english, current, rules)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    codec.encrypt_container(args.output, output, args.manifest, 'en-US', prefix)
    verified, _ = codec.decrypt_container(args.output, args.manifest, 'en-US')
    if verified != output:
        raise ValueError('Payload roundtrip failed')
    report = dict(changed=len(changes), reviewed_keys=sorted(reviewed),
                  source_uncertainty_keys=sorted(k for k in reviewed if any(
                      note in output[k] for note in ('原文未注明比例基准', '计算基准未在原文中注明', '尚待确认'))),
                  source_placeholder_keys=sorted(k for k in reviewed if '原文占位：' in output[k]),
                  partial_clause_keys=clauses, changes=changes,
                  coverage=review_coverage(english, reviewed, clauses),
                  complete=False, note='Translation review does not resolve source uncertainties. Status consistency and release verification are still required.')
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(changed=len(changes), fully_reviewed_records=len(reviewed),
                          clause_only_records=len(clauses), complete=False)))


if __name__ == '__main__':
    main()
