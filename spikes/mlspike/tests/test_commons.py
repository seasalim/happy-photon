import unittest

from common import licence_name
from commons_candidates import parse_pages


class CommonsTests(unittest.TestCase):
    def response(self):
        return {"query": {"pages": {"7": {"pageid": 7, "title": "File:Photo.jpg",
            "imageinfo": [{"width": 3000, "height": 2000,
                          "url": "https://upload.wikimedia.org/photo.jpg",
                          "descriptionurl": "https://commons.wikimedia.org/wiki/File:Photo.jpg",
                          "extmetadata": {"LicenseShortName": {"value": "CC BY-SA 4.0"},
                                          "Artist": {"value": "<a>Test &amp; Author</a>"}}}]}}}}

    def test_candidates_keep_attribution_and_require_reviewed_tags(self):
        sample, = parse_pages(self.response(), "portrait")
        self.assertEqual("Test & Author", sample["author"])
        self.assertEqual("CC-BY-SA-4.0", sample["licence"])
        self.assertEqual([], sample["tags"])
        self.assertEqual("commons-7", sample["id"])

    def test_noncommercial_small_and_unknown_creator_are_filtered(self):
        response = self.response()
        info = response["query"]["pages"]["7"]["imageinfo"][0]
        info["extmetadata"]["LicenseShortName"]["value"] = "CC BY-NC 4.0"
        self.assertEqual([], parse_pages(response, "portrait"))
        info["extmetadata"]["LicenseShortName"]["value"] = "CC0"
        info["width"] = 2399
        self.assertEqual([], parse_pages(response, "portrait"))
        info["width"] = 2400
        info["extmetadata"]["Artist"]["value"] = ""
        self.assertEqual([], parse_pages(response, "portrait"))

    def test_licence_normalization_never_admits_nc_or_nd(self):
        self.assertEqual("CC-BY-2.0", licence_name("http://creativecommons.org/licenses/by/2.0/"))
        self.assertEqual("US-Government", licence_name("United States Government Work"))
        self.assertEqual("Public-domain", licence_name("Public domain"))
        for value in ("CC BY-ND 4.0", "CC BY-NC-SA 4.0", "unknown", "CC BY"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                licence_name(value)


if __name__ == "__main__":
    unittest.main()
