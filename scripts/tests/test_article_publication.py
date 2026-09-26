from pathlib import Path
import importlib.util, tempfile, unittest

spec=importlib.util.spec_from_file_location('publication',Path(__file__).parents[1]/'publish_article_results.py')
publication=importlib.util.module_from_spec(spec);spec.loader.exec_module(publication)


class PublicationTests(unittest.TestCase):
    def test_derived_exhibit_checks_both_source_and_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            source=root/'data.csv';source.write_text('original data\n')
            output=root/'table.tex';output.write_text('derived presentation\n')
            record={'Source':source.name,'Output':output.name,'Derived':True,
                    'SourceSha256':publication.sha(source),'Sha256':publication.sha(output)}
            publication.verify_main_exhibit(root,record)
            source.write_text('changed data\n')
            with self.assertRaisesRegex(ValueError,'Changed source'):
                publication.verify_main_exhibit(root,record)
            source.write_text('original data\n')
            output.write_text('changed presentation\n')
            with self.assertRaisesRegex(ValueError,'Changed numbered exhibit'):
                publication.verify_main_exhibit(root,record)

    def test_comparison_coverage_rejects_missing_complete_rule(self):
        matrix=[{'OptionSetName':risk+fee,'Transformation':'Baseline','Offers':10,'Cost':1,'Risk':risk,'FeeRule':fee}
                for risk in ['RN','RA'] for fee in ['American','Trial Fee-Shifting','Complete Fee-Shifting']]
        self.assertEqual(publication.expected_counts(matrix),
                         {'individual-results':36,'selection-offers':2,'welfare-outcomes':3,'dispositions':3})
        with self.assertRaises(ValueError):publication.expected_counts(matrix[:-1])

    def test_paths_cannot_escape_destination(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(publication.inside(directory,'Sources/file.tex'),Path(directory).resolve()/'Sources/file.tex')
            with self.assertRaises(ValueError):publication.inside(directory,'../outside.tex')

    def test_reuse_check_rejects_a_changed_strategy_even_when_reports_match(self):
        with tempfile.TemporaryDirectory() as directory:
            roots=[Path(directory)/name for name in ['before','after']]
            case={'EquilibriumFileName':'CS004 sample -equ.csv','ReportPrefix':'CS004','OptionSetName':'sample'}
            for root in roots:
                raw=root/'Run records/Retained study';raw.mkdir(parents=True)
                (raw/case['EquilibriumFileName']).write_text('0.25,0.75\n')
                (raw/'CS004 sample.csv').write_text('OptionSet,Filter,PFiles,Seconds\nsample,All,0.25,10\n')
            self.assertEqual(len(publication.compare_reused(roots[1],roots[0],[case])),1)
            (roots[1]/'Run records/Retained study'/case['EquilibriumFileName']).write_text('0.3,0.7\n')
            with self.assertRaises(ValueError):publication.compare_reused(roots[1],roots[0],[case])


if __name__=='__main__':unittest.main()
