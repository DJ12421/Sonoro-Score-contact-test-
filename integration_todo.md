# Sonoro-Score Scanner – Integration Doc & TODO
> **Attribution**: OCR pipeline strategy, region definitions, and icon-signature matcher are adapted from [Tacet-Lab](https://github.com/DJ12421/Tacet-Lab) under the GPL-3.0 License. See NOTICES.md.
> **Status (2026-09-27):** Priority 1 + 2 implemented (uncommitted working tree). Priority 3 thresholds codified in `ScannerConfig.cs`; re-run pending (no .NET SDK on PATH). Priority 4 + export done in code, pending test run.

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

### Files in `SonoroScore.Scanner/`

| File | Role | Status |
|------|------|--------|
| `EchoRecognizer.cs` | Main pipeline orchestrator | ✅ Tesseract-primary + WinOcr gate (`ScannerConfig`) |
| `EchoRegions.cs` | Region rectangles (% of panel) | ✅ incl. `SonataIcon` |
| `WinOcr.cs` | Windows Media OCR wrapper | ✅ fallback (gated by `ScannerConfig.UseWindowsOcrFallback`) |
| `TesseractOcr.cs` | Tesseract OCR wrapper | ✅ wired as primary (fixed iterator API) |
| `ScannerConfig.cs` | QA knobs: OCR gate + thresholds | ✅ **NEW** |
| `ImagePreprocessor.cs` | Contrast boost + upscale | ✅ exists (in WinOcr.cs file) |
| `GameDatabase.cs` | Echo catalog (nanoka API + local cache) | ✅ exists |
| `FuzzyMatcher.cs` | Fuzzy string matching | ✅ exists |
| `StatParser.cs` | Stat label → StatKey | ✅ exists |
| `StatPixelMatcher.cs` | HP pixel fallback | ✅ exists |
| `RarityClassifier.cs` | Star-count from pixel band | ✅ exists |
| `TunableRolls.cs` | Snap substat values to valid rolls | ✅ exists |
| `ScanResult.cs` | EchoScanResult / FieldResult / SubstatResult | ✅ exists |
| **`SonataSignatureMatcher.cs`** | Icon pixel-signature match (Tacet-Lab) | ✅ implemented (versioned + auto-update) |
| `EchoExportModel.cs` | Shared export DTO | ✅ **NEW** |
| `TacetLabExporter.cs` | `tacet-lab-backup.json` (schema v7) | ✅ **NEW** |
| `GoodExporter.cs` | GOOD v3 bridge (`sonoro-good.json`) | ✅ **NEW** |

---

## 2. Tacet-Lab Strategy to Copy

### 2a. Sonata Icon Region
From `Tacet-Lab/src/scanner/regions.ts` line 47:
```
sonata region: x=0.88, y=0.008, width=0.115, height=0.065  (relative to panel)
```
This crops the **icon** to the right of "Sonata Effect" heading.

### 2b. Signature Matching Algorithm (`visual.ts`)
1. Crop the sonata icon region from the panel bitmap.
2. Resize the crop to **16×16** pixels.
3. Flatten the 16×16 pixels into a 256-element array.
4. **Normalize**: each pixel channel value → `clamp(value, 0, 255) >> 2` (i.e. divide by 4, gives 0–63 range matching signature values).
5. Compute **dot-product similarity** against every entry in `generatedEchoSignatures`.
6. Return the entry with the highest similarity score.

### 2c. Signature Data
`Tacet-Lab/src/game-data/scanner-signatures.generated.ts` exports:
```ts
generatedEchoSignatures = [
  { name: "Trailblazing Star", cost: 4, signature: [int8 × 256] },
  ...
]
```
This must be **converted to a C# resource** (JSON file or embedded array).

---

## 3. TODO List

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

---

### ✅ Priority 2 – Sonata Icon Signature Matcher — DONE (working tree)

- [x] **Export signatures from Tacet-Lab** → `sonata_signatures.json` (34 sets, now versioned
  `{"version":"3.6","scannerSignatureVersion":"3.6","signatures":[...]}`).

- [x] **Create `SonataSignatureMatcher.cs`** (version-aware loader + `Match`).

- [x] **Define sonata icon region in `EchoRegions.cs`** (`SonataIcon = 0.88, 0.008, 0.115, 0.065).

- [x] **Wire into `EchoRecognizer`** (step 9; threshold `ScannerConfig.SonataIconMinConfidence = 0.70`;
  OCR text fallback retained; sonata source logged to warnings for audit).

---

### 🟡 Priority 3 – Quality & Reliability — PARTIAL (code done, test run pending)

- [x] **Confidence thresholds** – codified in `ScannerConfig.cs` (no more notional 0.5):
  `OcrMinTextLength=2` + empty-fallback, `SonataIconMinConfidence=0.70`,
  `EchoNameMinConfidence=0.68`, `SonataTextMinConfidence=0.55`.
  Calibrate against `publish\AlephalSonata\aleph_images` via Scanner.Cli; compare
  `Name/MainStat/Sonata` rates + new `icon vs OCR` sonata breakdown.
  QA Tesseract-only A/B: `--no-winocr-fallback` (or `SONORO_NO_WINOCR=1`).

- [x] **Tessdata setup docs** – `README` section present (eng.traineddata → `./tessdata/`, DLLs via NuGet).

- [x] **Attribution file** – `NOTICES.md` credits `https://github.com/DJ12421/Tacet-Lab`
  (GPL-3.0; corrected from MIT) + adapted portions list.

- [ ] **Re-run test suite** after wiring (BLOCKED: no .NET SDK on PATH at time of writing):
  ```powershell
  dotnet run --project src/SonoroScore.Scanner.Cli -- --dir publish\AlephalSonata\aleph_images\session_20260927_204453 --export-tacet-auto --export-good-auto
  ```
  Target: sonata field reads from icon match (warnings `Sonata from icon match…`, conf > 0.70);
  record icon-vs-OCR counts from the new summary lines.

---

### ✅ Priority 4 – Nice-to-Haves — DONE in code (test run pending)

- [x] **Signature version check** — `sonata_signatures.json` is now versioned
  (`version` + `scannerSignatureVersion` = `3.6`); `SonataSignatureMatcher.CheckVersion(log)`
  warns on mismatch vs `ExpectedSignatureVersion` and `GameDatabase.DataVersion`;
  `EchoRecognizer` surfaces it per-scan. Loader still accepts legacy bare-array files.

- [x] **Auto-update signatures** — `SonataSignatureMatcher.EnsureUpdatedAsync(url?, forceRefresh, log)`
  (same pattern as `GameDatabase.LoadAsync`); CLI flag `--update-signatures [--signature-url …]`.

- [x] **WinOcr removal gate** — `ScannerConfig.UseWindowsOcrFallback` (default true);
  CLI `--no-winocr-fallback`, env `SONORO_NO_WINOCR=1` for QA A/B before any removal.

- [x] **1-click exports** (README roadmap) — `TacetLabExporter` (`tacet-lab-backup.json`,
  schemaVersion 7, `gameDataVersion` `nanoka-3.6-catalog`) + `GoodExporter`
  (`sonoro-good.json`, GOOD v3 bridge with `wuwa*` lossless fields).
  CLI: `--export-tacet-auto/--export-tacet <path>`, `--export-good-auto/--export-good <path>`;
  Debugger auto-writes both per scan; GUI toolbar has `⬆ Tacet-Lab` + `⬆ GOOD` buttons.

---

## 4. Known Issues / Blockers

| Issue | Notes |
|-------|-------|
| ~~`TesseractOcr.cs` not yet wired in `EchoRecognizer`~~ | ✅ Done: `OcrRegionAsync`/`OcrLinesAsync` are Tesseract-primary + gated WinOcr fallback |
| ~~`TesseractOcr.cs` iterator API may be wrong~~ | ✅ Done: uses `iter.Next(PageIteratorLevel.TextLine)` |
| ~~Tesseract NuGet not added to `.csproj`~~ | ✅ Done: `Tesseract 5.2.0` in `SonoroScore.Scanner.csproj` |
| ~~`SonataSignatureMatcher.cs` does not exist~~ | ✅ Done: versioned loader + `Match` + `CheckVersion` + `EnsureUpdatedAsync` |
| ~~`sonata_signatures.json` not extracted yet~~ | ✅ Done: 34 sets, versioned object format (legacy array still accepted) |
| ~~`EchoRegions.SonataIcon` rect not defined~~ | ✅ Done |
| Test run blocked: no .NET SDK on PATH | Run Scanner.Cli re-run command above once SDK is available |
| ⚠️ License: Tacet-Lab is GPL-3.0, not MIT | ✅ NOTICES.md + headers corrected; combined scanner distribution needs GPL-compatible review |

