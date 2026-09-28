# Sonoro-Score 🎵

> **C# (.NET 8) automation and Echo scanner suite for Wuthering Waves.**
> Pixel-driven navigation, hybrid-OCR echo recognition, review studio, and
> one-click exports for Tacet-Lab.

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://github.com/RadianceNoMore/Sonoro-Score)
[![Framework](https://img.shields.io/badge/.NET-8.0_Desktop-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## ✦ What It Is

Two Windows applications plus a shared scanner library:

| Application | Executable | Role |
| :--- | :--- | :--- |
| **`SonoroScore` (SS)** | `SonoroScore.exe` | **Echo Review & Verification Studio** (WinForms GUI). Load captures + scan results side by side, correct fields, re-scan single echoes, export verified inventories. |
| **`AlephalSonata`** | `AlephalSonata.exe` | **Diagnostic debugger & capture tool** (console). Window inspection, auto-navigation with verbose tracing, raw screen-dataset capture, one-click OCR test-suite runs with file logging to `logs/aleph_trace_*.log`. |
| `SonoroScore.Scanner` | *(library)* | **Recognition pipeline.** Panel extraction, hybrid OCR, icon-signature matching, roll-table snapping, Tacet-Lab/GOOD exporters. |
| `SonoroScore.Scanner.Cli` | *(dev tool)* | **Batch test runner.** Runs the pipeline over an image folder, prints detection rates, writes scan + export JSONs. |
| `SonoroScore.Core` | *(library)* | **Automation primitives.** Window focus tracking, Interception virtual-HID input with Win32 `SendInput` fallback, Gaussian click jitter, scroll bursts, Alt+Tab safety guard. |

---

## ✦ Scanner Pipeline

Per echo screenshot (1080p echo-detail panel):

1. **Panel extraction** — crops the right-side detail strip (`EchoRegions`, panel-relative rects tunable in-app, see below).
2. **Hybrid OCR** — Tesseract 5 (LSTM-only, DPI 300, per-region page-segmentation modes, closed stat/name vocabularies) as primary; Windows OCR as gated fallback. Names route to Windows OCR (measured best on the stylized font); stat strips/blocks run Tesseract.
3. **Field preprocessing** — per-region pipeline ported from Tacet-Lab: ×3 enlarge, grayscale, 4/96 percentile normalization, polarity correction, Otsu threshold to black-on-white, plus name/substat cleanup filters.
4. **Sonata icons** — 16×16 pixel-signature matching over 34 sets (no OCR involved).
5. **Rarity** — direct HSL pixel classification, no OCR.
6. **Parsing** — fuzzy catalog match (live Nanoka data + local cache), stat-label parsing with OCR-artifact recovery, substat values snapped to legal tunable rolls, Y-position row slotting.

### Measured accuracy (300-image 1080p corpus, 2026-09-28)

| Field | Rate |
|---|---|
| Echo name | 99.7% |
| Main stat | 99.0% |
| Second main stat | 98.7% |
| Sonata set (icon match) | 100% |
| Fully complete echoes | 35.3% |
| Avg substats / echo | 2.54 |

`dotnet test` runs the same checks against a 25-fixture 1080p regression corpus plus unit tests — green. Details and A/B history: `TODO.md`.

---

## ✦ Review Studio (SS)

- **📁 Open Folder** — loads echo images together with the folder's latest scan JSON (no more pending stubs).
- **📄 Load Scan JSON** — loads results and relinks screenshots by file name if paths went stale (asks for the folder when needed).
- **Editor** — identity, main + second main stat, five tuned substat rows with roll validation, raw OCR evidence per field, search/filter, keyboard flow (`A`/`D` navigate, `Space` verify, `Ctrl+S` save).
- **⚡ Re-Scan Current** — re-runs the pipeline on one echo with live catalog.
- **⬆ Tacet-Lab / ⬆ GOOD** — one-click exports of the reviewed inventory.
- **◈ Areas** — visual scan-area config: all 12 regions as draggable dashed boxes over the current echo panel, Left/Top/Right/Bottom numeric entry (panel-relative 0–1), arrow-key nudging at 1 px/press. Saves to `regions.override.json` next to the exe; delete it (or Reset) to restore compiled defaults.

Window and dialog layouts (sizes + splitter positions) are remembered across sessions.

---

## ✦ 1-Click Exports

- **Tacet-Lab (lossless, primary):** `tacet-lab-backup.json` (schema v7) — restore in Tacet-Lab via top-bar Export/Restore.
- **GOOD (best-effort bridge):** `sonoro-good.json` (GOOD v3 envelope with `wuwa*` lossless fields). WuWa→GOOD stat/slot mapping is approximate — prefer the Tacet-Lab backup. Both exporters carry the primary main stat only (single-main schemas); the second main stat lives in SS verified JSON.

---

## ✦ Quick Start

### Prerequisites
* Windows 10/11 (64-bit)
* Wuthering Waves at **1080p** (16:9 Borderless or Fullscreen) — scanning is calibrated for 1080p
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only to compile from source)

### Tesseract OCR data setup
1. Download `eng.traineddata` ([tessdata_best](https://github.com/tesseract-ocr/tessdata_best) or `_fast`).
2. Place it in a `tessdata/` folder next to the executable, e.g. `publish/SonoroScore/tessdata/eng.traineddata`.
3. Native `leptonica`/`tesseract` DLLs ship via the `Tesseract` NuGet package on build.

Without `tessdata`, everything still runs on Windows OCR (unless disabled via `--no-winocr-fallback` / `SONORO_NO_WINOCR=1`).

### Running the Apps
```powershell
# Review studio
dotnet run --project src/SonoroScore/SonoroScore.csproj
./publish/SonoroScore/SonoroScore.exe

# Debugger / capture tool (logs to logs/aleph_trace_<timestamp>.log)
dotnet run --project src/AlephalSonata/AlephalSonata.csproj
./publish/AlephalSonata/AlephalSonata.exe

# Batch scanner: full corpus + exports
dotnet run --project src/SonoroScore.Scanner.Cli/SonoroScore.Scanner.Cli.csproj -c Release -- `
  --dir publish/AlephalSonata/aleph_images/session_20260927_204453 `
  --export-tacet-auto --export-good-auto

# Regression suite (25 fixtures + unit tests)
dotnet test src/SonoroScore.Scanner.Tests/SonoroScore.Scanner.Tests.csproj -c Release
```

### Building Standalone Single-File Executables
```powershell
dotnet publish src/SonoroScore/SonoroScore.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/SonoroScore
dotnet publish src/AlephalSonata/AlephalSonata.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/AlephalSonata
```
Produces ~170 MB self-contained `.exe` files (Tesseract natives + WinForms runtime included).

---

## ✦ Controls & Operation

### AlephalSonata menu
```text
1. Inspect Game Window Rect & Handle
2. Test Single Click at Cell (0,0)
3. Test Page Scroll Calibration
4. Run Full Auto-Navigation with Verbose Tracing
5. Run Active Picker Crawl with Verbose Tracing
6. Capture Raw Screen Dataset (aleph_images/session_<timestamp>)
7. Test Step-by-Step Nav ('C', Sidebar, Slot) & Timings
8. Open Images Directory
9. Open Logs Directory
A. Run OCR Echo Scanner on aleph_images test suite -> JSON (+ Tacet/GOOD exports)
0. Exit
```

### SonoroScore toolbar
```text
📁 Open Folder | 📄 Load Scan JSON | 💾 Save Verified JSON | ⚡ Re-Scan Current
⬆ Tacet-Lab | ⬆ GOOD | ◈ Areas
```

- **Emergency halt:** `Ctrl+C` in console, or **Alt+Tab** out of the game — automation stops immediately on focus loss.
- **Per-scan QA flags (CLI):** `--limit N`, `--refresh`, `--out <path>`, `--no-winocr-fallback`,
  `--name-engine auto|tesseract|windows`, `--update-signatures`, `--diag-tess`, `--dump-lines <img>`.

---

## ✦ Solution Layout

```
Sonoro-Score/
├── SonoroScore.sln
├── Directory.Build.props          # shared version (v1.6.0)
├── NOTICES.md                     # third-party licenses (Tacet-Lab: GPL-3.0)
├── TODO.md                        # accuracy plan + A/B history
├── src/
│   ├── SonoroScore.Core/          # window, Interception HID / SendInput, jitter, navigator
│   ├── SonoroScore.Scanner/       # EchoRecognizer, EchoRegions (+regions.override.json),
│   │                              # TesseractOcr/WinOcr, EchoFieldPreprocessor (+legacy),
│   │                              # SonataSignatureMatcher, GameDatabase, FuzzyMatcher,
│   │                              # StatParser/PixelMatcher, TunableRolls, RarityClassifier,
│   │                              # exporters, EchoAccuracy, sonata_signatures.json
│   ├── SonoroScore.Scanner.Cli/   # batch runner + --diag-tess/--dump-lines/--dump-panel
│   ├── SonoroScore.Scanner.Tests/ # xUnit: scoring/invariant unit tests + 25-fixture
│   │                              # 1080p corpus (fixtures/echoes/english-1080p/)
│   ├── SonoroScore/               # review studio (MainReviewForm, RegionConfigDialog)
│   └── AlephalSonata/             # debugger + capture tool
├── publish/                       # built exes (git-ignored)
└── logs/                          # trace logs (git-ignored)
```

---

## ✦ Inspirations & Acknowledgments

- **[Tacet Lab](https://github.com/DJ12421/Tacet-Lab) (by DJ12421):** Wuthering Waves optimizer whose scanner architecture this project ports — region layout, OCR pipeline shape, preprocessing strategy, icon-signature matching, backup format. Adapted portions are GPL-3.0; see `NOTICES.md`.
- **[Nanoka](https://ww.nanoka.cc/):** datamine source for the live echo catalog (names, costs, sonata sets) and roll tables.

---

## ✦ License & Disclaimer

Distributed under the **MIT License** for original code. Parts adapted from
[Tacet-Lab](https://github.com/DJ12421/Tacet-Lab) are GPL-3.0 — see `NOTICES.md`
before distributing builds that bundle the scanner.

*Sonoro-Score is a fan-made open-source tool and is not affiliated with, endorsed by, or sponsored by Kuro Games. Wuthering Waves and all associated assets, artwork, and game titles are trademarks and copyrights of Kuro Games.*
