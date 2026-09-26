# MLSPIKE WP1 sample rig

Python 3.11 tooling for the unmerged `spike/mlspike` branch. Nothing here runs a
model, installs an inference runtime, touches the catalog, or changes the app.
Net product LOC is 0; deletes nothing (isolated evaluation). Keep this branch out
of main. WP1 is bounded at eight active hours; Claude records time in the run's
state and stops for the owner at that bound.

**D-6: local evaluation only.** Images, masks, annotation dumps, licence evidence
and contact sheets stay outside this repository, under the run directory.
Never commit or redistribute them, and never use them for training or tuning.
Only source identifiers and attribution in the reviewed `edge-set.json` and
`coco-attribution.json` belong in Git. The CLIs refuse sample-data output directories
inside the repository. Tests generate tiny synthetic files inside temporary directories under `tests/` and remove them.

## Setup and responsibilities

`requirements.txt` pins Pillow 12.3.0 and numpy 2.4.4. Claude adds the pip artifact
hashes on the networked host before treating this as the hashed requirements lock;
Astra neither downloads dependencies nor invents hashes. Install the completed
lock with `python -m pip install --require-hashes -r spikes/mlspike/requirements.txt`.
The tools are scripts, run from the repository root; installing this project is
unnecessary.

Claude runs downloads, queries, visual review, control builds and hosted workflows.
No workflow is authored here. G1/G3/G4 measurements and the hardened macOS signing
change are Claude's work. The scripts accept paths to that evidence. The owner
confirms the contact sheet only after the approved WP2 handoff is ready. Only then
does Claude record the S1 start, deadline and manifest hash in LRPARITY. Generating
a sheet does not start S1 or imply owner approval.

## Labeled selection

Provide the official COCO instances val2017 JSON and COCO-Stuff val2017 JSON
(`images`/`licenses` from instances; `categories`/`annotations` from Stuff).
Extract archives locally first. These tools do not download annotation dumps.

```powershell
python spikes/mlspike/select_labeled.py --instances RUN/annotations/instances_val2017.json --stuff RUN/annotations/stuff_val2017.json --attribution spikes/mlspike/coco-attribution.json --output-dir RUN/selection
```

Seed 51 is fixed. The selector sorts by numeric image ID before shuffling with
Python's seeded PRNG. It selects 16 people, 8 animals, 16 sky and 4 indoor negatives.
It selects subjects first, excludes their IDs from the sky pool, reserves six
tree-adjacent skies, then fills the remaining sky slots. An insufficient pool
fails; thresholds never relax. Annotation JSON SHA-256 values travel with the
selection, together with the exact attribution snapshot file SHA-256.

Subject coverage uses COCO's annotated areas: exactly one non-crowd person/animal,
at least 15% of the frame; all other instance areas summed at most 5%; long edge
at least 600. Animal categories are COCO's ten animal classes. For sky, the union
of sky-other and clouds must cover 10–70% of pixels. A qualifying tree covers at
least 5% and touches sky across a pixel edge (four-neighbor adjacency).
Negatives require zero sky pixels and a positive ceiling-other or ceiling-tile
mask as a conservative indoor cue. Floor labels alone do not establish indoors.

RLE accepts both COCO compressed strings (including signed delta runs) and plain
run arrays, in column-major order. Polygon masks union pixel-center scanline fills;
coordinates are clipped to the image. Every PNG mask uses the image's native
raster dimensions, with values 0/255. No resizing occurs until the contact-sheet
thumbnail; no predicted mask exists in WP1.

COCO creator names are missing. `--attribution` is required and names the reviewed
`coco-attribution.json`, initially `{}` for Claude to fill. It is keyed by COCO
image ID (e.g. `"123"`, never sample ID). Each entry is either
`{"author": "Flickr owner", "attribution_url": "https://www.flickr.com/photos/OWNER/PHOTO/"}`
or `{"unresolved": "Photo deleted; no recoverable owner"}`.

Each seeded draw skips known unresolved images without reshuffling. At the first
unknown image, selection exits with code 2 without writing `labeled.json`. It writes
`needs-attribution.json`: an ordered list of `image_id` / `flickr_url` records for
that draw's remaining shortfall plus up to five backups. Only unknown IDs appear;
a smaller remaining pool yields fewer backups. Resolve that batch on the host:

```powershell
python spikes/mlspike/resolve_flickr_authors.py --needs RUN/selection/needs-attribution.json --snapshot spikes/mlspike/coco-attribution.json
```

The helper parses the photo ID from COCO's Flickr URL, follows `photo.gne` using a
cookie jar, and records the photo page's owner handle and canonical attribution URL.
An empty `/photos///` owner, failed request or unresolved redirect becomes an
`unresolved` entry. Existing snapshot decisions are preserved; Claude reviews them
and may correct a failed lookup before rerunning selection. No live lookup occurs
in selection or fetching. Repeat selection and resolution until all draws are
attributable; quotas never relax. On success the needs file is removed. A completed
selection directory cannot be overwritten: use a fresh directory for another run.

The fetcher requires the same snapshot bytes (checked against `attribution_sha256`
in `labeled.json`), uses authors by COCO image ID, and carries the hash into the
manifest. It rejects missing/unresolved entries before downloading, even if the
selection contains an author. Commit the reviewed snapshot alongside the picks.

## Edge review

```powershell
python spikes/mlspike/commons_candidates.py --category portrait --query "portrait backlit hair" --user-agent "HappyPhoton MLSPIKE (YOUR CONTACT)" --output-dir RUN/candidates
```

Repeat for `pet`, `product`, `low-light`, `multi-subject` and `landscape`.
Queries are discovery aids, not automatic aesthetic selections. Override
`--query` to find each sub-quota; keep each review batch in its own output directory.
For offline processing, replace the live query with `--response PATH` to a saved
Commons API response. The API collector follows continuation and filters on
explicit allowed licence, creator, JPEG/PNG MIME type and long edge of at least
2400. Original upload URLs have tracking queries removed.

`edge-set.json` intentionally starts as `[]`. After visual review, Claude copies
36 chosen records into it. An empty list is valid pick-list syntax but cannot pass
G2. Each record uses this shape (values below are schematic, not actual picks):

```json
{
  "id": "commons-123",
  "source": "commons",
  "source_id": "123",
  "url": "https://upload.wikimedia.org/PUBLIC-SOURCE.jpg",
  "author": "Photographer",
  "licence": "CC-BY-SA-4.0",
  "category": "portrait",
  "width": 3000,
  "height": 2000,
  "raw": false,
  "tags": ["backlit-or-flyaway", "dark-on-dark"]
}
```

Keep optional `title`, `attribution_url` and `licence_url` from Commons.
A `sha256` field pins the expected source bytes; the fetcher always records the
actual SHA-256. Use `source: "raw.pixls.us"` with its public URL, stable source ID,
CC0-1.0 and `raw: true` for that archive. For repo RAWs use `source: "repo"` and
a revision-pinned `raw.githubusercontent.com/seasalim/happy-photon/REV/Tests/assets/...`
URL. Read `Tests/assets/README.md` for the original attribution and hash.
The two D70 burst fixtures are the same image; they must not occupy two slots.

For already-local public files, add `local_path` relative to the explicit
`--local-root`. The URL remains the public provenance URL. Local paths never enter
the manifest. Listed local files must be fully available; Windows offline/recall
attributes are rejected before reads. No originals are modified.

RAW records require `dimensions_evidence`: a reviewed source metadata reference
establishing the original width/height and 2400-pixel long edge. The script cannot
establish sensor dimensions without a RAW decoder. It extracts the largest
decodable embedded JPEG for display, without developing RAW pixels; a missing JPEG
fails explicitly. A small embedded preview does not lower the original size quota.

Required categories and review tags:

| Category | Count | Minimum tags |
| --- | ---: | --- |
| portrait | 10 | 4 backlit-or-flyaway, 2 dark-on-dark |
| pet | 6 | 2 long-fur |
| product | 4 | — |
| low-light | 4 | — |
| multi-subject | 4 | — |
| landscape | 8 | 3 branches, 1 wires, 1 sunset, 1 hazy-horizon |

At least six edge records must be RAW. Tag judgments and RAW dimension evidence
are reviewer assertions, displayed on the sheet for owner confirmation.

## Rig and annotation licence inputs

`--rig` takes a JSON object keyed by `windows-2025`, `ubuntu-24.04`, `macos-15`.
Each value records `runner_image` (include image version), `cpu_model`,
`architecture` (`x64` or `arm64`), `cores` and integer `ram_bytes`.
Use the measured G3 artifact identities, not guessed values. The expected classes
are 4 cores/about 16 GiB for x64 and M1/3 cores/about 7 GiB for arm64.
Keep volatile capture timestamps in the run evidence, outside this deterministic
rig JSON. Reuse the exact rig input for the selection reproducibility rerun.

`--annotation-licences` takes a JSON list of exactly two records named
`instances` and `stuff`. Each has `name`, `url`, `licence`, `reviewed: true`,
`sha256` and `path` relative to that JSON file's directory. Save and hash the
downloaded licence text first. Claude verifies those annotation terms and stops
for the owner on a surprise. The fetcher requires this review, checks the hashes,
and copies the evidence into the sample directory. It does not claim legal review.
Image licences are independently restricted to the spec's allowlist; NC and ND
never pass. Model/weight licence review belongs to WP2.

## Fetch, rerun and owner sheet

```powershell
python spikes/mlspike/fetch.py --labeled RUN/selection/labeled.json --edges spikes/mlspike/edge-set.json --rig RUN/rig.json --annotation-licences RUN/annotation-licences.json --attribution spikes/mlspike/coco-attribution.json --output-dir RUN/sampleset
python spikes/mlspike/contact_sheet.py --manifest RUN/sampleset/manifest.json --output-dir RUN/contact-sheet
```

The manifest contains sorted samples, original/mask/RAW-preview hashes, attribution,
selection evidence, annotation hashes/licences and rig identity. JSON is UTF-8,
sorted-key, compact, newline-terminated and timestamp-free. SHA-256 of those bytes
is `manifest.sha256`, the sample revision. Absolute input paths are excluded.
Original source bytes are retained; masks and previews are separate files.

For G2, rerun the selector with the same annotations and seed, then fetch to a
second fresh sample directory using the same picks, attribution, licence and rig
inputs. Existing manifests are never overwritten. An interrupted directory can
only reuse an existing image when the input explicitly pins its SHA-256.

```powershell
python spikes/mlspike/verify_manifest.py --manifest RUN/sampleset/manifest.json --compare RUN/rerun-sampleset/manifest.json
```

This checks both sets' files, quotas, licences, mask sizes, source provenance,
rig records, hash sidecars and equality of the independent manifest hashes.
A pass is the automated G2 evidence, not the owner's visual confirmation or the
G1/G4 package measurements. Open `RUN/contact-sheet/contact-sheet.html` locally;
keep its thumbnails beside it. It shows all 80 images, categories, attribution,
licences and sub-quota tags. Ground-truth masks are green overlays, including
empty negatives. RAW entries use embedded previews. Never publish this directory.

## Verification

```powershell
python -m compileall -q spikes/mlspike
cd spikes/mlspike
python -m unittest tests.test_masks.MaskTests tests.test_selection.SelectionTests tests.test_commons.CommonsTests tests.test_pipeline.PipelineTests tests.test_attribution.AttributionTests
```

The orchestrator separately runs discovery and the repository verification script,
checks the control packages contain no `spikes/` payload, and owns the gate report.
