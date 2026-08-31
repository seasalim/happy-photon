# Pipeline test assets

These fixtures are committed directly for deterministic image-pipeline tests. The
raw.pixls.us files were uploaded under the
[CC0 1.0 public-domain dedication](https://creativecommons.org/publicdomain/zero/1.0/).
The Dryad high-ISO dataset is also CC0. The derived standard-format fixtures therefore
remain CC0. The compact Display P3 profile used during generation is also CC0.
The ColorChecker fixture is the sixth distinct RAW and is an author-captured CC0
exception to the raw.pixls.us roster. The iPhone HEIC is a second
author-captured CC0 exception on the same footing.

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
| `canon-eos-6d-iso-6400.cr2` | High-ISO Bayer RAW for luminance-NR tuning; original file `IMG_2977.CR2`, ISO 6400, f/3.5, 30 s | [Dryad: *X, Y, and Z: A bird's eye view on light pollution*](https://doi.org/10.5061/dryad.v6wwpzh0m), CC0 | `7727ee0280b44ea1d633962f49942f37f3c7ec6d704d22e108a5223666327c32` |
| `nikon-d300-colorchecker.nef` | Physical ColorChecker ground truth; Nikon D300, captured 2010-05-16 under studio flash at ISO 100 and f/9 | Author capture supplied to the project and released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/); 11,443,794 bytes | `96c947a3289c21ef34e609640f441bb5ae4f8f85bd9ff7194eeb0ff1d4063ed0` |
| `srgb-reference.jpg` | Tagged sRGB same-picture reference | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `bffb5c04d6b1509760b08f94ecb872a3599f9e5a5d3b7f9bbf92fae6319011a4` |
| `srgb-exif-gps-orientation-6.jpg` | EXIF, GPS, and orientation policy | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `5c84507702920b07ddf35dc6e7210ec6974c8f54cb9fcd666b7ef263684b9694` |
| `display-p3-reference.jpg` | Display P3 **normalization** sentinel; generated from sRGB content, so it holds nothing outside the sRGB gamut and cannot demonstrate gamut preservation | Generated from the sRGB reference with [Compact ICC Profiles `DisplayP3-v4.icc`](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/master/profiles/DisplayP3-v4.icc), CC0 | `bcc8f43999e4881575df3fca8b04abdf12c03ff9d51e1eb1b26345f14608e34b` |
| `DisplayP3-v4.icc` | Independent Display P3 source profile for the wide-gamut normalization test; 480 bytes | [Compact ICC Profiles `DisplayP3-v4.icc`](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/master/profiles/DisplayP3-v4.icc), CC0 | `cb51de38e482ee974c0c76b9689e16aad04bad16e226fed2f30c842d15ff3a3d` |
| `adobe-rgb-reference.jpg` | Adobe RGB normalization sentinel | Generated from the sRGB reference with Magick.NET's Adobe RGB 1998 profile, CC0 | `acdd66bc2ea5b55de54f329e7a5c53a84f5efd7f7041812cb36501f4fd9cc4e2` |
| `reference-16bit.tiff` | Standard-format depth preservation | Generated from the reference CR2 with `scripts/generate-pipeline-test-assets.cs`, CC0 | `2d68f4b19d0623ca220df8205e307b34a63c4be9fcc50f126ebbb20d123052d4` |
| `reference.heic` | Platform-codec path | Encoded from `srgb-reference.jpg` with pillow-heif 1.1.1, CC0 | `297afe8c8415871966591d671e7f181a6e73a31c1dcd65dcb657d997981ff166` |
| `iphone-14-pro-iso-1000.heic` | High-ISO standard-source luminance-NR tuning; iPhone 14 Pro, ISO 1000, captured 2025-04-18 | Author capture supplied to the project and released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/); GPS metadata stripped before commit; 2,019,243 bytes | `e4be19fba9f585b74ac633af0e545bcb5a331db7ec3d0d8dea81d8c538cb1e02` |

Regenerate the JPEG and TIFF derivatives:

```powershell
dotnet run --file scripts/generate-pipeline-test-assets.cs -- `
  Tests/assets path/to/DisplayP3-v4.icc
```

The HEIC was encoded at quality 90 with pillow-heif 1.1.1. Its decode test is
skipped with an explicit reason when the platform codec is unavailable.

## XMP sidecar fixtures (`xmp/`)

Real third-party XMP for sidecar/interop tests. The `lightroom-*` files were
authored for the project with Adobe Lightroom Classic 15.5.1 (crs 18.5.1,
Process Version 15.4) by editing the committed CC0 fixtures per the capture
matrix in the 2026-08-30 trial (run 220; findings log kept with the capture
set). They are derivatives of CC0 content and are released CC0. Sidecars are
committed verbatim; `-embedded`/`-jpg` files are XMP packets extracted
byte-for-byte from files Lightroom rewrote in place.

| File | Purpose | SHA-256 |
|------|---------|---------|
| `darktable-rating.xmp` | Third-party (darktable) rating sidecar | `62b6db507e953b1cc83e92e305e086a3595d3315b3f607cc643c97b06fe466e4` |
| `lightroom-baseline-nocrop.xmp` | LR no-crop state: crop edges 0,0,1,1 present WITHOUT `HasCrop` -> must read Empty | `7209c3e37e04fe4f0cbe13c7ab63b7a4d00dfe219ffd019251f9ad8feab7d1e7` |
| `lightroom-assessed.xmp` | Rating 3, `xmp:Label="Yellow"`, `xmpDM:pick="1"` | `18cfc318336b0e3f1f43f56aec11b7bbef9adef0532a815000770294fc51bf3d` |
| `lightroom-crop-plain.xmp` | Asymmetric user crop, `HasCrop="True"`, angle 0 -> Matched; also carries `crss:SavedSettings` snapshot copies (top-level-only parsing trap) | `466bd6d1b8f28b653e5ecfaae2e522b494e13fd17380061cd34102d2a731f0e5` |
| `lightroom-crop-angled.xmp` | `CropAngle="-3"` (UI +3) -> Unsupported | `b85f4a276b0b06feb19839919414caf42de1d91f161fb96f4404a64c9bf12d9d` |
| `lightroom-crop-rotated.xmp` | 90-degree LR rotation: `tiff:Orientation="6"`, crop fractions byte-identical to plain -> Unsupported via orientation guard | `1e2ae582a4d4d3680e922acdfeabdb452b0815097b63f4d6748e0363b2495d21` |
| `lightroom-crop-warp.xmp` | `CropConstrainToWarp="1"` + Transform, fractions byte-identical to plain -> Unsupported | `db3be712ae5934f6c9876f0a2c1accc1a258fcdff3752d62fe3c426809be5911` |
| `lightroom-cleared-reject.xmp` | `xmpDM:pick="-1"`; rating/label/`HasCrop` REMOVED (clears are removals) | `0a9a6dece6560256ff4be471b0494a02d1fc7b3c3cca97170578a65d529f6f37` |
| `lightroom-camera-crop-baseline.xmp` | In-camera 1:1 aspect crop: camera-authored `HasCrop="True"` at import | `ed20909550d7e60a028ca7252f5c924715ef8d80c3241bf8cce9d295e5f1bc19` |
| `lightroom-camera-crop-reset.xmp` | After user crop reset: camera-crop edges persist WITHOUT `HasCrop` -> Empty | `07c39df84edc7b5e404d5dd02001dde57883183a713c4a09c16f15978af1365e` |
| `lightroom-label-custom.xmp` | `xmp:Label="CustomRed"` — custom label-set name (name-map / Unsupported-token tests) | `93a481aa09d8e0e81b42cfed2669466a9d2fa133ae68bab650fc21745152335f` |
| `lightroom-label-custom-embedded.xmp` | `xmp:Label="CustomGreen"` packet extracted from an LR-rewritten HEIC | `637b5a5fe0001d827bb525fecd1b918c2e973871d0dee50905ec6a82b5eb5ca5` |
| `lightroom-pair-raw-owner.xmp` | RAW+JPEG stacked mode: single sidecar owns the pair's assessment (JPEG untouched) | `d9a7685779495023a9e9af13684a54a5cf6b45a9b38abc3565f7098120c89547` |
| `lightroom-pair-separate-nef.xmp` | Separate-photos mode, NEF sidecar (rating 3, pick 0) — basename shared with the JPEG twin (ambiguity tests) | `ab68b30d3ebc230c9af12c35cebc7c958bc7809b68db005fa60df79e66dc1f47` |
| `lightroom-pair-separate-jpg.xmp` | Separate-photos mode, packet from the rewritten JPEG twin (rating 4, pick 1) — diverges from the NEF sidecar | `7780c259157a3c1e6db61164fba5f41e849a04de2cf29a87b4528fbe7138e9d2` |

## Display-reference comparison assets

External reference renders follow
`references/<fixture-stem>.<tool>.<lossless-extension>`. The comparison harness also
accepts a fixture-keyed directory supplied by `HAPPY_PHOTON_COMPARE_REFERENCE_DIR`.
Committed references are canonical test assets: store them losslessly with exactly the
1600px measurement long edge (never smaller), and add their compressed size to the
recursive 120 MiB asset budget before committing. If the two real compressed files do
not fit the remaining budget, obtain maintainer approval for a budget change; do not
reduce their measurement resolution.

Copy and complete this provenance block for every added reference:

```text
### <fixture-stem>.<tool>.<ext>
- Source fixture: <committed RAW filename and SHA-256>
- Redistribution license: <license name and source URL>
- Reference SHA-256: <lowercase digest>
- Renderer: <tool name and exact version>
- Settings: <complete preset/profile or exported sidecar plus deviations>
- Color profile: <embedded/declared profile; never “application default”>
- Orientation handling: <source EXIF orientation and where it was applied>
- Output: <lossless format, bit depth, pixel dimensions, resize method>
```
