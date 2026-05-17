<div align="center">
  <img src="Assets/AppIcon.ico" width="80" height="80" alt="CineLibrary Essentials" />
  <h1>CineLibrary Essentials</h1>
  <p><b>Drag your movie folder into CineLibrary Essentials and let the Magic begin! Clean up messy movie downloads. Rename, organize, and prep them for scraping, fast. 
     </b></p>
  <p>
    A Windows desktop tool (WinUI 3) that takes the chaos out of your downloads folder.
    Recommended as the <b>preparation step</b> for
    <a href="https://github.com/aungkokomm/CineLibraryCS"><b>CineLibrary</b></a>.
  </p>
</div>
<img width="1536" height="1024" alt="mockup" src="https://github.com/user-attachments/assets/59d1724f-fd85-49b5-b2e9-3a6054bebd98" />

---
![Stars](https://img.shields.io/github/stars/aungkokomm/CineLibraryEssentials?style=for-the-badge&color=blue)
![Downloads](https://img.shields.io/github/downloads/aungkokomm/CineLibraryEssentials/total?style=for-the-badge&color=green)
![Release](https://img.shields.io/github/v/release/aungkokomm/CineLibraryEssentials?style=for-the-badge&color=yellow)

## What it does

Most movie downloads come with messy filenames like:

```
UnTouch.The.Kerala.Story.2.2026.1080p.WEB-HDRip.Hindi.DDP5.1.MULTi.x264.ESub-india4Movies.Diy.mkv
```

CineLibrary Essentials cleans them, organizes them, and gets them ready for a scraper:

```
Movies/
├── The Kerala Story 2 (2026)/
│   ├── The Kerala Story 2 (2026).mkv
│   ├── The Kerala Story 2 (2026)-poster.jpg
│   ├── The Kerala Story 2 (2026)-fanart.jpg
│   ├── The Kerala Story 2 (2026).nfo
│   └── .actors/
└── Bawarchi (1972)/
    └── ...
```

This is the **Plex / Kodi / Jellyfin / MediaElch** standard layout.

---
<img width="1600" height="952" alt="rename" src="https://github.com/user-attachments/assets/e5cdd96c-c8f6-42bc-a279-a48f3c24811d" />


<img width="1600" height="952" alt="f2f" src="https://github.com/user-attachments/assets/7f262903-9bba-4182-8acf-08f6fd257b66" />

<img width="960" height="499" alt="image" src="https://github.com/user-attachments/assets/a7b59a10-ea91-4adf-819a-8e80d51ec23e" />

## Recommended Workflow

```
┌──────────────────────────┐    ┌──────────────────────────┐    ┌──────────────────────────┐
│   1. Essentials (this)   │ →  │   2. MediaElch (heavy)   │ →  │  3. CineLibrary (final)  │
│   Rename + Organize      │    │   Full-fledged scraper   │    │   Scan + add to library  │
└──────────────────────────┘    └──────────────────────────┘    └──────────────────────────┘
```

1. **CineLibrary Essentials** — clean filenames and move them into `Title (Year)/` folders. Optionally use the built-in casual scraper for basic metadata.
2. **[MediaElch](https://www.mediaelch.de/)** — recommended for full-fledged scraping (multiple sources, edition handling, trailers, etc.).
3. **[CineLibrary](https://github.com/aungkokomm/CineLibraryCS)** — scan the prepared library and browse it.

The built-in casual scraper is good enough if you just want posters, plots, and basic metadata. MediaElch is the right choice if you want depth.

---

## User Guide

📖 **User guide:** [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md) — covers every feature, edge case, and troubleshooting tip.

📖 **Full detailed user guide** [In my GitHub.io **CineLibrary Essentials page**] (https://aungkokomm.github.io/cinelibraryessentials/guide/)

Short version below — the app is a **3-step wizard**. The header pills (`① Clean Names → ② Organize → ③ Scrape`) show where you are.

### Step 1 · Clean Names

Pick a folder of movies (or **drag-and-drop** one onto the window).

- **Auto-detect** — extracts title and year, strips technical tags (`1080p`, `x265`, `BluRay`, `WEB-HDRip`, `Atmos`, release-group prefixes, etc.)
- **Diff highlight** — the original filename shows kept tokens in grey and removed tokens in **red strikethrough**, so you can see what changed
- **Confidence chip** — High / Medium / Low per row
- **Edit any row** — just type into the cleaned-name box
- **Bulk tools** — Find & Replace, Apply Smart Title Case, Reset
- **Filter** — search box, confidence filter (All / High / Medium / Low / Warnings)
- **Per-row 🔍 Scrape** — search TMDb directly to confirm the exact title/year
- **Output template** — `{Title} ({Year})` (Plex/Kodi default) or `{Year} - {Title}`
- **Subfolders** — toggle to recursively scan subfolders
- Warnings flag duplicates, invalid characters, missing year, TV episodes, path-too-long, etc.

Click **Rename Selected** to rename in place, OR just continue to Step 2 (Step 2 also renames during the move).

### Step 2 · Organize

Files automatically carry over from Step 1. The output folder defaults to your source folder.

- All rows **pre-checked** — uncheck any to skip
- See **Original → New** path side-by-side
- Click **Run File to Folder** — moves each file into its `Title (Year)/` folder (subtitles follow along)
- After success, you're auto-advanced to Step 3

### Step 3 · Scrape (optional)

Movies from Step 2 are auto-listed. You can also **+ Add Folder** for any movie folder.

- Per-row 🔍 **Scrape** opens the TMDb search dialog so you pick the exact match
- Choose result → app downloads:
  - `Title (Year).nfo` (Plex/Kodi metadata)
  - `Title (Year)-poster.jpg`
  - `Title (Year)-fanart.jpg`
  - `.actors/` folder with cast photos
- **Scrape Selected** auto-scrapes (uses the first TMDb match) for everything checked
- **Save** when you're done

Or skip this step entirely and use **MediaElch** instead — your folders are already in the format MediaElch expects.

---


## Install

Download the latest installer from [Releases](../../releases) and run:

- `CineLibraryEssentials_Setup_<version>.exe` (~58 MB, self-contained — no prerequisites)
- Per-user install, no admin required
- Optional desktop / Start Menu shortcut

**Minimum:** Windows 10 build 17763 (1809) or newer · x64

---

## Build from Source

Requires **.NET 10 SDK**, **Windows App SDK**, and **Inno Setup 6+** (for the installer).

```powershell
# Run + debug
dotnet build -p:Platform=x64 -p:RuntimeIdentifier=win-x64

# Build the installer (publishes self-contained Release + runs Inno Setup)
.\build-installer.ps1
```

Output: `release\CineLibraryEssentials_Setup_<version>.exe`

---

## Tech

- **WinUI 3** (Windows App SDK 2.0)
- **.NET 10**
- **CommunityToolkit.Mvvm** for MVVM
- **TMDb API** for metadata (key embedded — get your own at [themoviedb.org](https://www.themoviedb.org/settings/api) if you want to swap it)

---

## Credits

- Built by [@aungkokomm](https://github.com/aungkokomm)
- Movie metadata by [TMDb](https://www.themoviedb.org/) (this product is not endorsed or certified by TMDb)
- Companion to [CineLibrary](https://github.com/aungkokomm/CineLibraryCS)
