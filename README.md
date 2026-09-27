# Alephal-Sonata (ℵ-Sonata)

> **"Infinite Echoes"** — Lightweight, high-performance C# native automation and navigation engine for **Wuthering Waves**.

`Alephal-Sonata` is a standalone Windows automation tool engineered to crawl, navigate, and scan Wuthering Waves Echo inventories without the heavy overhead, slow build times, or memory bloat of Electron.

---

## ✦ Key Features

- **Native C# Win32 & Virtual HID Input:**
  - Direct Win32 P/Invoke with 0ms execution latency.
  - Native **Interception Driver** integration (`interception.dll`) to bypass Unreal Engine 4 synthetic input filtering (`LLMHF_INJECTED`) at the kernel/hardware level.
- **Calibrated In-Game State Machine:**
  - Automated menu sequence: Focus Window $\rightarrow$ Character Menu (`C`) $\rightarrow$ Echo Tab $\rightarrow$ Equipped Slot Picker.
  - Full Echo Grid Crawl: 5 rows × 3 columns picker grid traversal with humanized Gaussian click delays.
  - Smooth page advancement: Calibrated `-34` wheel notches per page with clamped burst delivery.
- **Safety First:**
  - Instant **Alt+Tab detection**: immediately ceases clicking if the game loses foreground focus.
  - Emergency user interrupt (`Ctrl+C`).

---

## ✦ Quick Start

### Prerequisites
- Windows 10/11
- .NET 8.0 SDK

### Run the App
```powershell
dotnet run --project AlephalSonata.csproj
```

### Build Single-File Release
```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

---

## ✦ Project Structure

```
Alephal-Sonata/
├── src/
│   ├── Native/
│   │   ├── Win32.cs              # Win32 user32/kernel32 P/Invoke declarations
│   │   └── Interception.cs       # Hardware-level HID driver bindings
│   ├── Automation/
│   │   ├── InputSimulator.cs     # High-level mouse/keyboard simulation & jitter
│   │   ├── WindowManager.cs      # Wuthering Waves window detection & focus
│   │   ├── NavigationConfig.cs   # Tested grid coordinates, fractional offsets & timings
│   │   └── AutoNavigator.cs      # Core navigation & grid crawling state machine
│   └── Program.cs                # Interactive CLI control dashboard
├── AlephalSonata.csproj          # .NET 8 Windows Desktop configuration
└── README.md
```

---

## ✦ Meaning of the Name

- **Aleph ($\aleph$):** The mathematical symbol for transfinite infinity, and an homage to **Denia** (*"Bubbles of Nihility"*), vessel of the Voidborne Threnodian Aleph-1.
- **Sonata:** The acoustic Echo harmony of Wuthering Waves.
- **Alephal-Sonata:** *"Infinite Echoes"* — built to conquer the infinite grind.
