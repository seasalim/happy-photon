import unittest

import numpy as np

from masks import adjacent, decode, rle_counts, union


class MaskTests(unittest.TestCase):
    def test_compressed_rle_signed_delta_and_column_order(self):
        expected = np.array([[1, 0, 0], [1, 0, 1]], dtype=np.uint8)
        for counts in ("023O", [0, 2, 3, 1]):
            np.testing.assert_array_equal(decode({"size": [2, 3], "counts": counts}, 3, 2),
                                          expected)
        self.assertEqual([32], rle_counts("P1"))

    def test_polygon_pixel_centers_and_union(self):
        first = {"segmentation": [[0, 0, 2, 0, 2, 2, 0, 2]]}
        second = {"segmentation": [[1, 1, 3, 1, 3, 3, 1, 3]]}
        expected = np.array([[1, 1, 0], [1, 1, 1], [0, 1, 1]], dtype=np.uint8)
        np.testing.assert_array_equal(union([first, second], 3, 3), expected)

    def test_polygon_outside_image_does_not_fill_pixels(self):
        self.assertFalse(decode([[-4, 0, -1, 0, -1, 2, -4, 2]], 3, 2).any())

    def test_invalid_rle_and_polygon_fail(self):
        for counts in ("P", "/", [7], [-1, 7], [1.0, 5]):
            with self.subTest(counts=counts), self.assertRaises(ValueError):
                decode({"size": [2, 3], "counts": counts}, 3, 2)
        with self.assertRaises(ValueError):
            decode({"size": [3, 2], "counts": [6]}, 3, 2)
        with self.assertRaises(ValueError):
            decode([[0, 1, 2]], 3, 2)

    def test_adjacency_does_not_wrap_edges_or_accept_diagonals(self):
        a = np.array([[1, 0], [0, 0]], dtype=np.uint8)
        self.assertTrue(adjacent(a, np.array([[0, 1], [0, 0]], dtype=np.uint8)))
        self.assertFalse(adjacent(a, np.array([[0, 0], [0, 1]], dtype=np.uint8)))


if __name__ == "__main__":
    unittest.main()
