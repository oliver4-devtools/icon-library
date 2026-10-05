"""
Tests for the build-time catalogue step.

Run from the project folder:   python3 -m unittest discover -s tests/build -v
Standard library only.
"""
import gzip
import json
import os
import re
import sys
import unittest

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(REPO, "build"))
import build_catalogue as bc  # noqa: E402

NS = 'xmlns="http://www.w3.org/2000/svg"'


def svg(body, size=16, extra=""):
    return f'<svg {NS} width="{size}" height="{size}" viewBox="0 0 {size} {size}" fill="none"{extra}>{body}</svg>'


class NormaliseSvgTests(unittest.TestCase):
    """BC-01..BC-12: normalise_svg turns Fluent source artwork into a Dataverse-safe icon."""

    def test_BC01_hard_coded_fill_becomes_current_color(self):
        out, problems = bc.normalise_svg(svg('<path d="M1 1h2" fill="#212121"/>'), 16)
        self.assertEqual(problems, [])
        self.assertIn('fill="currentColor"', out)
        self.assertNotRegex(out, r"#[0-9a-fA-F]{3,8}")

    def test_BC02_width_height_16_and_source_viewbox_kept_for_20px_art(self):
        out, _ = bc.normalise_svg(svg('<path d="M1 1h2" fill="#212121"/>', size=20), 20)
        self.assertIn('width="16"', out)
        self.assertIn('height="16"', out)
        self.assertIn('viewBox="0 0 20 20"', out)

    def test_BC03_missing_viewbox_defaults_to_artwork_size(self):
        src = f'<svg {NS} width="20" height="20"><path d="M1 1h2"/></svg>'
        out, _ = bc.normalise_svg(src, 20)
        self.assertIn('viewBox="0 0 20 20"', out)

    def test_BC04_script_is_a_build_error(self):
        _, problems = bc.normalise_svg(svg('<script>alert(1)</script><path d="M1 1"/>'), 16)
        self.assertTrue(any("script" in p for p in problems), problems)

    def test_BC05_style_attribute_and_event_handler_are_build_errors(self):
        _, problems = bc.normalise_svg(svg('<path d="M1 1" style="fill:red" onclick="x()"/>'), 16)
        self.assertTrue(any("style" in p for p in problems), problems)
        self.assertTrue(any("onclick" in p for p in problems), problems)

    def test_BC06_class_and_id_are_dropped(self):
        out, problems = bc.normalise_svg(svg('<path d="M1 1" class="a" id="b"/>'), 16)
        self.assertEqual(problems, [])
        self.assertNotIn("class=", out)
        self.assertNotIn("id=", out)

    def test_BC07_full_size_clip_wrapper_is_removed_and_groups_flattened(self):
        src = svg('<g clip-path="url(#c)"><path d="M1 1" fill="#212121"/></g>'
                  '<defs><clipPath id="c"><rect width="16" height="16" fill="white"/></clipPath></defs>')
        out, problems = bc.normalise_svg(src, 16)
        self.assertEqual(problems, [])
        self.assertNotIn("<g", out)
        self.assertNotIn("clip", out)
        self.assertIn("<path", out)

    def test_BC08_other_defs_are_a_build_error(self):
        _, problems = bc.normalise_svg(svg('<defs><linearGradient id="g"/></defs><path d="M1 1"/>'), 16)
        self.assertTrue(any("defs" in p for p in problems), problems)

    def test_BC09_coloured_stroke_becomes_current_color(self):
        out, problems = bc.normalise_svg(svg('<path d="M1 1" stroke="#000"/>'), 16)
        self.assertEqual(problems, [])
        self.assertIn('stroke="currentColor"', out)

    def test_BC10_fill_none_is_kept(self):
        out, _ = bc.normalise_svg(svg('<path d="M1 1" fill="none" stroke="currentColor"/>'), 16)
        self.assertIn('fill="none"', out)

    def test_BC11_empty_icon_is_a_build_error(self):
        _, problems = bc.normalise_svg(svg(""), 16)
        self.assertIn("no drawable elements", problems)

    def test_BC12_oversize_icon_is_a_build_error(self):
        big = "M1 1" + " L2 2" * 3000
        _, problems = bc.normalise_svg(svg(f'<path d="{big}"/>'), 16)
        self.assertTrue(any("exceeds 10 KB" in p for p in problems), problems)

    def test_BC13_output_is_single_line_without_xml_declaration(self):
        out, _ = bc.normalise_svg('<?xml version="1.0"?>\n' + svg('\n  <path d="M1 1"/>\n'), 16)
        self.assertTrue(out.startswith("<svg"))
        self.assertNotIn("\n", out)


class HelperTests(unittest.TestCase):
    def test_BC14_icon_id_parses_16_and_20(self):
        self.assertEqual(bc.icon_id("ic_fluent_person_add_16_regular.svg"), ("person_add", 16))
        self.assertEqual(bc.icon_id("ic_fluent_arrow_trending_wrench_20_regular.svg"), ("arrow_trending_wrench", 20))

    def test_BC15_name_terms_win_over_metaphors_and_unmatched_goes_to_other(self):
        cats = [{"id": "people", "name": {"person"}, "metaphor": {"human"}},
                {"id": "docs", "name": {"document"}, "metaphor": {"paper"}}]
        self.assertEqual(bc.categorise({"person", "add"}, {"paper"}, cats), ["people"])
        self.assertEqual(bc.categorise({"thing"}, {"paper"}, cats), ["docs"])
        self.assertEqual(bc.categorise({"thing"}, {"nothing"}, cats), ["other"])
        self.assertEqual(bc.categorise({"person", "document"}, set(), cats), ["people", "docs"])


class BuiltCatalogueTests(unittest.TestCase):
    """BC-16..BC-24: the catalogue that ships (embedded .gz) is consistent and follows the SVG rules."""

    @classmethod
    def setUpClass(cls):
        with gzip.open(os.path.join(REPO, "src", "Oliver4.IconLibrary", "Web", "catalogue.json.gz"), "rt", encoding="utf-8") as f:
            cls.cat = json.load(f)
        cls.icons = cls.cat["icons"]

    def test_BC16_readable_copy_matches_the_embedded_catalogue(self):
        path = os.path.join(REPO, "build", "out", "catalogue.json")
        if not os.path.exists(path):
            self.skipTest("build/out/catalogue.json not present (it is a build output)")
        with open(path, encoding="utf-8") as f:
            self.assertEqual(json.load(f), self.cat)

    def test_BC17_ids_and_names_unique(self):
        self.assertEqual(len({i["id"] for i in self.icons}), len(self.icons))
        self.assertEqual(len({i["name"] for i in self.icons}), len(self.icons))

    def test_BC18_every_icon_follows_the_svg_rules(self):
        bad = []
        for i in self.icons:
            s = i["svg"]
            if not s.startswith("<svg") or 'width="16"' not in s or 'height="16"' not in s or "viewBox=" not in s:
                bad.append((i["id"], "size/viewBox"))
            if re.search(r"#[0-9a-fA-F]{3,8}\b", s):
                bad.append((i["id"], "hex colour"))
            if re.search(r"<(script|style|foreignObject)\b|\son\w+=|\sstyle=|\sclass=", s, re.I):
                bad.append((i["id"], "script/style/class/event"))
            if len(s.encode("utf-8")) > 10 * 1024:
                bad.append((i["id"], "over 10 KB"))
        self.assertEqual(bad, [])

    def test_BC19_sizes_are_16_or_20(self):
        self.assertTrue(all(i["size"] in (16, 20) for i in self.icons))

    def test_BC20_every_icon_has_a_known_category(self):
        known = {c["id"] for c in self.cat["categories"]}
        self.assertTrue(all(i["categories"] and set(i["categories"]) <= known for i in self.icons))

    def test_BC21_category_counts_match(self):
        for c in self.cat["categories"]:
            self.assertEqual(c["count"], sum(1 for i in self.icons if c["id"] in i["categories"]), c["id"])

    def test_BC22_between_15_and_25_filters_with_other_last(self):
        cats = self.cat["categories"]
        self.assertTrue(15 <= len(cats) <= 25, len(cats))
        self.assertEqual(cats[-1]["id"], "other")

    def test_BC23_source_commit_recorded_with_mit_licence(self):
        self.assertEqual(self.cat["source"]["licence"], "MIT")
        self.assertRegex(self.cat["source"]["commit"], r"^[0-9a-f]{40}$")

    def test_BC24_default_web_resource_names_are_valid(self):
        # The UI suggests "<prefix>_<id>_regular.svg"; ids must only use characters a web resource name allows.
        bad = [i["id"] for i in self.icons if not re.fullmatch(r"[a-z0-9_]+", i["id"])]
        self.assertEqual(bad, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
