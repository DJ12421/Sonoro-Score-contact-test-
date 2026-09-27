# Alephal-Sonata (ℵ-Sonata)

> **"Infinite Echoes"** — Lightweight, high-performance C# native automation and Echo navigation engine for **Wuthering Waves**.

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)](https://github.com/RadianceNoMore/Alephal-Sonata)
[![Framework](https://img.shields.io/badge/.NET-8.0_Desktop-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Zero Electron](https://img.shields.io/badge/Zero-Electron-green?logo=electron&logoColor=red)](https://github.com/RadianceNoMore/Alephal-Sonata)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## ✦ Overview & Motivation

Wuthering Waves players routinely farm hundreds of Echoes every week, frequently hitting the 2,000 inventory ceiling. While powerful web optimizers like **[Tacet Lab](https://github.com/DJ12421/Tacet-Lab)** have revolutionized damage calculations and build analysis, getting hundreds of Echoes into the optimizer has remained a major pain point: players either have to type them by hand or manually click through screen shares.

Existing automated scanners often rely on **Electron**, dragging along a bundled Chromium browser, slow build cycles, and heavy RAM overhead (150MB–300MB+), frequently suffering from brittle OCR checks that abort unexpectedly during menu navigation.

**`Alephal-Sonata`** is built to solve this:
- **Native C# (.NET 8):** Fast, standalone single-file binary (~67MB) that starts in 100ms and consumes < 50MB RAM.
- **Zero Electron:** Pure Win32 P/Invoke and direct hardware-level input.
- **Pixel-First Navigation:** Abandons fragile text OCR for menu detection, replacing it with microsecond-fast pixel color and contrast checks.
- **Hands-Free Inventory Crawling:** Automatically navigates in-game menus, traverses the 5×3 Echo picker grid, and smoothly advances pages with calibrated scroll bursts.

---

## ✦ Inspirations & Acknowledgments

This project stands on the shoulders of the incredible open-source community:

- **[InventoryKamera](https://github.com/Andrewthe13th/Inventory_Kamera) & [WuWa Inventory Kamera](https://github.com/Psycho-Marcus/WuWa_Inventory_Kamera):**  
  The primary inspiration for `Alephal-Sonata`'s automated inventory crawler concept. We pay tribute to their pioneering work in automating desktop screen navigation and OCR ingestion for gacha RPGs.
- **[Tacet Lab](https://github.com/DJ12421/Tacet-Lab) (by DJ12421):**  
  The gold standard Wuthering Waves calculation engine and optimizer. `Alephal-Sonata` adopts Tacet Lab's computer vision principles—specifically using direct pixel color/contrast analysis for UI states instead of heavy OCR—and is designed to output seamless 1-click import files (`tacet-lab-backup.json`) for Tacet Lab.
- **[Nanoka](https://ww.nanoka.cc/):**  
  The community-standard Wuthering Waves datamine reference.

---

## ✦ Meaning of the Name

* **Aleph ($\aleph$):** The mathematical symbol representing transfinite infinity (introduced by Georg Cantor), and an inside homage to **Denia** (*"Bubbles of Nihility"*), vessel of the Voidborne Threnodian Aleph-1.
* **Sonata:** The acoustic Echo harmony and set-bonus mechanic in Solaris-3.
* **Alephal-Sonata:** Literally translates to **"Infinite Echoes"** ($\aleph$ + Sonata) — engineered specifically to conquer the infinite Echo grind.

---

## ✦ Technical Architecture

```text
Wuthering Waves Window
       │
       ▼
[WindowManager]  ─────────► Real-time Foreground Focus Check (Alt+Tab safety)
       │
       ▼
[AutoNavigator]  ─────────► State Machine: Main -> Character ('C') -> Echo Tab -> Slot Picker
       │
       ▼
[InputSimulator] ─────────► Dual Input Engine:
                            ├── Interception Driver (Virtual HID: bypasses UE4/ACE filters)
                            └── Win32 SendInput / mouse_event (Universal fallback)
       │
       ▼
[5x3 Picker Grid Crawl] ──► Calibrated Cell Coordinates & Gaussian Click Jitter
       │
       ▼
[Page Advancement] ──────► Smooth Multi-Step Scroll (-34 Ticks per page)
```

### 1. Hardware-Level Virtual HID Input
Wuthering Waves (Unreal Engine 4 + anti-cheat) filters standard synthetic Windows events carrying the `LLMHF_INJECTED` flag. `Alephal-Sonata` integrates native bindings to the **Interception Driver** (`interception.dll`), injecting mouse clicks, movement, and wheel scrolls at the hardware driver level with 0ms latency. If the driver is not installed, it gracefully falls back to native Win32 `SendInput`.

### 2. Pixel-First State Detection
Rather than running slow Tesseract OCR passes to check if menus are open (which often misreads single-line badges and aborts), `Alephal-Sonata` employs direct pixel color sampling (HSL space) and contrast edge detection. Pixel checks take **0.001ms** and never misread text.

### 3. Humanized Cadence & Anti-Stall Safety
- Click delays are calculated using Box-Muller **Gaussian jitter** around a calibrated mean, avoiding robotic metronomic clicks while staying fast.
- Page scrolls are broken into small, clamped notch packets (`-8` per burst) to prevent Unreal Engine's scroll wheel rate clamp from eating inputs.
- Active **Alt+Tab detection** monitors the foreground window; if the user switches out of Wuthering Waves, the automation halts immediately.

---

## ✦ Quick Start

### Prerequisites
- Windows 10 or Windows 11 (64-bit)
- Wuthering Waves running at 1080p or 1440p (Borderless Windowed or Fullscreen)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed if building from source)

### 1. Build and Run from Source
```powershell
# Clone the repository
git clone https://github.com/RadianceNoMore/Alephal-Sonata.git
cd Alephal-Sonata

# Run directly
dotnet run --project AlephalSonata.csproj
```

### 2. Build Standalone Single-File Executable
To produce a lightweight, single `.exe` file that runs on any PC without needing .NET installed:
```powershell
dotnet publish AlephalSonata.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish
```
The output `publish/AlephalSonata.exe` (~67MB) is completely portable.

---

## ✦ Controls & Usage

When you run `AlephalSonata.exe`, an interactive menu appears:

```text
    ╔═══════════════════════════════════════════════════════╗
    ║       ALEPHAL-SONATA (ℵ-Sonata)                       ║
    ║   Infinite Echo Automation & Navigation Engine        ║
    ║   Dedicated Windows C# Native Scanner                 ║
    ╚═══════════════════════════════════════════════════════╝

[DRIVER STATUS] Interception Virtual HID Driver: ACTIVE

=== ALEPHAL-SONATA CONTROLS ===
1. Detect Wuthering Waves Window
2. Full Auto-Navigation (Main Screen -> Character -> Echo Picker -> Crawl)
3. Crawl Picker Grid Only (Use if already inside Echo Picker)
4. Test Page Scroll Calibration (-34 ticks)
5. Test Single Card Click (First Echo)
0. Exit
```

- **Option 1 (Detect Window):** Verifies game handle, dimensions, resolution, and aspect ratio.
- **Option 2 (Full Auto-Navigation):** Automatically focuses the game, opens the Character screen (`C`), clicks the Echo tab, opens the top slot picker, and crawls all cards.
- **Option 3 (Crawl Grid Only):** If you are already sitting in the Echo picker, choose this option to immediately crawl and scroll without menu navigation.
- **Emergency Stop:** Press `Ctrl+C` in the console or simply **Alt+Tab** out of Wuthering Waves to stop clicking instantly.

---

## ✦ Project Structure

```
Alephal-Sonata/
├── src/
│   ├── Native/
│   │   ├── Win32.cs              # Win32 user32/kernel32 P/Invoke declarations
│   │   └── Interception.cs       # Direct C# bindings to interception.dll
│   ├── Automation/
│   │   ├── InputSimulator.cs     # Mouse/keyboard simulation, wheel bursts & Gaussian jitter
│   │   ├── WindowManager.cs      # Window handle enumeration, focus & bounding rects
│   │   ├── NavigationConfig.cs   # Calibrated fractional coordinates, offsets & timings
│   │   └── AutoNavigator.cs      # The core navigation & grid crawling state machine
│   └── Program.cs                # Interactive CLI dashboard and controls
├── AlephalSonata.csproj          # .NET 8 Windows configuration
├── .gitignore                    # Build artifacts ignore
└── README.md
```

---

## ✦ Roadmap

- [x] Native C# Win32 & Interception Virtual HID input engine
- [x] In-game menu navigation state machine (Main screen $\rightarrow$ Character $\rightarrow$ Echo Tab $\rightarrow$ Slot)
- [x] 5×3 Picker Grid crawler with humanized Gaussian delays
- [x] Calibrated page scrolling (-34 ticks) and Alt+Tab safety stop
- [ ] Computer Vision pixel classifiers (HSL Rarity detection, 16×16 Sonata elemental icon vector matching)
- [ ] Substat OCR parsing with discrete roll-table snapping
- [ ] Direct export to **Tacet Lab JSON** (`tacet-lab-backup.json`) and **GOOD** format

---

## ✦ License & Disclaimer

This project is licensed under the **MIT License**.

*Alephal-Sonata is a fan-made, open-source tool and is not affiliated with, endorsed by, or associated with Kuro Games. Wuthering Waves and all related assets, names, and game content are trademarks and copyrights of Kuro Games.*
