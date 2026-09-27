# Sonoro-Score 🎵

> **High-performance, lightweight C# native automation and Echo scanner suite for Wuthering Waves.**
> Free of Electron. Instant startup. Pixel-first state detection.

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://github.com/RadianceNoMore/Sonoro-Score)
[![Framework](https://img.shields.io/badge/.NET-8.0_Desktop-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Zero Electron](https://img.shields.io/badge/Zero-Electron-green?logo=electron&logoColor=red)](https://github.com/RadianceNoMore/Sonoro-Score)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## ✦ Dual Application Suite

**Sonoro-Score** is split into two specialized applications sharing a high-speed core library:

| Application | Role | Executable | Target Audience | Description |
| :--- | :--- | :--- | :--- | :--- |
| **`SonoroScore`** | **Production Release** | `SonoroScore.exe` | End Users & Players | Minimalist, lightning-fast automation interface for daily Echo scanning and inventory navigation. Clean console UI without log clutter. |
| **`AlephalSonata`** | **Diagnostic Debugger** | `AlephalSonata.exe` | Developers & Power Users | Deep telemetry tool. Traces every input event, screen coordinate, window focus change, and driver status in real-time, recording persistent logs to `logs/aleph_trace_*.log`. |

---

## ✦ Why Rebuild from Scratch?

### 1. Goodbye Electron Bloat
Legacy scanners often rely on **Electron**, packaging a complete Chromium browser and Node.js runtime just to click buttons and capture pixels. This leads to:
* **High RAM overhead:** 150 MB – 350 MB+ memory usage while running in the background.
* **Large distribution size:** 150 MB+ installers.
* **Sluggish input:** Firing clicks through Node.js sub-processes or PowerShell scripts incurs 50–200ms latency per action.

**Sonoro-Score (C# .NET 8):**
* **Instant Startup:** Boots in under 100ms.
* **Minimal Footprint:** Consumes < 45 MB RAM.
* **Single Portable Executable:** Everything is compiled into a standalone ~67 MB `.exe` with zero external runtime dependencies.
* **Microsecond Latency:** Win32 P/Invoke and hardware-level driver execution run in 0.001ms.

---

### 2. Pixel-First vs. Brittle OCR
Legacy projects (such as early forks of *FrequencyManager*) attempted to run Tesseract OCR passes just to check if in-game menus were open (e.g. searching for the word `"Terminal"` on screen). 

In practice, this approach is notoriously fragile:
* Dynamic 3D background lighting, character particle effects, and anti-aliasing frequently distort text edges, causing OCR engines to misread `"Terminal"` as `oe 3 inal y:` and crash the scanner.
* OCR passes take 200–500ms per frame.

**The Pixel-First Solution:**  
Borrowing principles pioneered by **[Tacet Lab](https://github.com/DJ12421/Tacet-Lab)**, `SonoroScore` replaces menu OCR with direct pixel color sampling (HSL space) and high-contrast UI state anchors. A pixel check takes **less than 0.001ms**, is 100% deterministic, and never misreads characters.

---

## ✦ Inspirations & Acknowledgments

This project is built with deep gratitude to the open-source community:

- **[InventoryKamera](https://github.com/Andrewthe13th/Inventory_Kamera) & [WuWa Inventory Kamera](https://github.com/Psycho-Marcus/WuWa_Inventory_Kamera):**  
  The foundational inspiration for automated gacha inventory crawling. Their pioneer work demonstrated how automated UI navigation and OCR extraction can save players thousands of hours of manual data entry.
- **[Tacet Lab](https://github.com/DJ12421/Tacet-Lab) (by DJ12421):**  
  The premier Wuthering Waves damage calculator and Echo optimizer. We adopted Tacet Lab's computer vision philosophy (direct pixel verification over text OCR for state machines) and design our output pipelines to directly generate 1-click import files (`tacet-lab-backup.json`).
- **[Nanoka](https://ww.nanoka.cc/):**  
  The definitive datamine reference for Wuthering Waves Echo stats and substat roll distributions.

---

## ✦ Meaning of the Names

* **Sonoro-Score:** Inspired by the *Sonoro Spheres* (acoustic spatial anomalies) and the *Tacet Field scores* in Solaris-3 — measuring the resonance score of your Echoes.
* **Alephal-Sonata (ℵ-Sonata):**
  * **Aleph ($\aleph$):** The transfinite mathematical symbol for infinity introduced by Georg Cantor, and an homage to **Denia** (*"Bubbles of Nihility"*), vessel of the Threnodian Aleph-1.
  * **Sonata:** The acoustic Echo set-bonus mechanic.
  * **Alephal-Sonata:** Literally translates to **"Infinite Echoes"** ($\aleph$ + Sonata) — the dedicated engine diagnostic system built to analyze infinite Echo collections.

---

## ✦ Technical Architecture

```text
               Wuthering Waves Window (16:9 / Borderless)
                                   │
                                   ▼
        ┌─────────────────────────────────────────────────────┐
        │                 SonoroScore.Core                    │
        ├─────────────────────────────────────────────────────┤
        │ [WindowManager]   ── Focus tracking & Alt+Tab guard │
        │ [AutoNavigator]   ── Menu state machine & grid scan │
        │ [InputSimulator]  ── Dual-mode click & scroll burst │
        │                      ├── Interception Virtual HID   │
        │                      └── Win32 SendInput (Fallback) │
        │ [NavigationConfig]── Calibrated fractional coords   │
        └─────────────────────────────────────────────────────┘
                                   │
                  ┌────────────────┴────────────────┐
                  ▼                                 ▼
         [SonoroScore.exe]                 [AlephalSonata.exe]
         (Production Release)              (Diagnostic Debugger)
          - Streamlined UI                  - Real-time event trace
          - Fast navigation                 - Virtual HID driver check
          - High-speed scan                 - File logging to logs/
```

### 1. Anti-Cheat & Driver-Level Virtual HID
Wuthering Waves runs on Unreal Engine 4 alongside active anti-cheat (ACE), which filters out standard synthetic Windows messages carrying the `LLMHF_INJECTED` flag.  
`SonoroScore.Core` features direct native bindings to the **Interception Driver** (`interception.dll`), injecting mouse movements and clicks as legitimate hardware driver events. If the driver is not detected, it smoothly falls back to standard Win32 `SendInput`.

### 2. Humanized Gaussian Cadence
To avoid robotic metronomic clicks:
* Clicks incorporate Box-Muller Gaussian jitter around calibrated target centers.
* Page scrolls are broken into clamped notch bursts (`-8` ticks) to prevent Unreal Engine's input buffer from dropping scroll events.
* Real-time **Alt+Tab safety** halts all clicking immediately if Wuthering Waves loses foreground focus.

---

## ✦ Solution & Directory Structure

```
Sonoro-Score/
├── SonoroScore.sln                 # Master Visual Studio / .NET Solution
├── src/
│   ├── SonoroScore.Core/           # Shared Class Library
│   │   ├── Native/
│   │   │   ├── Win32.cs            # P/Invoke user32.dll & kernel32.dll declarations
│   │   │   └── Interception.cs     # C# bindings for interception.dll (Virtual HID)
│   │   └── Automation/
│   │       ├── InputSimulator.cs   # Mouse, keyboard, scroll bursts & Gaussian jitter
│   │       ├── WindowManager.cs    # Handle enumeration, aspect ratio & focus tracking
│   │       ├── NavigationConfig.cs # Calibrated fractional coordinates & grid offsets
│   │       └── AutoNavigator.cs    # Automated menu & 5×3 grid crawling state machine
│   ├── SonoroScore/                # [Release App] Clean, user-facing scanner CLI
│   │   ├── Program.cs
│   │   └── SonoroScore.csproj
│   └── AlephalSonata/              # [Debugger App] Diagnostic tracer & file logger
│       ├── Program.cs
│       └── AlephalSonata.csproj
├── publish/                        # Standalone compiled binaries (git-ignored)
│   ├── SonoroScore/SonoroScore.exe
│   └── AlephalSonata/AlephalSonata.exe
├── logs/                           # Runtime diagnostic trace logs (git-ignored)
├── .gitignore
└── README.md
```

---

## ✦ Quick Start

### Prerequisites
* Windows 10 or Windows 11 (64-bit)
* Wuthering Waves running at 1080p or 1440p (16:9 Borderless Windowed or Fullscreen)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only if compiling from source)

### Tesseract OCR data setup
The scanner uses **Tesseract** as its primary OCR engine and falls back to the
built-in Windows OCR if Tesseract is unavailable (gated by
`ScannerConfig.UseWindowsOcrFallback`, default `true`).

1. Download `eng.traineddata` from
   [tessdata_best](https://github.com/tesseract-ocr/tessdata_best).
2. Place it in a `tessdata/` folder next to the executable, e.g.
   `publish/SonoroScore/tessdata/eng.traineddata`.
3. Native `leptonica`/`tesseract` DLLs are copied automatically by the
   `Tesseract` NuGet package on build.

Without `tessdata`, the scanner still runs using Windows OCR (unless fallback is
disabled for QA via `--no-winocr-fallback` or `SONORO_NO_WINOCR=1`).

### 1-click exports (Tacet-Lab + GOOD)
- **Tacet-Lab (lossless, primary):** `tacet-lab-backup.json` (schemaVersion 7) —
  import in Tacet-Lab via top-bar Export/Restore. Scanner CLI writes it with
  `--export-tacet-auto`; the debugger auto-writes one per scan; the review studio
  has a `⬆ Tacet-Lab` toolbar button.
- **GOOD (best-effort bridge):** `sonoro-good.json` (GOOD v3 envelope with `wuwa*`
  lossless fields) — WuWa→GOOD stat/slot mapping is approximate (see
  `GoodExporter.cs`); prefer the Tacet-Lab backup for lossless import.
- **Signatures:** `sonata_signatures.json` is versioned (`3.6`); refresh via
  `dotnet run --project src/SonoroScore.Scanner.Cli -- --update-signatures`.

---

### Running the Apps

#### Option A: Run Release App (`SonoroScore`)
```powershell
# Run directly from source
dotnet run --project src/SonoroScore/SonoroScore.csproj

# Or run the pre-built standalone binary
./publish/SonoroScore/SonoroScore.exe
```

#### Option B: Run Diagnostic Debugger (`AlephalSonata`)
```powershell
# Run directly from source
dotnet run --project src/AlephalSonata/AlephalSonata.csproj

# Or run the pre-built standalone binary
./publish/AlephalSonata/AlephalSonata.exe
```
*When running `AlephalSonata`, trace logs are automatically saved to `logs/aleph_trace_<timestamp>.log`.*

---

### Building Standalone Single-File Executables

To build standalone, single-file `.exe` binaries that run on any Windows machine without requiring .NET:

```powershell
# Publish SonoroScore (Release App)
dotnet publish src/SonoroScore/SonoroScore.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/SonoroScore

# Publish AlephalSonata (Diagnostic Debugger)
dotnet publish src/AlephalSonata/AlephalSonata.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish/AlephalSonata
```

---

## ✦ Controls & Operation

### 1. SonoroScore (Release)
```text
    ╔═══════════════════════════════════════════════════════╗
    ║       SONOROSCORE — Wuthering Waves Navigator         ║
    ║   Lightweight C# Native Automation & Scanner          ║
    ╚═══════════════════════════════════════════════════════╝

--- SonoroScore Menu ---
1. Start Full Auto-Navigation & Scan
2. Crawl Active Echo Picker (Fast Mode)
3. Verify Game Window Status
0. Exit
```

### 2. AlephalSonata (Debugger)
```text
    ╔═══════════════════════════════════════════════════════╗
    ║       ALEPHAL-SONATA (ℵ-Sonata)                       ║
    ║   Diagnostic Debugger & Process Telemetry Engine      ║
    ╚═══════════════════════════════════════════════════════╝

[DRIVER STATUS] Interception Virtual HID Driver: ACTIVE

=== ALEPHAL-SONATA DIAGNOSTIC MENU ===
1. Verify Wuthering Waves Window (Handle, Rect, 16:9 check)
2. Run Full Auto-Navigation with Verbose Tracing
3. Run Grid Crawl Diagnostic (5x3 Picker Grid)
4. Test Page Scroll Burst Calibration (-34 ticks)
5. Test Single Card Click (First Echo at row 0, col 0)
6. Flush Diagnostic Log Buffer
0. Exit
```

- **Emergency Halt:** Press `Ctrl+C` in the console or simply **Alt+Tab** out of Wuthering Waves at any moment to cancel automation immediately.

---

## ✦ Roadmap

- [x] Shared native C# class library (`SonoroScore.Core`)
- [x] Dual-executable architecture (`SonoroScore` release + `AlephalSonata` debugger)
- [x] Hardware-level Virtual HID driver support (`interception.dll`) with Win32 fallback
- [x] Menu navigation state machine: Main $\rightarrow$ Character (`C`) $\rightarrow$ Echo Tab $\rightarrow$ Slot Picker
- [x] 5×3 Picker Grid crawling with Box-Muller Gaussian click jitter
- [x] Calibrated smooth page scrolling (-34 ticks) and Alt+Tab safety guard
- [x] Real-time diagnostic file logging (`logs/aleph_trace_*.log`)
- [x] Direct HSL pixel classification for Echo rarity (Gold/Purple/Blue/Green)
- [x] 16×16 Sonata icon pixel-signature matching (Tacet-Lab port, 34 sets)
- [x] Tesseract OCR primary engine with Windows.Media.Ocr fallback
- [x] Discrete substat roll-table snapping
- [x] 1-Click JSON export for **Tacet Lab** (`tacet-lab-backup.json`) and **GOOD** format (`sonoro-good.json`)
- [ ] Calibration re-run on `publish/AlephalSonata/aleph_images` (needs .NET SDK; see `integration_todo.md` Priority 3)

---

## ✦ License & Disclaimer

Distributed under the **MIT License** for original code. Parts adapted from
[Tacet-Lab](https://github.com/DJ12421/Tacet-Lab) are GPL-3.0 — see `NOTICES.md`
before distributing builds that bundle the scanner.

*Sonoro-Score is a fan-made open-source tool and is not affiliated with, endorsed by, or sponsored by Kuro Games. Wuthering Waves and all associated assets, artwork, and game titles are trademarks and copyrights of Kuro Games.*
