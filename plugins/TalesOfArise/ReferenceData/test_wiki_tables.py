"""Synthetic parser regressions; no game files or downloaded fixture content."""

import sys
import unittest
from pathlib import Path

from bs4 import BeautifulSoup

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from wiki_tables import name, records, tables


class WikiTableTests(unittest.TestCase):
    def test_rowspanned_names_keep_explanations_and_variants_without_new_records(self):
        html = """<table><tr><th>Name</th><th>Cost</th><th>Rule</th></tr>
        <tr><td rowspan="3"><b>Example</b><br>Alias</td><td>4</td><td>Normal</td></tr>
        <tr><td>6</td><td>Alternative</td></tr>
        <tr><td colspan="2">Explanatory note</td></tr>
        <tr><td><b>Next</b></td><td>8</td><td>Other</td></tr></table>"""
        rows = list(records(BeautifulSoup(html, "html.parser").table, 0))
        self.assertEqual(["Example", "Next"], [name(row["cells"][0]) for row in rows])
        self.assertIn("6; Alternative", rows[0]["notes"][0])
        self.assertEqual("Explanatory note", rows[0]["notes"][1])
        self.assertEqual("8", rows[1]["cells"][1].get_text())

    def test_nested_navigation_tables_are_excluded(self):
        html = """<h2>Entries</h2><table><tr><th>Item Name</th></tr></table>
        <table class="navbox-table"><tr><th>Links</th><td>
        <table><tr><th>Nested links</th></tr></table></td></tr></table>"""
        found = list(tables(BeautifulSoup(html, "html.parser")))
        self.assertEqual(1, len(found))
        self.assertEqual(["Item Name"], found[0][0])

    def test_two_row_headers_do_not_offset_skill_columns(self):
        html = """<table><tr><th rowspan="2">Name</th><th colspan="2">Costs</th></tr>
        <tr><th>Base</th><th>DLC</th></tr>
        <tr><td><b>Skill</b></td><td>10</td><td>20</td></tr></table>"""
        row = list(records(BeautifulSoup(html, "html.parser").table, 0))[0]
        self.assertEqual("Skill", name(row["cells"][0]))
        self.assertEqual("20", row["cells"][2].get_text())


if __name__ == "__main__":
    unittest.main()
