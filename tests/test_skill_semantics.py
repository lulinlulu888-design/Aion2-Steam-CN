import json
from pathlib import Path
import re
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from apply_skill_semantics import apply_overrides, review_coverage
from skill_text_review import restore, visible_english


class SemanticTests(unittest.TestCase):
    def test_multilingual_source_errata(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        by_key = {k: r['translation'] for r in rules for k in r['keys']}
        tree = by_key['SkillString_STR_SKILL_PC_SORCERER_15140020_skill_desc_effect']
        self.assertIn('目标受到3次攻击时解除变树', tree)
        self.assertNotIn('施法者受到3次', tree)
        predation = by_key['SkillString_STR_SKILL_PC_GLADIATOR_11340050_skill_desc_effect']
        self.assertIn('吸收该伤害的§10§%作为生命力', predation)
        self.assertIn('§4§%（原文未注明比例基准）', predation)
        self.assertEqual(by_key['SkillString_STR_SKILL_PC_ELEMENTALIST_103520_specialized_skill_desc'], '神圣力充能量变为2倍')

    def test_duplicate_mp_erratum_is_key_and_number_scoped(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        key = 'SkillString_STR_SKILL_PC_CLERIC_17080130_skill_desc_effect'
        rule = next(r for r in rules if key in r['keys'])
        tokens = ['{test:' + str(i) + '}' for i in range(len(re.findall(r'§(\d+)§', rule['source'])))]
        source = restore(rule['source'], tokens)
        value = apply_overrides({key: source}, {key: '旧描述'}, [rule])[0][key]
        self.assertEqual(value.count('恢复150精神力'), 1)
        changed = dict(rule, translation=rule['translation'].replace('150', '300'))
        with self.assertRaisesRegex(ValueError, 'Literal number mismatch'):
            apply_overrides({key: source}, {key: '旧描述'}, [changed])
        other = key.replace('17080130', '17080131')
        with self.assertRaisesRegex(ValueError, 'Literal number mismatch'):
            apply_overrides({other: source}, {other: '旧描述'}, [dict(rule, keys=[other])])

    def test_percentage_cannot_be_added_to_flat_stat(self):
        key = 'SkillString_STR_SKILL_PC_ELEMENTALIST_16010000_specialized_skill_desc'
        rule = dict(source='+100 Skill Accuracy', translation='技能命中提高100%', keys=[key])
        with self.assertRaisesRegex(ValueError, 'Percentage unit mismatch'):
            apply_overrides({key: rule['source']}, {key: '旧描述'}, [rule])

    def test_coverage_does_not_approve_partial_or_unreviewed(self):
        keys = ['SkillString_STR_SKILL_PC_ASSASSIN_' + str(i) + '_skill_desc_effect' for i in range(3)]
        result = review_coverage(dict.fromkeys(keys, 'test'), {keys[0]}, [keys[0], keys[1]])
        self.assertEqual(result['ASSASSIN'], dict(total=3, full=1, partial=1, remaining=1))

    def test_duplicate_review_is_rejected(self):
        key = 'SkillString_STR_SKILL_PC_ASSASSIN_13180030_specialized_skill_desc'
        rule = dict(source='Available in combat', translation='可在战斗中使用', keys=[key])
        with self.assertRaisesRegex(ValueError, 'Duplicate reviewed key'):
            apply_overrides({key: rule['source']}, {key: '旧描述'}, [rule, rule])

    def test_mechanic_regressions(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        by_source = {rule['source']: rule['translation'] for rule in rules}
        self.assertEqual(by_source['Available in combat'], '可在战斗中使用')
        self.assertEqual(by_source['+1 consecutive use'], '连续使用次数增加1次')
        self.assertNotIn('增加1.5倍', by_source['x1.5 [Assault Stance] effect for the duration'])
        for source, translation in by_source.items():
            if 'if the caster has Precision' in source:
                self.assertIn('施法者处于[精准]状态', translation)
                self.assertIn('35%', translation)
                self.assertNotIn('标靶', translation)

    def test_gladiator_linked_skill_and_status_regressions(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        by_source = {rule['source']: rule['translation'] for rule in rules}
        self.assertEqual(by_source['x1.5 [Blood Absorption] effect for the duration'],
                         '持续期间，[血之吸收]的效果变为1.5倍')
        self.assertEqual(by_source['Shrink'], '萎缩')
        linked = [r for r in rules if 'Wounded does not stack with the Shrink effect' in r['source']]
        self.assertTrue(linked)
        for rule in linked:
            with self.subTest(key=rule['keys'][0]):
                self.assertIn('受伤效果不与守护星技能[挑衅]的萎缩效果叠加', rule['translation'])
                self.assertIn('内对该目标使用[下凿击]', rule['translation'])
                self.assertNotIn('缩小', rule['translation'])
        for rule in rules:
            if any('_GLADIATOR_' in key for key in rule['keys']):
                if 'Deals 100% more damage on attacking a target with Incapacitated Immunity.' in rule['source']:
                    self.assertIn('伤害提高100%', rule['translation'])

    def test_gladiator_malformed_source_variable_roles(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        by_key = {key: rule for rule in rules for key in rule['keys']}
        rule = by_key['SkillString_STR_SKILL_PC_GLADIATOR_11410030_skill_desc_effect']
        tokens = ['<chat_combat>', '{se:1141000011:effect_value02:time}', '</>',
                  '{se_dmg:1141000711:SkillUIMaxDmg}', '</>', '<chat_combat>',
                  '{se_dmg:1141000711:SkillUIMinDmg}', '{se:1141000811:effect_value14:divide100}']
        rendered = restore(rule['translation'], tokens)
        self.assertIn('造成<chat_combat>{se_dmg:1141000711:SkillUIMinDmg}-{se_dmg:1141000711:SkillUIMaxDmg}</>持续伤害', rendered)
        self.assertIn('持续<chat_combat>{se:1141000011:effect_value02:time}</>', rendered)
        menace = by_key['SkillString_STR_SKILL_PC_GLADIATOR_11800000_skill_desc_effect']['translation']
        self.assertIn('一层杀气，持续§0§', menace)
        self.assertIn('施法者的暴击伤害增幅', menace)
        self.assertNotIn('威胁值', menace)

    def test_delayed_damage_uses_source_time_token(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        matching = [r for r in rules if 'Deals §5§§6§-§7§§8§ delayed damage after §9§.' in r['source']
                    and 'Fire Mark' in r['source'] and 'Absorbs §4§% HP' in r['source']]
        self.assertTrue(matching)
        for rule in matching:
            with self.subTest(key=rule['keys'][0]):
                self.assertIn('在§9§后造成§5§§6§-§7§§8§延迟伤害', rule['translation'])
                self.assertIn('吸收伤害的§4§%作为生命力', rule['translation'])

    def test_prepare_for_battle_keeps_effect_durations_separate(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        key = 'SkillString_STR_SKILL_PC_GLADIATOR_11191450_skill_desc_effect'
        matching = [r for r in rules if key in r['keys']]
        self.assertEqual(len(matching), 1)
        translated = matching[0]['translation']
        self.assertIn('PvP伤害抗性提高§5§%，持续§7§', translated)
        self.assertIn('获得战斗准备效果，持续§6§', translated)

    def test_templar_mp_restore_is_not_probability_gated(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        matching = [r for r in rules if 'Restores 200 MP with a 50% chance to trigger [Debilitating Smash] on hit.' in r['source']]
        self.assertTrue(matching)
        for rule in matching:
            self.assertIn('命中时恢复200精神力。命中时有50%的几率启用[衰弱猛击]。', rule['translation'])
            self.assertIn('施放时启用[审判]，可用时间为', rule['translation'])

    def test_shield_back_attack_and_fury_direction(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        by_source = {rule['source']: rule['translation'] for rule in rules}
        self.assertEqual(by_source['No Back damage occurs'], '受到来自背后的伤害时，不触发背击')
        self.assertEqual(by_source['x1.5 [Fury] effect for the duration'], '持续期间，[激昂]效果变为1.5倍')
        self.assertEqual(by_source['Restores §0§% HP once on Block'], '格挡时恢复§0§%生命力，仅触发一次')

    def test_reviewed_stat_names_match_client_labels(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        # Verified against String_StatName_* in the current client payload.
        labels = {'Double Chance': '强击', 'Endurance': '铁壁',
                  'Parry': '武器防御', 'Heal Boost': '治愈增幅',
                  'Impact-type Chance': '冲击系命中',
                  'Status Effect Chance': '异常状态命中'}
        for rule in rules:
            for source, translated in labels.items():
                if source in rule['source']:
                    with self.subTest(key=rule['keys'][0], stat=source):
                        self.assertIn(translated, rule['translation'])

    def test_reviewed_rules(self):
        rules = json.loads((Path(__file__).resolve().parents[1] / 'tools/reviewed_semantic_overrides.json').read_text(encoding='utf-8'))
        for rule in rules:
            with self.subTest(key=rule['keys'][0]):
                indices = re.findall(r'§(\d+)§', rule['source'])
                tokens = ['{test:' + str(i) + '}' for i in range(len(indices))]
                source = restore(rule['source'], tokens)
                english = dict.fromkeys(rule['keys'], source)
                current = dict.fromkeys(rule['keys'], '旧描述')
                output, reviewed, clauses, _ = apply_overrides(english, current, [rule])
                self.assertEqual(len(reviewed), len(rule['keys']))
                self.assertFalse(visible_english(next(iter(output.values()))))
                changed_source = {k: v + ' changed' for k, v in english.items()}
                with self.assertRaises(ValueError):
                    apply_overrides(changed_source, current, [rule])

    def test_back_attack_clause_requires_matching_source(self):
        key = 'SkillString_STR_SKILL_PC_ASSASSIN_13060000_skill_desc_effect'
        current = {key: '若从目标后方攻击，伤害增加50%。其他效果50%。'}
        english = {key: 'Increases damage by 30% on landing as a Back attack.'}
        output, _, clauses, _ = apply_overrides(english, current, [])
        self.assertEqual(output[key], '若从目标后方攻击，伤害增加30%。其他效果50%。')
        self.assertEqual(clauses, [key])
        self.assertEqual(apply_overrides({key: 'Damage increases by 50%.'}, current, [])[0], current)


if __name__ == '__main__':
    unittest.main()
