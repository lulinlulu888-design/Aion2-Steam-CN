import json
from pathlib import Path
import re
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from skill_text_review import mask, restore, restore_token_case, translate_sentences, visible_english

class SkillTextTests(unittest.TestCase):
    def test_restore_case_does_not_change_variables(self):
        self.assertEqual(restore_token_case('<unique>测试</>', '<Unique>text</>'), '<Unique>测试</>')
        self.assertEqual(restore_token_case('{value:1}', '{value:2}'), '{value:1}')

    def test_markup_is_not_visible_english(self):
        self.assertFalse(visible_english('<chat_combat>{se_dmg:123:SkillUIMaxDmgSum}</>伤害 PvE/PvP NPC MAX'))
        self.assertTrue(visible_english('Restores 生命力 every 2秒'))

    def test_tokens_roundtrip_and_reordering(self):
        source = '<chat_combat>{damage}</>每隔{time}'
        template, tokens = mask(source)
        self.assertEqual(restore(template, tokens), source)
        self.assertEqual(restore('每隔§3§造成§0§§1§§2§', tokens), '每隔{time}造成<chat_combat>{damage}</>')
        for invalid in ('§0§', '§0§§1§§2§§2§', '§0§§1§§2§§4§'):
            with self.assertRaises(ValueError): restore(invalid, tokens)

    def test_all_reviewed_sentences_preserve_tokens_and_literals(self):
        path = Path(__file__).resolve().parents[1] / 'tools/reviewed_skill_sentences.json'
        mappings = json.loads(path.read_text(encoding='utf-8'))
        mappings.update(json.loads(path.with_name('reviewed_skill_additions.json').read_text(encoding='utf-8')))
        for source, translated in mappings.items():
            with self.subTest(source=source):
                indices = re.findall(r'§(\d+)§', source)
                self.assertEqual(sorted(indices), sorted(re.findall(r'§(\d+)§', translated)))
                literal = lambda value: sorted(re.findall(r'\d+(?:\.\d+)?', re.sub(r'§\d+§', '', value)))
                self.assertEqual(literal(source), literal(translated))
                self.assertFalse(visible_english(translated))
                tokens = ['{test:' + i + '}' for i in indices]
                original = restore(source, tokens)
                self.assertEqual(translate_sentences(original, mappings), restore(translated, tokens))

if __name__ == '__main__': unittest.main()
