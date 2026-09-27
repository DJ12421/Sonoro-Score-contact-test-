# Sonoro-Score Scanner — TODO

> **Attribution**: OCR pipeline strategy, region definitions, icon-signature matcher, preprocessing
> approach, and fixture/accuracy methodology are adapted from [`../Tacet-Lab`](../Tacet-Lab) under
> the GPL-3.0 License. See `NOTICES.md`.
>
> **Scope note**: this pass is **1080p-only**. Tacet-Lab's own docs track 1080p and 1440p fixture
> sets; everything below only carries over the 1080p side of that until 1440p is explicitly picked
> back up. Don't add 1440p fixtures or resolution-branching logic as part of this TODO.
>
> **Status (2026-09-28):** Priority 1 + 2 implemented (uncommitted working tree). Priority 3
> thresholds codified in `ScannerConfig.cs`; re-run pending (no .NET SDK on PATH). Priority 4 +
> export done in code, pending test run. Accuracy work (§5–§9 below) not yet started.

---

## 1. Current Architecture

```
Full screenshot
     │
     ▼
EchoRegions.ExtractPanel()         ← crops the right-side echo panel
     │
     ├─ [text regions] ──────────► ScannerConfig-gated OCR                    ← ✅ Tesseract primary
     │    name, level, cost,         TesseractOcr.RecognizeAsync()
     │    mainStat, sonataZone,      fallback WinOcr iff UseWindowsOcrFallback
     │    ownerZone, substatsBlock   (+ empty-result fallback, OcrMinTextLength=2)
     │
     ├─ [rarity] ────────────────► RarityClassifier.Classify() ← ✅ pixel-based (fine)
     │
     ├─ [sonata] ────────────────► SonataSignatureMatcher.Match(icon) ← ✅ icon-signature primary
     │                             (conf > ScannerConfig.SonataIconMinConfidence=0.70)
     │                             fallback OCR text parse (0.55 fuzzy) → catalog default
     │
     └─ Results assembled into EchoScanResult (+ sonata-source warnings for test audit)
```

### Files in `src/SonoroScore.Scanner/`

| File                            | Role                                         | Status                                                      |
| ------------------------------- | --------------------------------------------- | ------------------------------------------------------------ |
| `EchoRecognizer.cs`             | Main pipeline orchestrator                   | ✅ Tesseract-primary + WinOcr gate (`ScannerConfig`)         |
| `EchoRegions.cs`                | Region rectangles (% of panel)               | ✅ incl. `SonataIcon`                                        |
| `WinOcr.cs`                     | Windows Media OCR wrapper                    | ✅ fallback (gated by `ScannerConfig.UseWindowsOcrFallback`) |
| `TesseractOcr.cs`               | Tesseract OCR wrapper                        | ✅ wired as primary (fixed iterator API)                     |
| `ScannerConfig.cs`              | QA knobs: OCR gate + thresholds              | ✅ implemented                                                |
| `ImagePreprocessor.cs`          | Contrast boost + upscale                     | ⚠️ exists (in `WinOcr.cs`) — **too generic, see §5**         |
| `GameDatabase.cs`               | Echo catalog (nanoka API + local cache)      | ✅ exists                                                    |
| `FuzzyMatcher.cs`               | Fuzzy string matching                        | ✅ exists                                                    |
| `StatParser.cs`                 | Stat label → StatKey                         | ✅ exists                                                    |
| `StatPixelMatcher.cs`           | HP pixel fallback                            | ✅ exists — extend per §7                                    |
| `RarityClassifier.cs`           | Star-count from pixel band                   | ✅ exists                                                    |
| `TunableRolls.cs`               | Snap substat values to valid rolls            | ✅ exists — audit per §7                                     |
| `ScanResult.cs`                 | EchoScanResult / FieldResult / SubstatResult | ✅ exists                                                    |
| **`SonataSignatureMatcher.cs`** | Icon pixel-signature match (Tacet-Lab)       | ✅ implemented (versioned + auto-update)                     |
| `EchoExportModel.cs`            | Shared export DTO                            | ✅ implemented                                                |
| `TacetLabExporter.cs`           | `tacet-lab-backup.json` (schema v7)          | ✅ implemented                                                |
| `GoodExporter.cs`               | GOOD v3 bridge (`sonoro-good.json`)          | ✅ implemented                                                |
| `EchoAccuracy.cs`               | Field-level fixture accuracy harness         | ❌ **new, see §6**                                            |
| `EchoFieldPreprocessor.cs`      | Per-region, polarity-aware preprocessing     | ❌ **new, replaces generic `ImagePreprocessor`, see §5**      |

---

## 2. Tacet-Lab Strategy to Copy

Paths below are relative to the `Sonoro-Score/` repo root, assuming both repos are checked out
side by side (`../Tacet-Lab`).

### 2a. Sonata Icon Region

From [`../Tacet-Lab/src/scanner/regions.ts`](../Tacet-Lab/src/scanner/regions.ts) line 47:

```
sonata region: x=0.88, y=0.008, width=0.115, height=0.065  (relative to panel)
```

This crops the **icon** to the right of "Sonata Effect" heading.

### 2b. Signature Matching Algorithm (`visual.ts`)

From [`../Tacet-Lab/src/scanner/visual.ts`](../Tacet-Lab/src/scanner/visual.ts):

1. Crop the sonata icon region from the panel bitmap.
2. Resize the crop to **16×16** pixels.
3. Flatten the 16×16 pixels into a 256-element array.
4. **Normalize**: each pixel channel value → `clamp(value, 0, 255) >> 2` (i.e. divide by 4, gives 0–63 range matching signature values).
5. Compute **dot-product similarity** against every entry in `generatedEchoSignatures`.
6. Return the entry with the highest similarity score.

### 2c. Signature Data

[`../Tacet-Lab/src/game-data/scanner-signatures.generated.ts`](../Tacet-Lab/src/game-data/scanner-signatures.generated.ts) exports:

```
generatedEchoSignatures = [
  { name: "Trailblazing Star", cost: 4, signature: [int8 × 256] },
  ...
]
```

This was converted to a C# resource: `sonata_signatures.json` (34 sets, versioned).

### 2d. Field Preprocessing Pipeline (`docs/architecture.md`, "Scanner privacy and ordering")

From [`../Tacet-Lab/docs/architecture.md`](../Tacet-Lab/docs/architecture.md):

> "Named regions are padded, enlarged, converted to grayscale, percentile-normalized,
> polarity-corrected, adaptively thresholded, lightly morphologically closed, and rendered as
> black text on a white background. The alternate retry uses global Otsu thresholding. Icon and
> color classifiers retain color input."

This is the pipeline `EchoFieldPreprocessor.cs` (§5) needs to port — `SonoroScore`'s current
`ImagePreprocessor` only does contrast boost + upscale, with no polarity handling, no adaptive
threshold, no morphological closing, and no Otsu fallback.

### 2e. Fixture / Accuracy Format (`docs/ocr-fixtures.md`)

From [`../Tacet-Lab/docs/ocr-fixtures.md`](../Tacet-Lab/docs/ocr-fixtures.md) — paired
`name.png` + `name.json` sidecar per sample, 1080p-only for this repo's purposes:

```json
{
  "fixtureVersion": 1,
  "layout": "echo-detail",
  "resolution": { "width": 1920, "height": 1080 },
  "uiScale": 1,
  "panelRect": { "x": 0.77, "y": 0.12, "width": 0.22, "height": 0.86 },
  "fieldRects": {
    "echo-name": { "x": 0.04, "y": 0.015, "width": 0.55, "height": 0.075 }
  },
  "name": "Hooscamp",
  "cost": 1,
  "rarity": 5,
  "level": 25,
  "sonata": "Lingering Tunes",
  "mainStat": { "key": "atkPercent", "value": 18.0 },
  "subStats": [
    { "key": "critRate", "value": 6.3 }
  ]
}
```

Their acceptance rule (from the same doc, trimmed to the 1080p-only subset that applies here):
`src/scanner/accuracy.ts` counts identity, cost, rarity, level, sonata, main stat, and each
substat as individual fields; corpus accuracy is total matched fields ÷ total expected fields.
A 95%-accuracy claim requires ≥25 varied real 1080p samples, no field silently defaulted, and
low-confidence/failed fields left editable rather than auto-committed.

---

## 3. Baseline Accuracy (measured, not yet fixture-verified — see §6)

Last full-corpus run (2026-09-28, WinOcr-fallback mode, 300 images from
`publish\AlephalSonata\aleph_images\session_20260927_204453`, 1080p):

```
dotnet run --project src/SonoroScore.Scanner.Cli -c Release -- --dir publish\AlephalSonata\aleph_images\session_20260927_204453 --export-tacet-auto --export-good-auto
```

| Field | Rate |
|---|---|
| Name | 293/300 (97.7%) |
| Main stat | 284/300 (94.7%) |
| Sonata | 300/300 (100%, all via icon match, 0 via OCR fallback — P2 target met) |
| Complete (≥4 substats) | 37/300 (12.3%) — **metric artifact**: unleveled echoes have locked substat rows and can never hit ≥4, this is not a pipeline failure |
| Avg substats/echo | 1.62 |

Exports: `tacet-lab-backup_*.json` + `sonoro-good_*.json` (284 echoes, 16 skipped w/o main stat).

Post-§5 numbers (same corpus, hybrid routing + field preprocessing, 2026-09-28):

| Field | Rate |
|---|---|
| Name | 291/300 (97.0%) |
| Main stat | 297/300 (99.0%) |
| 2nd main stat | 296/300 (98.7%) |
| Sonata | 300/300 (100%, all via icon match) |
| Complete (≥4 substats) | 103/300 (34.3%) |
| Avg substats/echo | 2.54 |

**Known regression**, first live Tesseract-primary run vs. prior WinOcr-primary run: 2nd-main-stat
recall jumped (5/5 on smoke incl. `Atk 150`, `Hp 2280`) but **substat/name recall dropped** —
preprocessing and `PageSegMode` are still WinOcr-tuned, not Tesseract-tuned. This is the entry point
for §5 below.

⚠️ Env issue RESOLVED 2026-09-28: Tesseract was never broken — `eng.traineddata` had been copied to
`bin/x64/Release/...` (solution-build layout) while `dotnet run` executes from `bin/Release/...` (no
`x64`). The `--diag-tess` CLI flag now proves engine init per output dir.

---

## 4. TODO — Tesseract / Icon Port (from original `integration_todo.md`)

### ✅ Priority 1 – Wire Tesseract into EchoRecognizer — DONE (working tree)

- [x] **Add Tesseract NuGet package** to `SonoroScore.Scanner.csproj`

```xml
<PackageReference Include="Tesseract" Version="5.2.0" />
```
> Requires native `leptonica-1.82.0.dll` + `tesseract50.dll` copied to output dir.
> Language data file `eng.traineddata` must be in `./tessdata/` next to the exe.

- [x] **Fix `TesseractOcr.cs`** — iterator uses `iter.Next(PageIteratorLevel.TextLine)` (correct 5.x API).
- [x] **Replace `WinOcr` calls in `EchoRecognizer.OcrRegionAsync`** with `TesseractOcr` primary +
`WinOcr` fallback gated by `ScannerConfig.UseWindowsOcrFallback` (incl. empty-result fallback).
- [x] **Replace substat OCR** (line ~123) with `OcrLinesAsync` (Tesseract primary + WinOcr fallback).

### ✅ Priority 2 – Sonata Icon Signature Matcher — DONE (working tree)

- [x] **Export signatures from Tacet-Lab** → `sonata_signatures.json` (34 sets, versioned
`{"version":"3.6","scannerSignatureVersion":"3.6","signatures":[...]}`).
- [x] **Create `SonataSignatureMatcher.cs`** (version-aware loader + `Match`).
- [x] **Define sonata icon region in `EchoRegions.cs`** (`SonataIcon = 0.88, 0.008, 0.115, 0.065`).
- [x] **Wire into `EchoRecognizer`** (step 9; threshold `ScannerConfig.SonataIconMinConfidence = 0.70`;
OCR text fallback retained; sonata source logged to warnings for audit).

### 🟡 Priority 3 – Quality & Reliability — PARTIAL (code done, test run pending)

- [x] **Confidence thresholds** – codified in `ScannerConfig.cs`:
`OcrMinTextLength=2` + empty-fallback, `SonataIconMinConfidence=0.70`,
`EchoNameMinConfidence=0.68`, `SonataTextMinConfidence=0.55`.
- [x] **Tessdata setup docs** – `README` section present (eng.traineddata → `./tessdata/`, DLLs via NuGet).
- [x] **Attribution file** – `NOTICES.md` credits `../Tacet-Lab` (GPL-3.0; corrected from MIT) + adapted portions list. **Extend this list per §9 once §5/§6 land.**
- [x] **Re-run test suite** after wiring — DONE 2026-09-28 (see §3 above for full numbers).

### ✅ Priority 4 – Nice-to-Haves — DONE in code (test run pending)

- [x] **Signature version check** — `sonata_signatures.json` versioned (`version` +
`scannerSignatureVersion` = `3.6`); `SonataSignatureMatcher.CheckVersion(log)` warns on mismatch;
`EchoRecognizer` surfaces it per-scan. Loader still accepts legacy bare-array files.
- [x] **Auto-update signatures** — `SonataSignatureMatcher.EnsureUpdatedAsync(url?, forceRefresh, log)`;
CLI flag `--update-signatures [--signature-url …]`.
- [x] **WinOcr removal gate** — `ScannerConfig.UseWindowsOcrFallback` (default true);
CLI `--no-winocr-fallback`, env `SONORO_NO_WINOCR=1` for QA A/B before any removal.
- [x] **1-click exports** — `TacetLabExporter` (`tacet-lab-backup.json`, schemaVersion 7) +
`GoodExporter` (`sonoro-good.json`, GOOD v3 bridge). CLI: `--export-tacet-auto/--export-tacet <path>`,
`--export-good-auto/--export-good <path>`; Debugger auto-writes both per scan; GUI toolbar has
`⬆ Tacet-Lab` + `⬆ GOOD` buttons.

---

## 5. TODO — Accuracy Priority 0/1: Field-Aware Preprocessing + Per-Region PSM

**Goal: fix the Tesseract-primary name/substat regression noted in §3, by porting the
preprocessing shape documented in [`../Tacet-Lab/docs/architecture.md`](../Tacet-Lab/docs/architecture.md) §2d.**

- [x] Split `ImagePreprocessor` out of `WinOcr.cs` into `SonoroScore.Scanner/ImagePreprocessing/` — legacy `ImagePreprocessor.cs` kept verbatim for the WinOcr fallback path (measured numbers), new `EchoFieldPreprocessor.cs` serves the Tesseract path.
- [x] **Pad before enlarge**: `padPx` param in source-image space (default 0, matching shipped Tacet code where `paddingX/Y = 0`).
- [x] Percentile normalization (4th/96th, min range 18) per-region — robust to the translucent panel background.
- [x] **Polarity detection** per region (8% border-mean test, invert when mean < 128). Not hardcoded panel-wide.
- [x] Otsu global threshold as the primary path — matches shipped Tacet worker. DEVIATION (documented): their `adaptiveThreshold`/dilate/erode helpers exist but are never called (verified by repo-wide grep); adaptive was therefore NOT ported, only Otsu. The existing WinOcr empty-result fallback is retained instead of an Otsu-retry.
- [x] Strategy cleanups ported 1:1: name artwork removal, label small-noise removal (dot-protected), substat highlight trim + footer trim; 3× enlarge for text strategies, white quiet border.
- [x] Color-crop checkpoint: `RarityClassifier`, `SonataSignatureMatcher`, `StatPixelMatcher` all crop raw color bitmaps; binarized output never reaches them (verified + commented at each site).
- [x] `PageSegMode` parameter on Tesseract calls (plus LSTM-only engine, DPI 300, interword-spaces, no-invert, per-kind char whitelists — all mirrored from their `ocr-pool.ts`):
  - name → `NameRegionPsm` (SingleBlock, their name-kind mode) with Auto-retry second chance.
  - strips (level/cost/main/2nd-main/owner) → SingleLine; sonata zone + substats block → SingleBlock.
- [x] PSM knobs in `ScannerConfig` (`NameRegionPsm`, `SubstatBlockPsm`) + `--name-engine` routing (names default WinOcr-only: measured 91.7% vs 66.0%).
- [x] `--diag-tess` prints PSM + preprocessing paths; `--dump-lines` compares engines per crop.
- [x] Re-ran the 300-image corpus (§3): mains 93.7%→**99.0%**, 2nd-main 24.7%→**98.7%**, names 91.7%→**97.0%**, complete 6.0%→**34.3%**, subs 1.47→**2.54**. Extras that made it work beyond the doc: merged "Label Value" row parsing, trailing-junk strip, parse-gated strip fallback.

---

## 6. TODO — Accuracy Priority 2: 1080p Fixture Corpus + Field-Level Accuracy Harness

**Goal: stop eyeballing "97.7%" from one ad-hoc CLI run; port
[`../Tacet-Lab/docs/ocr-fixtures.md`](../Tacet-Lab/docs/ocr-fixtures.md)'s protocol, 1080p slice only.**

- [ ] Create `tests/fixtures/echoes/english-1080p/` under the scanner test project. (No 1440p folder for now — out of scope, see header note.)
- [ ] Define a C# sidecar record type mirroring §2e's JSON shape: `panelRect`, `fieldRects` (normalized 0–1, panel-relative), plus full expected parse (`name, cost, rarity, level, sonata, mainStat, subStats[]`).
- [ ] Capture ≥25 varied real 1080p samples: multiple echo costs, multiple rarities, multiple sonata sets, unleveled *and* fully-leveled (5-substat) echoes, a few deliberately low-contrast/edge-lighting frames. Anonymize (strip UID/account name) before committing.
- [ ] Write `EchoAccuracy.cs`: counts identity/cost/rarity/level/sonata/mainStat and **each individual substat slot** as separate scored fields — not "echo complete y/n" — matching Tacet-Lab's `accuracy.ts` field-counting rule.
- [ ] Wire `EchoAccuracy` into a `dotnet test` target (or `Scanner.Cli --run-fixtures`) that runs against the fixture corpus and fails the run if field accuracy regresses below a checked-in floor.
- [ ] Adopt the acceptance bar from §2e before any README claim of "95% accurate": ≥25 1080p samples, ≥95% combined field accuracy, no field silently defaulted (audit the sonata OCR-text/catalog-default fallback and every other silent-fallback path to confirm each is flagged in output), low-confidence fields never auto-committed.
- [ ] Once this exists, re-baseline the §3 numbers (97.7% / 94.7% / 100%) against the fixture corpus instead of the ad-hoc session directory.

---

## 7. TODO — Accuracy Priority 3: Substat Recall on Leveled Echoes

**Goal: close the 1.62-avg-substats/echo gap — the open lever this repo's own status notes already point at ("Y-clustering + `StatPixelMatcher`").**

- [ ] Confirm the substat block's row-splitting is **Y-position-clustered** rather than fixed row-count/spacing offsets — a 1-substat unleveled echo and a 5-substat maxed echo don't lay out identically; if row rects are currently static, that alone would explain worse recall on leveled (more rows, more layout variance) echoes.
- [ ] For each substat row, run `StatPixelMatcher` **in parallel with** OCR, not only as an HP-specific fallback — cross-check pixel-matched stat type against the OCR'd label the same way sonata already cross-checks icon vs. text.
- [ ] Confirm `TunableRolls` only *validates/corrects* an OCR'd numeric value to the nearest legal roll, and never fabricates a substat OCR failed to detect at all. Add a unit test asserting a fully-missed substat row stays absent rather than being backfilled from the roll table.
- [ ] Once §6's fixture corpus exists, check whether the substat gap concentrates in rows 4–5 (bottom of block, likelier clipped by panel-scroll boundary) or is evenly distributed — tells you whether this is a **region-rect problem** (panel not fully captured) or an **OCR problem** (§5 should already have addressed that).

---

## 8. TODO — Accuracy Priority 4: Capture-Time Frame Stability (`AlephalSonata` live path only)

**Goal: port Tacet-Lab's "stable frame sampler and fingerprint gate" + session/frame/job ID
discipline (`docs/architecture.md`) to the live crawl, so blurry/mid-animation frames never reach `EchoRecognizer`.**

- [ ] After each grid-cell click / scroll burst in `AutoNavigator`, replace the fixed settle sleep with a lightweight frame-stability check (hash/diff two captures a few ms apart; require a match before handing the frame to `EchoRecognizer`).
- [ ] Extend `AlephalSonata`'s existing trace logging with explicit session/frame/job IDs so a superseded frame (Alt+Tab mid-scan, safety halt fired) can be positively dropped instead of partially processed.
- [ ] Add duplicate-frame suppression for the crawl: skip re-OCRing a card if the picker grid didn't scroll/change since the last step (e.g. end-of-list).
- [ ] Once this exists, capture a few known-bad pre-settle frames into the §6 fixture corpus as a **negative test** — assert the pipeline re-captures or flags them rather than silently emitting a low-confidence parse.

---

## 9. Longer-Horizon Levers (not urgent)

- [ ] Consider a Tesseract dictionary constrained to the closed vocabulary of stat labels + echo/sonata names (from `GameDatabase`'s catalog) — WuWa's field text is bounded, not open text; constraining recognition itself (not just post-hoc `FuzzyMatcher`) could give a further gain.
- [ ] Evaluate extending icon/pixel-signature matching (already used for rarity + sonata) to any other iconified field in the panel — every field moved from OCR to deterministic pixel matching removes it from the OCR-accuracy conversation entirely, per the project's own "Pixel-First vs. Brittle OCR" design philosophy (README).
- [ ] Once §5 lands, re-run the WinOcr/Tesseract A/B (`--no-winocr-fallback` / `SONORO_NO_WINOCR=1`) **per field type**, not pipeline-wide — the current regression implies Tesseract wins on main-stat but loses on name/substats; let `ScannerConfig` pick the better engine per field independently.
- [ ] Extend `NOTICES.md`'s "adapted portions" list to cover the §5 preprocessing port and the §6 fixture/accuracy-harness port once implemented — both are structurally adapted from GPL-3.0 `../Tacet-Lab` docs, same as the existing sonata-icon-matcher entry.
- [ ] 1440p fixtures + resolution-branching logic — deliberately deferred, see header note. Revisit once 1080p accuracy is fixture-verified and stable.

---

## 10. Known Issues / Blockers

| Issue                                                   | Notes                                                                                       |
| ------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| ~~`TesseractOcr.cs` not yet wired in `EchoRecognizer`~~ | ✅ Done: `OcrRegionAsync`/`OcrLinesAsync` are Tesseract-primary + gated WinOcr fallback      |
| ~~`TesseractOcr.cs` iterator API may be wrong~~         | ✅ Done: uses `iter.Next(PageIteratorLevel.TextLine)`                                        |
| ~~Tesseract NuGet not added to `.csproj`~~              | ✅ Done: `Tesseract 5.2.0` in `SonoroScore.Scanner.csproj`                                   |
| ~~`SonataSignatureMatcher.cs` does not exist~~          | ✅ Done: versioned loader + `Match` + `CheckVersion` + `EnsureUpdatedAsync`                  |
| ~~`sonata_signatures.json` not extracted yet~~          | ✅ Done: 34 sets, versioned object format (legacy array still accepted)                      |
| ~~`EchoRegions.SonataIcon` rect not defined~~           | ✅ Done                                                                                      |
| **Tesseract-primary regression: name/substat recall dropped** | ⬅ **Active — this is what §5 targets**                                                |
| Test run blocked: no .NET SDK on PATH                   | Run Scanner.Cli re-run command (§3) once SDK is available                                    |
| ⚠️ License: Tacet-Lab is GPL-3.0, not MIT               | ✅ NOTICES.md + headers corrected; extend "adapted portions" list per §9 as §5/§6 land       |
| No fixture corpus / accuracy is eyeballed, not measured | ⬅ **Active — this is what §6 targets**                                                     |
| Substat detection gap on leveled echoes (avg 1.62/echo) | ⬅ **Active — this is what §7 targets**                                                     |
