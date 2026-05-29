<div align="center">
  <img src="Assets/AppIcon.ico" width="80" height="80" alt="CineLibrary Essentials" />
  <h1>CineLibrary Essentials</h1>
  <p><b>Drag your movie folder into CineLibrary Essentials and let the Magic begin! 
    Clean up messy movie downloads. <p><b>
    <p><b>Rename, Organize, and Scrapes, everything in one App.  
     </b></p>
  <p>
    A Windows desktop tool (WinUI 3) that takes the chaos out of your downloads folder.
    Recommended as the <b>preparation step</b> for
    <a href="https://github.com/aungkokomm/CineLibraryCS"><b>CineLibrary</b></a>.
  </p>
</div>
<img width="1983" height="793" alt="May 29, 2026, 11_30_50 AM" src="https://github.com/user-attachments/assets/4456435d-40d2-481b-aaf6-e679ac51966a" />


---
![Stars](https://img.shields.io/github/stars/aungkokomm/CineLibraryEssentials?style=for-the-badge&color=blue)
![Downloads](https://img.shields.io/github/downloads/aungkokomm/CineLibraryEssentials/total?style=for-the-badge&color=green)
![Release](https://img.shields.io/github/v/release/aungkokomm/CineLibraryEssentials?style=for-the-badge&color=yellow)

## What it does

Most downloads come with messy filenames like:

```
UnTouch.The.Kerala.Story.2.2026.1080p.WEB-HDRip.Hindi.DDP5.1.MULTi.x264.ESub-india4Movies.Diy.mkv
Breaking.Bad.S01E03.And.the.Bags.in.the.River.1080p.BluRay.x265-RARBG.mkv
```

CineLibrary Essentials cleans them, organizes them into the right folder structure, and scrapes full metadata — for **both movies and TV shows**:

**Movies** → `Title (Year)/`

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

**TV shows** → `Show/Season XX/`

```
TV/
└── Breaking Bad/
    ├── tvshow.nfo
    ├── poster.jpg
    ├── fanart.jpg
    ├── .actors/
    └── Season 01/
        ├── Breaking Bad - S01E01 - Pilot.mkv
        ├── Breaking Bad - S01E01 - Pilot.nfo
        ├── Breaking Bad - S01E01 - Pilot-thumb.jpg
        └── ...
```

This is the **Plex / Kodi / Jellyfin / MediaElch** standard layout — readable by every major media player and library manager.

---
<img width="1600" height="952" alt="rename" src="https://github.com/user-attachments/assets/e5cdd96c-c8f6-42bc-a279-a48f3c24811d" />


<img width="1600" height="952" alt="f2f" src="https://github.com/user-attachments/assets/7f262903-9bba-4182-8acf-08f6fd257b66" />

<img width="960" height="499" alt="image" src="https://github.com/user-attachments/assets/a7b59a10-ea91-4adf-819a-8e80d51ec23e" />

## Workflow

```
┌────────────────────────────────┐    ┌──────────────────────────┐
│     CineLibrary Essentials     │ →  │  CineLibrary (browse it) │
│  Rename · Organize · Scrape    │    │   Scan + add to library  │
└────────────────────────────────┘    └──────────────────────────┘
```

CineLibrary Essentials is now an **all-in-one** preparation toolbox 
— clean filenames, organize into the correct folder structure, and scrape a complete Kodi-standard NFO with poster, fanart, full cast photos, and (for TV) per-episode metadata + thumbnails. One-Stop-Solution.

- **[CineLibrary](https://github.com/aungkokomm/CineLibraryCS)** — scan the prepared library and browse it across multiple drives.
- **[MediaElch](https://www.mediaelch.de/)** — still optional if you want to layer in extra sources or edition-specific artwork; it reads the same folders Essentials produces.

---

## User Guide

📖 **User guide:** [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md) — covers every feature, edge case, and troubleshooting tip.

📖 **Full detailed user guide** [In my GitHub.io **CineLibrary Essentials page**] (https://aungkokomm.github.io/cinelibraryessentials/guide/)

Short version below — the app is a **3-step wizard**. The header pills (`① Clean Names → ② Organize → ③ Scrape`) show where you are.

A **Mode** selector (Auto · Movies · TV Shows) lets you tell the app what you're processing. **Auto** detects each file; **Movies** / **TV Shows** force one type. A **⚙ Settings** dialog (gear icon, top-right) holds your output template, scrape language, default toggles, and update preferences.

### Step 1 · Clean Names

Pick a folder (or **drag-and-drop** one onto the window).

- **Auto-detect** — extracts title + year for movies, or show + season + episode for TV, and strips technical tags (`1080p`, `x265`, `BluRay`, `WEB-HDRip`, `Atmos`, release-group prefixes, etc.)
- **TV episodes** → `Show - S01E01 - Episode Title` (Kodi convention); **Movies** → `Title (Year)`
- **Edition detection** — Director's Cut, Extended, IMAX, 4K Remaster, Theatrical, Unrated, Criterion … shown as a chip and written to the NFO
- **Diff highlight** — original filename shows kept tokens in grey, removed tokens in **red strikethrough**
- **Confidence chip**, editable rows, bulk Find & Replace / Title Case / Reset, search + filters
- **Per-row 🔍 Scrape** — search TMDb (movies or TV) to confirm the exact match
- Subtitles (incl. language-tagged like `.en.forced.srt`) follow the rename automatically

### Step 2 · Organize

Files carry over from Step 1. The output folder defaults to your source folder.

- **Movies** → `Title (Year)/Title (Year).ext`
- **TV** → `Show/Season XX/Show - S01E01 - Title.ext`
- **Folder merging** — if a destination already exists, files merge in instead of erroring (nothing overwritten)
- Click **Run File to Folder** — moves everything (subtitles included), then auto-advances to Step 3

### Step 3 · Scrape

Folders from Step 2 are auto-listed. You can also **+ Add Folder** for any existing library folder.

- **Movies** → downloads `Title (Year).nfo`, original-resolution poster + fanart, and a `.actors/` folder with the full cast's photos
- **TV shows** → downloads `tvshow.nfo` + show poster/fanart/cast, then per-episode `.nfo` + episode thumbnail for every episode
- **Double-tap a scraped card** → a rich **Movie Details** window (hero fanart, poster, plot, color-coded crew/studio/country/genres/IDs/file-info, scrollable cast)
- **Scrape Selected (auto)** — batch-scrape everything checked
- **Fill gaps only** — *Verify-library* sweep: scrapes only folders missing the NFO, poster, fanart, or actor photos; skips complete ones
- **16 languages** — set the scrape language in Settings (English, Burmese, Hindi, Tamil, Telugu, Thai, Chinese, Japanese, Korean, and more)

---


## Install

Download the latest installer from [Releases](../../releases) and run:

- `CineLibraryEssentials_Setup_<version>.exe` (~63 MB, self-contained — no prerequisites)
- Per-user install, no admin required
- Optional desktop / Start Menu shortcut

**Minimum:** Windows 10 build 17763 (1809) or newer · x64

Once installed, the app **checks for updates on startup** (once per day) and offers to download + install new versions for you — no need to come back here.

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
