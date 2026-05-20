# CineLibrary Essentials — User Guide

> **In a nutshell:** drop a folder of messy movie downloads, the app cleans the filenames, organises them into `Title (Year)/` folders, and (optionally) downloads metadata. Designed as the **prep step** before [CineLibrary](https://github.com/aungkokomm/CineLibraryCS).

---

## Table of contents

- [Install & first launch](#install--first-launch)
- [The big picture — 3-stage workflow](#the-big-picture--3-stage-workflow)
- [Step 1 — Clean Names](#step-1--clean-names)
  - [Loading files](#loading-files)
  - [Understanding each row](#understanding-each-row)
  - [Editing & bulk tools](#editing--bulk-tools)
  - [Filters & search](#filters--search)
  - [Sorting](#sorting)
  - [Per-row TMDb search](#per-row-tmdb-search)
  - [Top-row toggles explained](#top-row-toggles-explained)
  - [Hitting the Rename button](#hitting-the-rename-button)
  - [Undo](#undo)
- [Step 2 — Organize](#step-2--organize)
- [Step 3 — Scrape](#step-3--scrape)
- [Settings page](#settings-page)
- [Persistence](#persistence)
- [Tips & tricks](#tips--tricks)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)

---

## Install & first launch

1. Download the latest `CineLibraryEssentials_Setup_<version>.exe` from the [Releases page](https://github.com/aungkokomm/CineLibraryEssentials/releases/latest).
2. Run it. The installer is **per-user** (no admin required), self-contained (no .NET / WindowsAppSDK to install separately), and roughly 58 MB.
3. Pick an install location (default is fine), tick whether you want a desktop / Start Menu shortcut, and finish.

When the app launches you'll see three "step pills" at the top — `① Clean Names › ② Organize › ③ Scrape`. Click any pill to jump to that step. The pill that's currently active is filled with the accent colour.

There's no setup screen — the TMDb API key is embedded, so the app is **ready to use immediately**.

---

## The big picture — 3-stage workflow

CineLibrary Essentials is meant to fit **between** your downloads and your final media library. The complete pipeline:

```
   Downloads/             Step 1: Clean Names      Step 2: Organize        Step 3: Scrape (or use MediaElch)        CineLibrary
   ──────────             ────────────────────     ─────────────────       ──────────────────────────────────       ───────────
   movie.1080p.mkv  →     "Movie (Year).mkv"  →    Movie (Year)/      →    Movie (Year).nfo + posters         →    Browse + play
   another.mkv            "Another (2020).mkv"     Another (2020)/         …
```

You don't have to do all three steps. The app supports four workflows:

| Goal | Steps you actually need |
|---|---|
| Just rename files | Step 1 → click **Rename Selected** → done |
| Rename + organize into folders | Step 1 → Step 2 |
| Already-clean files, just need them organized | Step 2 (use **+ Add Files** / **+ Add Folder**) |
| Full pipeline including metadata | Step 1 → Step 2 → Step 3 |
| Already-organized library, just need metadata | Step 3 (use **Add Folder**) |

You can skip steps in either direction at any time.

---

## Movies and TV shows

CineLibrary Essentials handles **both** in the same wizard. A **Mode** selector at the top of Step 1 (and Step 3) decides how files are treated:

| Mode | Behavior |
|---|---|
| **Auto** *(default)* | Each file is detected individually — anything matching `S01E01`, `1x01`, or `Season 1 Episode 1` is treated as a TV episode; everything else as a movie. |
| **Movies** | TV detection is disabled — every file is treated as a movie (so *"Star Wars Episode IV"* won't be mistaken for TV). |
| **TV Shows** | Every file is treated as an episode; files that don't match an S/E pattern are flagged so you can fix them. In Step 3 the dropdown also acts as a filter — show only movies, only TV shows, or all. |

The mode is shared between Step 1 and Step 3 and is remembered between sessions.

### How each is organized

- **Movies** → `Output/Title (Year)/Title (Year).ext`
- **TV episodes** → `Output/Show Name/Season 01/Show Name - S01E01 - Episode Title.ext`

TV episodes are renamed to the Kodi/Plex convention `Show - S01E01 - Title`, and all episodes of a season live together in one `Season XX` folder.

### How each is scraped (Step 3)

- **Movies** → `Title (Year).nfo`, original-resolution poster + fanart, `.actors/` cast photos
- **TV shows** → `tvshow.nfo` + show poster/fanart/cast at the show root, plus a per-episode `.nfo` and episode thumbnail for every episode inside each season

Flat single-season folders (e.g. `Breaking Bad Season 1 Complete/` with episodes directly inside) are recognized too — the show name is read from the episode filenames, not the folder name.

---

## Step 1 — Clean Names

![Step 1 screenshot](https://github.com/user-attachments/assets/e5cdd96c-c8f6-42bc-a279-a48f3c24811d)

This is where messy filenames become clean ones. The app shows you a side-by-side preview of every video file and what it'll be renamed to — you review, edit if needed, then commit.

### Loading files

Three ways:

1. **Click the folder icon** next to the Source box. Pick a folder. Done.
2. **Click the ▾ on the folder button** for a dropdown of your recent folders.
3. **Drag a folder** from File Explorer and drop it anywhere on the window. A dimmed overlay confirms the drop is accepted.

If you drop a *file* instead of a folder, the app uses the file's parent folder. Useful when you want to drag a single file from a deep path without navigating to its folder first.

### Understanding each row

| Column | What it shows |
|---|---|
| ☑ | Whether this row will be processed when you click Rename Selected. Defaults to checked. |
| **Original** | The filename on disk now. **Red strikethrough** marks tokens the parser is going to remove (`1080p`, `BluRay`, `x265`, release groups, etc.). Grey tokens are the ones that survive into the cleaned name. |
| File size | Right under the original, dimmed. Helps you spot tiny "sample" files. |
| **Cleaned** | The proposed new filename. **Editable** — type into it to override the parser. If an edition is detected (Director's Cut, Extended, IMAX, 4K Remaster, Theatrical, Unrated, Criterion, etc.) a small **chip** appears under the cleaned name. The edition isn't added to the filename itself, but it *is* written to the NFO so Plex / Kodi can group multiple cuts of the same movie. |
| **Confidence** | A coloured pill — *High* (green) means the parser is sure, *Medium* (amber) means probably right, *Low* (red) means review carefully. |
| ⚠ | Warning icon. Hover for details: missing year, duplicate name, invalid characters, TV-episode pattern, filename too long, etc. |
| 🔍 | Inline TMDb search button — click to confirm the title against The Movie Database. |

### Editing & bulk tools

You can fix names three ways:

- **Type directly** into the Cleaned field. Updates apply when you click out of the field.
- **Bulk Find & Replace** — type something in *Find*, something in *Replace*, click **Replace**. Affects every checked row.
- **More menu** (top-right `⋯`):
  - **Select All** (also Ctrl+A)
  - **Select None**
  - **Apply Smart Title Case** — capitalises words properly (small words like "of" / "the" / "a" stay lowercase, except as first/last word).
  - **Reset Auto-Cleaning** — if you've messed something up, this re-runs the parser on the original filename.

**Right-click any row** for the same actions targeted at just that one row, plus *Open Containing Folder* and *Remove from List*.

### Filters & search

Top toolbar:

- **🔍 Search box** — live-filters by both original and cleaned name.
- **Confidence dropdown** — *All*, *High*, *Medium*, *Low*, *Warnings only*, *Needs renaming*.
- **Modified only toggle** — quick way to hide rows where the cleaned name already matches the original. Useful when you've got a half-cleaned folder and want to focus on what's left.

### Sorting

Click any of the three column headers — **Original**, **Cleaned**, **Confidence** — to sort by that column. Click again to reverse direction. The little arrow next to the column name shows current state. Your sort choice is **remembered between sessions**.

### Per-row TMDb search

When the parser isn't sure (or you just want to verify), click the 🔍 button on any row. A search dialog opens with:

- The cleaned title and year pre-filled (year is sent as a separate filter to TMDb)
- A list of poster + plot + year for each match
- Click a result, click **Use Selected**

The cleaned name updates to the canonical TMDb title + year, the row is marked **reviewed**, and confidence jumps to 100%. The original filename on disk hasn't changed yet — that only happens when you hit Rename Selected.

### Top-row toggles explained

Right of the source picker:

- **Subfolders** — when on, the file scan recurses into every subfolder. Useful for processing an entire downloads tree at once.
- **Rename folder** — when on, if a movie sits in its **own subfolder** (single-video folder, not the source root), the folder is renamed to match the cleaned filename. Skips the source root, multi-video folders, and name collisions.
- **Clean metadata** — when on, after the file rename, the app also rewrites the **embedded title tag** inside the video file (and clears Comment / Description / Encoder / Copyright). This is what kills the **website link in VLC's titlebar** that scene releases often have. Powered by [TagLib#](https://github.com/mono/taglib-sharp). No re-encoding — fast and safe.

### Output template dropdown

Choose between two formats for the cleaned filename:

- `{Title} ({Year})` — the Plex / Kodi / Jellyfin / MediaElch standard
- `{Year} - {Title}` — sortable chronologically

Switching the template re-applies it to all **non-reviewed** rows. Reviewed rows (yours edits, TMDb confirmations) are left alone so you don't lose your work.

### Hitting the Rename button

When you're happy with the list, click **Rename Selected**.

- A confirmation dialog tells you how many files will be touched.
- Companion subtitles (`.srt`, `.sub`, `.ass`, `.ssa`, `.vtt`, `.idx`) are renamed alongside their video.
- If **Rename folder** is on, the parent folder is renamed *after* the file (so you don't end up with broken paths).
- If **Clean metadata** is on, the file's embedded tags are scrubbed.

When the rename finishes, you'll see a toast notification at the bottom-right.

### Undo

The success toast has an **Undo** button. You have **30 seconds** to click it — after that the toast dismisses and the rename is final.

Undo reverses every rename in the batch (files first, folders last) so you're back to where you started.

> **Tip:** even though we have undo, you should still review carefully before committing. If you close the toast or wait too long, you'll have to fix any mistakes manually.

---

## Step 2 — Organize

![Step 2 screenshot](https://github.com/user-attachments/assets/7f262903-9bba-4182-8acf-08f6fd257b66)

Step 2 takes the (renamed) files and moves each one into a `Title (Year)/` folder — the layout Plex, Kodi, Jellyfin, and MediaElch all expect.

### How files arrive in Step 2

Three ways:

1. **From Step 1** — automatic. Files carry over, all checked, the moment you click into Step 2.
2. **+ Add Files** — multi-select file picker. Use this when you've already cleaned filenames elsewhere.
3. **+ Add Folder** — recursively pulls every video out of a folder you pick. Good for processing a whole drive of pre-named files.
4. **Drag-drop** — drop a folder or files onto the page. Folder = recursive. Files = added directly.

Each row shows the source file → arrow → destination folder + new filename.

### Output folder

The output folder defaults to the **source folder** so the moves happen "in place" (creating subfolders inside the source). Click the folder button to pick a different output, or use the ▾ for recents.

If you change the output folder, every destination preview updates automatically.

### Running the move

Tick / untick rows you want to skip, then click **Run File to Folder**. The app:

1. Creates a `Title (Year)/` folder (movies) or `Show/Season XX/` folders (TV) under the output.
2. Moves the video into it.
3. Moves any matching subtitle files alongside — including language-tagged ones like `Movie.en.forced.srt`.
4. After success, advances to Step 3 automatically.

**Folder merging:** if a destination folder already exists, files are merged into it instead of erroring. Existing files are never overwritten — and any subtitles from the source still move into the destination. The status line reports e.g. *"3 organized · 2 merged into existing folder(s)"*.

---

## Step 3 — Scrape

Step 3 downloads metadata, posters, fanart, and actor photos from TMDb and writes them into each movie folder in the **Plex / Kodi NFO standard** layout:

```
Bawarchi (1972)/
├── Bawarchi (1972).mkv
├── Bawarchi (1972)-poster.jpg
├── Bawarchi (1972)-fanart.jpg
├── Bawarchi (1972).nfo
└── .actors/
    ├── Rajesh-Khanna.jpg
    └── ...
```

### Card view

The default view is a **grid of cards** — each card shows the poster (or a placeholder), the title, the folder path, a status pill, and three buttons (Scrape / Open / Remove).

Status pills are colour-coded:

| Color | Meaning |
|---|---|
| Grey | *Ready* — never scraped |
| Blue | *Scraping…* / *Searching…* / *Downloading…* (in progress) |
| Green | *Complete* (just scraped) or *Already scraped* (folder already had .nfo + poster when loaded) |
| Red | *Failed* — hover the warning, the error is in the row |

If a folder was already scraped (it has both an `.nfo` and a `-poster.jpg`), the app **detects this on load** and pre-unchecks it so a bulk *Scrape Selected* won't redo it.

### Per-movie scrape

Click **Scrape** on any card → the same TMDb search dialog as Step 1 opens, pre-filled with the title and year. Pick a result → app downloads everything. The card flips to green and the poster appears.

### Bulk auto-scrape

Click **Scrape Selected (auto)** at the top → every checked card is processed sequentially using the **first TMDb match** for each one (movie or TV show, depending on the card). Fast for batch processing, less precise than per-card. Use it after you've eyeballed the list and trust the auto-match.

### Fill gaps only (verify a library)

Click **Fill gaps only** → the app sweeps **every** folder in the list and scrapes only the ones missing something (NFO, poster, fanart, or actor photos). Complete folders are skipped, so it's safe to re-run on an existing library — it just patches the holes. Ignores the row checkboxes. Each card's status shows exactly what's missing, e.g. *"Missing: poster · fanart"* or *"Complete"*.

### TV shows in Step 3

TV folders show one card per show with a status like *"TV · 3 seasons · Complete"*. Scraping a TV card writes `tvshow.nfo` + show artwork at the root and a per-episode `.nfo` + thumbnail for every episode. Right-click → **Search TMDb** searches the **TV** database (not movies) for TV cards.

### Other things you can do

- **+ Add Folder** — add a movie folder that wasn't part of Steps 1 or 2. The app accepts both single movie folders and "parent" folders that contain many movie folders.
- **Drag-drop** — same as above.
- **Double-tap a scraped card** — opens the **Movie Details** window (hero fanart, poster, plot, color-coded crew / studio / country / genres / IDs / file info, scrollable cast strip with photos, plus Play / Open Folder / Trailer buttons). ESC closes it. Resizable and maximizable.
- **Double-tap an unscraped card** — opens the resizable TMDb search dialog so you can pick the right match.
- **Right-click a card** → View details / Search TMDb / Open Folder / Remove from List.
- **List view toggle** (icon in toolbar) — for libraries with hundreds of movies, the dense list view is faster. Your preference is remembered.
- **Each card shows an inline spinner** next to its status pill while it's being scraped — easy to see which one is in flight when you batch-scrape.
- **Done** button — closes out the workflow with a confirmation message. Files and metadata are already on disk, so this is more of a "finished" indicator than an action.

---

## Settings page

A **gear icon** lives in the title bar next to the About icon. Clicking it opens a single Settings dialog with everything you can persist:

**General**

- **Default output template** — `{Title} ({Year})` or `{Year} - {Title}`.
- **Recursively scan subfolders by default** — Step 1 starts with the Subfolders toggle on.
- **Clean embedded MKV metadata by default** — Step 1's "Clean metadata" checkbox starts on.

**Scraping**

- **TMDb language** — 16 languages including English, Burmese, Hindi, Tamil, Telugu, Thai, Chinese, Japanese, Korean, French, German, Spanish, Italian, Portuguese, Russian, Arabic. TMDb returns titles, plots and posters in the chosen language where available, and falls back to English when it doesn't.

**Updates**

- **Check for updates on startup** — silent once-per-24h check against GitHub. When a newer version is published, a toast appears with a **Download** button that fetches the installer directly inside the app (with a live progress percentage) and launches it for you.

---

## Persistence

The app saves settings into `appsettings.json` next to the .exe. You shouldn't need to touch this file, but if you ever want to reset everything, just delete it.

What's remembered between sessions:

- TMDb API key (embedded by default)
- Everything from the Settings page above
- Recent source / output folders (last 10 each)
- Window size + position
- Which warnings you've dismissed
- Step 3 view preference (Grid / List)
- Step 1 sort column + direction
- Last successful update-check timestamp

---

## Tips & tricks

- **Drag-drop works on every step.** Step 1 = load source. Step 2 = add to organize queue. Step 3 = add to scrape queue.
- **Right-click is your friend.** Every list / card has a context menu with the most useful actions.
- **Use "Modified only" + "Low" filter** to focus on the rows that actually need attention in big folders.
- **The TMDb dialog accepts year in the search box.** Type `Movie 2024` — it parses the year out and uses it as TMDb's release-year filter for tighter matches.
- **The 30-second Undo applies to the whole batch.** If you regret part of the rename, undo the whole thing, fix that one row, and re-rename.
- **Step 3 detects already-scraped folders.** You can re-load the same folder a hundred times — it won't re-scrape what's already done unless you tick the row.
- **Clean metadata is a one-shot fix** for the website-in-VLC-titlebar problem. Once it's done for a file, the file is clean forever.

---

## Troubleshooting

| Problem | Try this |
|---|---|
| App doesn't launch | Make sure you're on **Windows 10 build 17763 (1809) or newer**, x64. The installer is self-contained — no other prerequisites. |
| "Folder no longer exists" toast on a recent | The folder was moved or deleted. Pick a new one — the recent list will refresh. |
| TMDb search returns nothing | Check spelling, drop the year temporarily, or look the movie up on themoviedb.org first to confirm it exists in their database. |
| Rename failed: "target already exists" | Two files would have ended up with the same cleaned name. Edit one of them and try again. The duplicate warning would have flagged this before you hit Rename. |
| Folder rename failed | Likely a file inside the folder is open in another app (e.g. VLC playing it). Close other apps and try again. |
| Undo did nothing | The undo window is **30 seconds**. After that, the toast dismisses and the operation is final. |
| Wrong year extracted from filename | Click the 🔍 TMDb button on that row and pick the right match — it'll override both title and year. |

---

## FAQ

**Q: Will this app re-encode my videos?**
No. It only renames files (a metadata-level operation) and moves them between folders. Even the "Clean metadata" feature only rewrites tags in the file container — no audio or video data is touched. Operations are near-instant regardless of file size.

**Q: Does it work with TV shows?**
Yes — fully. As of v1.2.0 TV shows are a first-class workflow alongside movies. The app detects episode patterns (`S01E03`, `1x03`, `Season 1 Episode 3`), renames them to the Kodi convention `Show - S01E01 - Title`, organizes them into `Show/Season XX/` folders, and scrapes a `tvshow.nfo` + show poster/fanart/cast plus a per-episode `.nfo` and thumbnail for every episode. A **Mode** selector (Auto / Movies / TV Shows) lets you force or filter the type, and flat "Season Complete" folders are recognized too.

**Q: What about my movie's existing subtitles?**
Companion files with the same base name (`.srt`, `.sub`, `.ass`, `.ssa`, `.vtt`, `.idx`) are renamed alongside the video automatically. They get moved into the `Title (Year)/` folder along with the video in Step 2.

**Q: My TMDb API key — is it the app's or mine?**
The app ships with an embedded API key, so it works out of the box. If you want to use your own (e.g. for higher rate limits), edit the `TmdbApiKey` field in `appsettings.json`.

**Q: Can I trust this with my library?**
Step 1's renames are reversible via the 30-second Undo toast. Steps 2 and 3 are more conservative — they create new folders and move files but don't delete anything. As always, **try it on a small folder first** before unleashing it on your whole collection.

**Q: Where do I report bugs / request features?**
Open an issue at https://github.com/aungkokomm/CineLibraryEssentials/issues.

**Q: Why use this over MediaElch directly?**
MediaElch is *fantastic* for full-fledged scraping but it stumbles on messy filenames and unstructured folders. CineLibrary Essentials does the cleanup work first so MediaElch (or our built-in scraper) starts from a clean slate. Use them together.

---

## Next steps

When your library is clean and scraped:

1. **For deeper scraping** → open MediaElch and point it at your output folder.
2. **For browsing + management** → grab [CineLibrary](https://github.com/aungkokomm/CineLibraryCS), the companion app that catalogs your prepared library across multiple drives.

Enjoy your tidy movie collection! 🎬
