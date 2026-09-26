"""Tests of comparison direction, cost selection and matched-case integrity."""
import csv
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_fee_rule_comparisons as comparisons


class FeeRuleComparisonTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.article = Path(self.temporary.name)
        self.source = self.article / "Results/Aggregated Data/Sources/welfare-outcomes.csv"
        self.output = self.article / "tables"
        self.rows = []
        # Five ordinary settings plus a fifteen-offer setting available only at cost 1.
        settings = [("baseline", cost, "10") for cost in ("0.25", "0.5", "1", "2", "4")]
        settings.append(("baseline-offers-15", "1", "15"))
        for family, cost, offers in settings:
            for risk in comparisons.RISKS:
                for index, rule in enumerate(comparisons.RULES):
                    row = {key: "same" for key in comparisons.TEXT_SETTINGS}
                    row.update({key: "1" for key in comparisons.NUMBER_SETTINGS})
                    row.update({
                        "Comparison Family": family, "Costs Multiplier": cost,
                        "Risk Aversion": risk, "Comparison Fee Rule": rule,
                        "OptionSetName": f"{family}/{cost}/{risk}/{rule}", "Filter": "All",
                        "Number of Offers": offers, "Probability Truly Liable": "0.5",
                        "CARA Alpha": "0" if risk == comparisons.RISKS[0] else "2",
                    })
                    # Removing extreme costs changes the balance of comparison signs.
                    values = ("0.8", "0.2", "0.3") if cost in ("0.25", "4") else ("0.2", "0.3", "0.1")
                    if offers == "15":
                        values = ("0.5", "0.4", "0.6")
                    row.update({column: values[index] for _, column, _ in comparisons.METRICS})
                    self.rows.append(row)
        self.write_source(self.rows)

    def write_source(self, rows, headers=None):
        self.source.parent.mkdir(parents=True, exist_ok=True)
        with self.source.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=headers or sorted(comparisons.REQUIRED))
            writer.writeheader()
            writer.writerows(rows)

    def test_direction_and_inclusive_tie_tolerance(self):
        tolerance = Decimal("0.00001")
        for value in ("-0.00001", "0", "0.00001"):
            self.assertEqual(comparisons.direction(Decimal(value), tolerance), "Tied")
        self.assertEqual(comparisons.direction(Decimal("-0.000010000000001"), tolerance), "Lower")
        self.assertEqual(comparisons.direction(Decimal("0.000010000000001"), tolerance), "Higher")

    def test_numeric_cost_filter_retains_fifteen_offer_case_and_expected_counts(self):
        cases = comparisons.read_cases(self.source)
        selected = comparisons.filter_costs(cases, {Decimal("0.25"), Decimal("4.0")})
        self.assertEqual(len(cases), 36)
        self.assertEqual(len(selected), 24)
        self.assertEqual({c.cost for c in selected}, {Decimal("0.5"), Decimal("1"), Decimal("2")})
        self.assertEqual(sum(c.family == "baseline-offers-15" for c in selected), 6)
        for view, expected in ((cases, (3, 0, 3, 6)), (selected, (1, 0, 3, 4))):
            counts = comparisons.summarize(comparisons.compare(view, comparisons.DEFAULT_TOLERANCE))
            for row in counts:
                if (row["first_fee_rule"], row["second_fee_rule"]) == comparisons.PAIRS[0]:
                    self.assertEqual(tuple(row[k] for k in ("Lower", "Tied", "Higher", "matched_settings")), expected)

    def test_second_minus_first_values_are_not_rounded_before_comparison(self):
        for row in self.rows:
            if row["Comparison Family"] == "baseline" and row["Costs Multiplier"] == "1":
                row["Plaintiff shortfall contribution"] = {
                    "American": "0.1234567891234567",
                    "Trial Fee-Shifting": "0.1234467891234566",
                    "Complete Fee-Shifting": "0.1234467891234567",
                }[row["Comparison Fee Rule"]]
        self.write_source(self.rows)
        records = comparisons.compare(comparisons.read_cases(self.source), comparisons.DEFAULT_TOLERANCE)
        records = [r for r in records if r["model_family"] == "baseline" and r["cost_multiplier"] == "1"
                   and r["source_column"] == "Plaintiff shortfall contribution" and r["first_fee_rule"] == "American"]
        self.assertEqual(len(records), 4)
        for row in records:
            expected = ("Lower", "-0.0000100000000001") if row["second_fee_rule"] == "Trial Fee-Shifting" else ("Tied", "-0.0000100000000000")
            self.assertEqual(row["direction"], expected[0])
            self.assertEqual(Decimal(row["second_minus_first"]), Decimal(expected[1]))

    def test_missing_fee_rule_and_missing_preference_are_rejected(self):
        for rows, message in (
            (self.rows[:-1], "Incomplete fee-rule comparison"),
            ([r for r in self.rows if not (r["Comparison Family"] == "baseline-offers-15"
                                          and r["Risk Aversion"] == comparisons.RISKS[1])], "Incomplete preference coverage"),
        ):
            with self.subTest(message=message):
                self.write_source(rows)
                with self.assertRaisesRegex(ValueError, message):
                    comparisons.read_cases(self.source)

    def test_duplicate_option_sets_and_duplicate_rule_cells_are_rejected(self):
        for unique_option, message in ((False, "Duplicate option set"), (True, "Duplicate fee-rule cell")):
            with self.subTest(message=message):
                duplicate = dict(self.rows[0])
                if unique_option:
                    duplicate["OptionSetName"] += "-duplicate"
                self.write_source(self.rows + [duplicate])
                with self.assertRaisesRegex(ValueError, message):
                    comparisons.read_cases(self.source)

    def test_fee_comparisons_cannot_mix_other_settings(self):
        self.rows[1]["Number of Offers"] = "15"
        self.write_source(self.rows)
        with self.assertRaisesRegex(ValueError, "Non-fee settings differ"):
            comparisons.read_cases(self.source)

    def test_invalid_values_and_wrong_population_are_rejected(self):
        for field, value, message in (
            ("Real Litigation Costs", "NaN", "Nonfinite"),
            ("Real Litigation Costs", "infinity", "Nonfinite"),
            ("Real Litigation Costs", "unknown", "Invalid numeric"),
            ("P Files", "1.01", "outside"),
            ("D Answers", "-0.01", "outside"),
            ("D Answers", "0.9", "Answering share exceeds filing share"),
            ("Probability Truly Liable", "0", "Truth prior"),
            ("Costs Multiplier", "0", "must be positive"),
            ("Filter", "Truly Liable", "Filter=All"),
            ("Risk Aversion", "Unknown", "Unknown risk"),
            ("Real Litigation Costs", "", "Missing values"),
        ):
            with self.subTest(field=field, value=value):
                rows = [dict(r) for r in self.rows]
                rows[0][field] = value
                self.write_source(rows)
                with self.assertRaisesRegex(ValueError, message):
                    comparisons.read_cases(self.source)

    def test_missing_source_column_is_rejected(self):
        rows = [{k: v for k, v in row.items() if k != "D Answers"} for row in self.rows]
        self.write_source(rows, sorted(comparisons.REQUIRED - {"D Answers"}))
        with self.assertRaisesRegex(ValueError, "Missing source columns: D Answers"):
            comparisons.read_cases(self.source)

    def test_answering_share_uses_all_potential_disputes(self):
        # Answering rises from .3 to .4 even though conditional answering falls
        # from .75 to .5 and nonanswer rises from .1 to .4.
        for row in self.rows:
            if row['Comparison Family'] == 'baseline' and row['Costs Multiplier'] == '1':
                filing, answering = {'American': ('0.4', '0.3'),
                                     'Trial Fee-Shifting': ('0.8', '0.4'),
                                     'Complete Fee-Shifting': ('0.8', '0.8')}[row['Comparison Fee Rule']]
                row['P Files'], row['D Answers'] = filing, answering
        self.write_source(self.rows)
        records = comparisons.compare(comparisons.read_cases(self.source), comparisons.DEFAULT_TOLERANCE)
        answers = [r for r in records if r['model_family'] == 'baseline'
                   and r['cost_multiplier'] == '1' and r['outcome'] == 'Answering share'
                   and (r['first_fee_rule'], r['second_fee_rule']) == comparisons.PAIRS[0]]
        self.assertEqual(len(answers), 2)
        for row in answers:
            self.assertEqual(row['source_column'], 'D Answers')
            self.assertEqual(row['first_value'], '0.3')
            self.assertEqual(row['second_value'], '0.4')
            self.assertEqual(row['second_minus_first'], '0.1')
            self.assertEqual(row['direction'], 'Higher')

    def test_invalid_selection_or_tolerance_writes_no_outputs(self):
        for excluded, tolerance in (
            ({Decimal("9")}, comparisons.DEFAULT_TOLERANCE),
            ({Decimal(c) for c in ("0.25", "0.5", "1", "2", "4")}, comparisons.DEFAULT_TOLERANCE),
            ({Decimal("4")}, Decimal("-1")),
        ):
            with self.subTest(excluded=excluded, tolerance=tolerance):
                with self.assertRaises(ValueError):
                    comparisons.build(self.article, self.output, excluded, tolerance)
                self.assertFalse(self.output.exists())

    def test_both_bundles_are_reproducible_and_preserve_source(self):
        source_bytes = self.source.read_bytes()
        arguments = (self.article, self.output, {Decimal("0.25"), Decimal("4.0")}, comparisons.DEFAULT_TOLERANCE)
        totals = comparisons.build(*arguments)
        original = {path.name: path.read_bytes() for path in self.output.iterdir()}
        self.assertEqual(len(original), 10)
        self.assertEqual(totals["all-costs"]["metric_comparisons"], 252)
        self.assertEqual(totals["filtered-costs"]["metric_comparisons"], 168)
        manifest = json.loads((self.output / "filtered-costs-provenance.json").read_text())
        self.assertEqual(manifest["included_costs"], ["0.5", "1", "2"])
        self.assertEqual(manifest["excluded_costs"], ["0.25", "4"])
        self.assertEqual(manifest["matched_settings_by_risk"], {risk: 4 for risk in comparisons.RISKS})
        self.assertEqual(manifest["source"]["sha256"], hashlib.sha256(source_bytes).hexdigest())
        self.assertIn("1 / 0 / 3", (self.output / "filtered-costs.md").read_text())
        comparisons.build(*arguments)
        self.assertEqual(original, {path.name: path.read_bytes() for path in self.output.iterdir()})
        self.assertEqual(self.source.read_bytes(), source_bytes)

    def test_case_details_separate_reversals_ties_and_balanced_directions(self):
        risk, first, second = comparisons.RISKS[0], *comparisons.PAIRS[2]
        rows = [{"risk_preferences": risk, "first_fee_rule": first, "second_fee_rule": second,
                 "outcome": "Shortfall", "model_family": family, "cost_multiplier": "1",
                 "first_value": "0.2", "second_value": value, "second_minus_first": delta,
                 "direction": direction}
                for family, value, delta, direction in (
                    ("up_one", "0.3", "0.1", "Higher"),
                    ("up_two", "0.4", "0.2", "Higher"),
                    ("reversal", "0.1", "-0.1", "Lower"),
                    ("near_tie", "0.200001", "0.000001", "Tied"))]
        count = {"risk_preferences": risk, "first_fee_rule": first, "second_fee_rule": second,
                 "outcome": "Shortfall", "Lower": 1, "Tied": 1, "Higher": 2}
        report = comparisons.case_details_markdown("Cases", rows, [count], comparisons.DEFAULT_TOLERANCE)
        self.assertIn("| reversal | 1 | 0.2 | 0.1 | -0.1 | Lower |", report)
        self.assertIn("| near_tie | 1 | 0.000001 |", report)
        self.assertNotIn("up_one", report)
        self.assertNotIn("up_two", report)
        balanced = comparisons.case_details_markdown("Cases", rows[1:], [{**count, "Higher": 1}], comparisons.DEFAULT_TOLERANCE)
        self.assertIn("No dominant strict direction.", balanced)
        self.assertIn("up_two", balanced)
        self.assertIn("reversal", balanced)


if __name__ == "__main__":
    unittest.main()
