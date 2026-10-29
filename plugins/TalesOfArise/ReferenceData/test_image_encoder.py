"""Encoder characterization from generated pixels; no downloaded image fixtures."""

import io
import json
import sys
import unittest
from pathlib import Path

from PIL import Image

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_assets import digest, encode_image
from importer_test_support import ROOT


def source_image(size, textured=False):
    image = Image.new("RGBA", size, (42, 64, 128, 255))
    if textured:
        image.putdata([((x * 37 + y * 19) % 256, (x * 11 + y * 43) % 256,
                        (x * y * 7) % 256, 255 if x % 3 else 64)
                       for y in range(size[1]) for x in range(size[0])])
    output = io.BytesIO()
    image.save(output, "PNG")
    return output.getvalue()


CASES = {
    "lossless-icon": ((20, 12), False, False),
    "palette-icon": ((96, 80), True, False),
    "downscaled-map": ((2300, 17), True, True),
}


class ImageEncoderTests(unittest.TestCase):
    def test_generated_images_match_original_encoder_goldens(self):
        expected = json.loads((ROOT / "golden/encoder.json").read_text(encoding="utf-8"))
        self.assertEqual(set(CASES), set(expected))
        for name, (size, textured, is_map) in CASES.items():
            with self.subTest(case=name):
                content, extension, dimensions = encode_image(source_image(size, textured), is_map)
                self.assertEqual(expected[name], {"sha256": digest(content), "extension": extension,
                                                  "dimensions": list(dimensions), "bytes": len(content)})

    def test_invalid_image_is_rejected(self):
        with self.assertRaises(Image.UnidentifiedImageError):
            encode_image(b"not an image", False)

    def test_unsupported_image_format_is_rejected(self):
        source = io.BytesIO()
        Image.new("RGB", (1, 1)).save(source, "GIF")
        with self.assertRaisesRegex(ValueError, "Unsupported source image type: GIF"):
            encode_image(source.getvalue(), False)


if __name__ == "__main__":
    unittest.main()
