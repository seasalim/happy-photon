# Pipeline test assets

These fixtures are committed directly for deterministic image-pipeline tests. The
raw.pixls.us files were uploaded under the
[CC0 1.0 public-domain dedication](https://creativecommons.org/publicdomain/zero/1.0/).
The Dryad high-ISO dataset is also CC0. The derived standard-format fixtures therefore
remain CC0. The compact Display P3 profile used during generation is also CC0.
The ColorChecker fixture is the sixth distinct RAW and is an author-captured CC0
exception to the raw.pixls.us roster.

Downloaded modern-camera fixtures are not part of this directory. Their authoritative
provenance, license, size, hash, and reviewed behavior are recorded in
[`../compatibility-fixtures.json`](../compatibility-fixtures.json).

| File | Purpose | Source / generation | SHA-256 |
|------|---------|---------------------|---------|
| `canon-eos-350d.cr2` | Reference Bayer RAW; tonal matrix and clipped highlights verified by a no-auto-bright linear decode | [raw.pixls.us Canon EOS 350D](https://raw.pixls.us/getfile.php/758/nice/Canon%20-%20EOS%20350D%20-%20RAW%20%283%3A2%29.CR2), CC0 | `8cbb84e04d93b005fe082da9c954122a612b5281af00aa088d767850f343fd38` |
| `nikon-d70-burst-1.nef` | Second Bayer path and burst determinism | [raw.pixls.us Nikon D70](https://raw.pixls.us/getfile.php/2060/nice/Nikon%20-%20D70%20-%2012bit%2012bit%20compressed%20%28Lossy%20%28type%201%29%29%20%283%3A2%29.NEF), CC0 | `dd6405aeb33b0cd5bf66c98ba98ccbb478a765450cfd130810e470dab8d1f4b4` |
| `nikon-d70-burst-2.nef` | Byte-identical second burst frame | Copy of `nikon-d70-burst-1.nef`, CC0 | `dd6405aeb33b0cd5bf66c98ba98ccbb478a765450cfd130810e470dab8d1f4b4` |
| `fujifilm-x30.raf` | X-Trans path | [raw.pixls.us Fujifilm X30](https://raw.pixls.us/getfile.php/1144/nice/Fujifilm%20-%20X30%20-%2012bit%2012bit%20uncompressed%20%284%3A3%29.RAF), CC0 | `1f65f680600c85a6f561d2b0ad7cd1f9d93ee7f733aafe4ecd37667d984dd6c4` |
| `pentax-k-r.dng` | Native DNG container path | [raw.pixls.us Pentax K-r](https://raw.pixls.us/getfile.php/8172/nice/Pentax%20-%20K-r%20-%2012bit%20%283%3A2%29.DNG), CC0 | `adc5c155341543e2e5c6de9c0ce87c86cc4629fe90a0f6e68fdf685dcd531b64` |
| `canon-eos-6d-iso-6400.cr2` | High-ISO Bayer RAW for FBDD quality/runtime evaluation; original file `IMG_2977.CR2`, ISO 6400, f/3.5, 30 s | [Dryad: *X, Y, and Z: A bird's eye view on light pollution*](https://doi.org/10.5061/dryad.v6wwpzh0m), CC0 | `7727ee0280b44ea1d633962f49942f37f3c7ec6d704d22e108a5223666327c32` |
| `nikon-d300-colorchecker.nef` | Physical ColorChecker ground truth; Nikon D300, captured 2010-05-16 under studio flash at ISO 100 and f/9 | Author capture supplied to the project and released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/); 11,443,794 bytes | `96c947a3289c21ef34e609640f441bb5ae4f8f85bd9ff7194eeb0ff1d4063ed0` |
| `srgb-reference.jpg` | Tagged sRGB same-picture reference | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `bffb5c04d6b1509760b08f94ecb872a3599f9e5a5d3b7f9bbf92fae6319011a4` |
| `srgb-exif-gps-orientation-6.jpg` | EXIF, GPS, and orientation policy | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `5c84507702920b07ddf35dc6e7210ec6974c8f54cb9fcd666b7ef263684b9694` |
| `display-p3-reference.jpg` | Display P3 normalization sentinel | Generated from the sRGB reference with [Compact ICC Profiles `DisplayP3-v4.icc`](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/master/profiles/DisplayP3-v4.icc), CC0 | `bcc8f43999e4881575df3fca8b04abdf12c03ff9d51e1eb1b26345f14608e34b` |
| `adobe-rgb-reference.jpg` | Adobe RGB normalization sentinel | Generated from the sRGB reference with Magick.NET's Adobe RGB 1998 profile, CC0 | `acdd66bc2ea5b55de54f329e7a5c53a84f5efd7f7041812cb36501f4fd9cc4e2` |
| `reference-16bit.tiff` | Standard-format depth preservation | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `2d68f4b19d0623ca220df8205e307b34a63c4be9fcc50f126ebbb20d123052d4` |
| `reference.heic` | Platform-codec path | Encoded from `srgb-reference.jpg` with pillow-heif 1.1.1, CC0 | `297afe8c8415871966591d671e7f181a6e73a31c1dcd65dcb657d997981ff166` |

Regenerate the JPEG and TIFF derivatives:

```powershell
dotnet run --file scripts/generate-pipeline-test-assets.cs -- `
  Tests/assets path/to/DisplayP3-v4.icc
```

The HEIC was encoded at quality 90 with pillow-heif 1.1.1. Its decode test is
skipped with an explicit reason when the platform codec is unavailable.
