import unittest

from coco_attribution import validate_snapshot
from resolve_flickr_authors import parse_owner, photo_id


class AttributionTests(unittest.TestCase):
    def test_photo_ids_from_coco_static_urls_and_flickr_pages(self):
        urls = ["http://farm4.staticflickr.com/42/12345_abcdef.jpg",
                "https://live.staticflickr.com/42/12345_abcdef_z.jpg?tracking=1",
                "https://www.flickr.com/photo.gne?id=12345",
                "https://www.flickr.com/photo.gne?rb=1&id=12345",
                "https://flickr.com/photos/owner/12345/"]
        for url in urls:
            with self.subTest(url=url):
                self.assertEqual("12345", photo_id(url))
        for url in ("https://example.test/12345_file.jpg",
                    "https://www.flickr.com/photo.gne?id=abc", ""):
            with self.subTest(url=url), self.assertRaises(ValueError):
                photo_id(url)

    def test_owner_redirect_becomes_canonical_attribution(self):
        self.assertEqual({"author": "photographer", "attribution_url":
                          "https://www.flickr.com/photos/photographer/12345/"},
                         parse_owner("https://flickr.com/photos/photographer/12345/?tracking=1", "12345"))
        self.assertEqual("123@N45", parse_owner(
            "https://www.flickr.com/photos/123@N45/12345/", "12345")["author"])

    def test_deleted_bootstrap_login_and_wrong_photo_have_no_owner(self):
        for url in ("https://www.flickr.com/photos///",
                    "https://www.flickr.com/photo.gne?rb=1&id=12345",
                    "https://www.flickr.com/signin",
                    "https://www.flickr.com/photos/owner/99999/",
                    "https://www.flickr.com/photos/%20/12345/",
                    "https://www.flickr.com/photos/%2F/12345/",
                    "https://example.test/photos/owner/12345/"):
            with self.subTest(url=url):
                self.assertEqual({"unresolved"}, set(parse_owner(url, "12345")))

    def test_snapshot_requires_image_ids_and_unambiguous_nonempty_entries(self):
        validate_snapshot({"123": {"unresolved": "Deleted"},
                           "456": {"author": "Owner", "attribution_url": "https://flickr.com/"}})
        for snapshot in ([], {"coco-sky-123": {"unresolved": "Deleted"}},
                         {"0123": {"unresolved": "Deleted"}},
                         {"123": {"unresolved": ""}},
                         {"123": {"author": "Owner"}},
                         {"123": {"author": "", "attribution_url": "url"}},
                         {"123": {"author": "Owner", "attribution_url": "url", "unresolved": "Deleted"}}):
            with self.subTest(snapshot=snapshot), self.assertRaises(ValueError):
                validate_snapshot(snapshot)


if __name__ == "__main__":
    unittest.main()
