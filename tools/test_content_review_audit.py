import copy
from pathlib import Path
import tempfile
import unittest
from content_review_audit import apply_decisions, collect, demo_payload, digest
from pinyin_definition_annotations import annotate, load_readings


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / 'evidence.txt').write_text('Synthetic test evidence, not a product review.')
        self.subject = dict(subjectId='test', sha256='a' * 64, reviews={'pinyin': 'NOT RUN'})
        self.record = dict(subjectId='test', sha256='a' * 64, dimension='pinyin', method='human',
                           reviewer='Synthetic fixture', date='2026-01-01', evidence='evidence.txt', status='PASS')

    def tearDown(self):
        self.temp.cleanup()

    def test_valid_and_stale(self):
        self.assertEqual([], apply_decisions([self.subject], [self.record], self.root))
        self.assertEqual('PASS', self.subject['reviews']['pinyin'])
        self.record['sha256'] = 'b' * 64
        self.assertEqual('STALE_DECISION', apply_decisions([self.subject], [self.record], self.root)[0]['code'])
        self.assertEqual('BLOCKED', self.subject['reviews']['pinyin'])

    def test_bad_then_valid_cannot_clear_block(self):
        for changes in ({'sha256': 'old'}, {'evidence': 'missing'}, {}):
            subject = copy.deepcopy(self.subject)
            self.assertTrue(apply_decisions([subject], [self.record | changes, self.record], self.root))
            self.assertEqual('BLOCKED', subject['reviews']['pinyin'])

    def test_invalid_evidence_and_identity(self):
        for changes in ({'evidence': '../outside.txt'}, {'evidence': ''}, {'reviewer': 'AI'},
                        {'method': 'automatic'}, {'date': '2999-01-01'}, {'status': 'approved'},
                        {'dimension': 'unknown'}, {'subjectId': 'unknown'}):
            self.assertTrue(apply_decisions([copy.deepcopy(self.subject)], [self.record | changes], self.root))

    def test_malformed_records_are_blocked(self):
        for records in ({}, None, [None], [dict(self.record, reviewer=2)], [dict(self.record, dimension=[])]):
            self.assertTrue(apply_decisions([copy.deepcopy(self.subject)], records, self.root))

    def test_demo_approval_binds_text_and_all_audio(self):
        item = dict(demoAudioKey='a', examples=[dict(contentId='c', audioKey='b', wordAudioKey='w')])
        course = dict(contents=[dict(id='c', text='old')], assets=[dict(key=k, sha256='old') for k in 'abw'])
        original = digest(demo_payload(item, course))
        for index in range(3):
            changed = copy.deepcopy(course); changed['assets'][index]['sha256'] = 'new'
            self.assertNotEqual(original, digest(demo_payload(item, changed)))
        course['contents'][0]['text'] = 'new'
        self.assertNotEqual(original, digest(demo_payload(item, course)))

    def test_actual_payload_and_pending_reviews(self):
        report = collect(decisions=[])
        self.assertEqual(398, report['summary']['contents'])
        self.assertEqual(394, report['summary']['audio'])
        self.assertEqual(75, sum(s['category'] == 'dictionary-example' for s in report['subjects']))
        self.assertEqual(1, sum(s['category'] == 'dictionary-annotation' for s in report['subjects']))
        self.assertFalse(report['summary']['releaseReady'])
        coverage = report['summary']['dictionaryCoverage']
        self.assertEqual(292114, coverage['entries'])
        self.assertEqual(293, coverage['batches'])
        self.assertEqual(0, coverage['fullyReviewedEntries'])
        self.assertEqual(292114, sum(b['entries'] for b in report['dictionaryBatches']))
        self.assertIn('DICTIONARY_RIGHTS_UNRESOLVED', report['summary']['findingsByCode'])
        words = [r for r in report['units'] if r['collection'] != 'pinyin' and r['role'] == 'definition']
        self.assertEqual(18, len(words))
        self.assertTrue(all(r['english'] and r['japanese'] and r['missingHanzi'] == 0 for r in words))
        self.assertNotIn('TAUTOLOGICAL_DEFINITION', report['summary']['findingsByCode'])
        self.assertNotIn('ANNOTATION_MISSING', report['summary']['findingsByCode'])

    def test_annotation_records_reject_text_drift_even_at_same_length(self):
        from content_review_audit import ROOT
        import json
        course = json.loads((ROOT / 'HanMate.App/Resources/Raw/Pinyin/course.json').read_text(encoding='utf-8'))
        records = load_readings()
        for change in ('text', 'count', 'missing', 'unused'):
            changed = copy.deepcopy(records)
            key = next(iter(changed)); text, readings = changed[key]
            if change == 'text': changed[key] = ('错' + text[1:], readings)
            elif change == 'count': changed[key] = (text, readings[:-1])
            elif change == 'missing': del changed[key]
            else: changed['unused'] = ('字', ['zi4'])
            with self.assertRaises((ValueError, KeyError)):
                annotate(copy.deepcopy(course), changed)

    def test_invalid_or_duplicate_reading_sources_are_rejected(self):
        for data in ('x;字;zi4\nx;字;zi4', 'x;字;zi5', 'x;字;zi', 'x;字;'):
            path = self.root / 'readings.tsv'; path.write_text(data, encoding='utf-8')
            with self.assertRaises(ValueError): load_readings(path)

    def test_dictionary_batches_cover_tail_and_bind_every_member(self):
        from dictionary_review_batches import batches
        rows = [('id' + str(i), str(i), '{"text":' + str(i) + '}') for i in range(5)]
        groups = list(batches(rows, 2))
        self.assertEqual([2, 2, 1], [len(g) for g in groups])
        self.assertEqual([r[0] for r in rows], [r['id'] for g in groups for r in g])
        changed = rows.copy(); changed[1] = (rows[1][0], rows[1][1], '{"text":"changed"}')
        self.assertNotEqual(groups[0][1]['sha256'], list(batches(changed, 2))[0][1]['sha256'])

    def test_sample_review_cannot_approve_whole_dictionary_batch(self):
        subject = dict(self.subject, category='dictionary-batch', entries=1000)
        for changes in ({}, {'allEntriesReviewed': True, 'reviewedEntries': 20},
                        {'allEntriesReviewed': False, 'reviewedEntries': 1000}):
            candidate = copy.deepcopy(subject)
            issues = apply_decisions([candidate], [self.record | changes], self.root)
            self.assertEqual('INCOMPLETE_BATCH_REVIEW', issues[0]['code'])
            self.assertEqual('BLOCKED', candidate['reviews']['pinyin'])
        self.assertEqual([], apply_decisions([subject],
            [self.record | dict(allEntriesReviewed=True, reviewedEntries=1000)], self.root))


if __name__ == '__main__':
    unittest.main()
