import unittest
from pinyin_tone_coverage import highlight


class CoverageTests(unittest.TestCase):
    def test_initials_require_exact_initial_not_prefix(self):
        for label, base in [('c', 'chi'), ('s', 'shi'), ('z', 'zhi'), ('n', 'ang')]:
            self.assertIsNone(highlight(dict(group='initial', display=label), base))
        self.assertEqual((0, 2), highlight(dict(group='initial', display='ch'), 'chang'))

    def test_finals_match_phonetics_and_visible_spelling(self):
        for label, base, expected in [('üe', 'xue', (1, 2)), ('ün', 'yun', (1, 2)),
                                      ('ü', 'lü', (1, 1)), ('ui', 'hui', (1, 2)),
                                      ('i', 'chi', None), ('u', 'yu', None),
                                      ('un', 'yun', None), ('in', 'ying', None),
                                      ('an', 'ang', None), ('un', 'wen', None),
                                      ('o', 'wo', None), ('ang', 'yang', None)]:
            self.assertEqual(expected, highlight(dict(group='compound', display=label), base))

    def test_whole_syllables_cannot_use_other_syllables_of_same_initial(self):
        self.assertIsNone(highlight(dict(group='whole', display='chi'), 'chang'))
        self.assertEqual((0, 3), highlight(dict(group='whole', display='chi'), 'chi'))


if __name__ == '__main__':
    unittest.main()
