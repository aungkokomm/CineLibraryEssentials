using System.Text;
using System.Text.RegularExpressions;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Movie filename parser. Strips technical/junk tags and extracts the title and year.
/// Designed to handle the messiest real-world download names.
/// </summary>
public static class RegexPatterns
{
    // Step 0: normalize "[1996]" or "{1996}" → "(1996)" so the format is consistent.
    private static readonly Regex BracketYearPattern = new(
        @"[\[\{]\s*(19\d{2}|20[0-3]\d)\s*[\]\}]",
        RegexOptions.Compiled);

    // Match ALL year-like 4-digit numbers (1900-2039) that sit at separator boundaries.
    // We use this in v1.1 to enumerate candidates and pick the best one, instead of
    // just grabbing the first match.
    private static readonly Regex YearPattern = new(
        @"(?:[\(\[\{\.\-\s_]|^)(19\d{2}|20[0-3]\d)(?:[\)\]\}\.\-\s_]|$)",
        RegexOptions.Compiled);

    // Tracker URLs at the start
    private static readonly Regex TrackerUrlPattern = new(
        @"(?:www\.|http(?:s)?:\/\/)\S+?\.\w{2,5}(?:\s*[\-_]\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Leading release-group / source prefix
    private static readonly Regex LeadingPrefixPattern = new(
        @"^\s*(?:" +
        @"[\[\(\{][^\]\)\}]+[\]\)\}]" +
        @"|" +
        @"(?:UnTouch(?:ed)?|AMZN|NF|HULU|DSNP|ATVP|iT|HBO|HMAX|PCOK|STAN|CR|FUNi" +
        @"|GalaxyRG|GalaxyTV|YIFY|YTS|RARBG|EVO|Vyndros|TGx|MeGusta|Tigole|EtHD" +
        @"|JustWatch|Cinephiles|Surtsy|FraMeSToR|Pahe|EZTV|FUM|PSA|RARBGx|d3g)" +
        @"\." +
        @")\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Brackets
    private static readonly Regex BracketsPattern = new(
        @"[\[\(\{][^\]\)\}]*[\]\)\}]",
        RegexOptions.Compiled);

    // Big "junk tags" pattern
    private static readonly Regex JunkTagPattern = new(
        @"\b(?:" +
        @"4k|2160p?|1440p?|1080p?|720p?|576p?|480p?|360p?|UHD|FHD|HD|SD" +
        @"|x264|x265|h\.?264|h\.?265|HEVC|AVC|XviD|DivX|VP9|AV1|10bit|8bit|10\-bit|8\-bit" +
        @"|AAC|AC3|EAC3|DDP?(?:[57]\.[01])?|DD\+|DTS(?:\-?HD|\-?X)?|FLAC|MP3|TrueHD|Atmos|Opus|MA" +
        @"|[57]\.[01]" +
        @"|HDR(?:10\+?|10Plus)?|DV|DolbyVision|Dolby\.Vision" +
        @"|Blu\-?Ray|BDRip|BRRip|BRip|BD(?:25|50|66|100)?|REMUX|WEB\-?DL|WEB\-?Rip|WEBRip|WEB" +
        @"|HDTV|HDTC|HDCAM|HDRip|DVDRip|DVD\-?R|DVD|TVRip|CAM|TS|TC|PDTV|PPV|VHS" +
        @"|Extended|Director\.?s?\.?Cut|Theatrical|Unrated|Uncut|Remastered|Special\.?Edition" +
        @"|Final\.?Cut|IMAX|REPACK|PROPER|INTERNAL|LIMITED|RERIP|Criterion" +
        @"|DUAL|MULTI|MULTi|VOSTFR|Hindi|English|Spanish|French|German|Italian" +
        @"|Japanese|Korean|Chinese|Tamil|Telugu" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Trailing release group like "-RARBG", "-NTb", "-EVO".
    // The negative lookbehind (?<!\s) prevents this from eating " - Title"
    // (TV episode title separator) — real release groups are written WITHOUT
    // a space before the dash. So:
    //   "Billions.S02E05.Title.1080p-RARBG"  →  strips "-RARBG"    ✓
    //   "Billions - S02E05 - Currency"       →  leaves it alone    ✓
    private static readonly Regex ReleaseGroupPattern = new(
        @"(?<!\s)[\-_][A-Za-z0-9]{2,20}\s*$",
        RegexOptions.Compiled);

    // The user's "Words to always remove" list (Settings). Null when empty.
    private static volatile Regex? _customRemovePattern;

    /// <summary>
    /// Sets the user's "Words to always remove" list. Matching is case-insensitive
    /// and whole-word only ("ION10" goes, but a word never loses part of itself).
    /// A leading or trailing "-", "_" or "." typed by the user is ignored.
    /// </summary>
    public static void SetCustomRemoveWords(IEnumerable<string> words)
    {
        var list = words
            .Select(w => w.Trim().Trim('-', '_', '.', ' '))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(w => w.Length)          // longest first: "ION265" before "ION"
            .ToList();
        if (list.Count == 0) { _customRemovePattern = null; return; }

        // A word boundary is any character that isn't a letter or digit (works for
        // non-Latin titles too). Only required on sides where the word itself
        // starts/ends with a letter or digit, so "[EZTVx.to]" can still match.
        static string Part(string w)
            => (char.IsLetterOrDigit(w[0]) ? @"(?<=^|[^\p{L}\p{N}])" : "")
               + Regex.Escape(w)
               + (char.IsLetterOrDigit(w[^1]) ? @"(?=$|[^\p{L}\p{N}])" : "");

        _customRemovePattern = new Regex(
            string.Join("|", list.Select(Part)),
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static string RemoveCustomWords(string text)
        => _customRemovePattern?.Replace(text, " ") ?? text;

    /// <summary>
    /// Strips a trailing release group, but only when the token looks like one:
    /// all caps ("-RARBG"), contains a digit ("-ION10") or has an inner capital
    /// ("-NTb", "-mSD"). A plain word is part of the title ("Spider-Man",
    /// "Monster-in-Law", "X-Men"), so it stays.
    /// </summary>
    private static string StripReleaseGroup(string text)
        => ReleaseGroupPattern.Replace(text, m => LooksLikeReleaseGroup(m.Value.Trim().TrimStart('-', '_')) ? " " : m.Value);

    private static bool LooksLikeReleaseGroup(string token)
    {
        if (token.Length < 2) return false;
        if (token.Any(char.IsDigit)) return true;
        if (token.All(c => !char.IsLetter(c) || char.IsUpper(c))) return true;   // RARBG
        return token.Skip(1).Any(char.IsUpper);                                   // NTb, mSD
    }

    // Multi-part / sequel tags (CD1, Disc 2, Part 3, Vol 4)
    private static readonly Regex PartPattern = new(
        @"\b(?:CD|Disc|Disk|Part|Pt|Vol(?:ume)?)\s*[0-9]+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // TV-episode markers
    private static readonly Regex TvEpisodePattern = new(
        @"\b(?:S\d{1,2}E\d{1,2}|S\d{1,2}\.E\d{1,2}|\d{1,2}x\d{1,2}|Episode[\.\s_]\d+|Season[\.\s_]\d+|E\d{2,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsTvEpisode(string filename)
        => !string.IsNullOrEmpty(filename) && TvEpisodePattern.IsMatch(filename);

    // ── TV-episode parsing ───────────────────────────────────────────
    // Capturing patterns, tried in order from most-specific to least.
    // Earlier patterns are more reliable; later ones are fallbacks that handle
    // anime / weirdly-named releases. The first one that matches wins.
    private static readonly Regex[] TvEpisodeCaptures = new[]
    {
        // S01E01, s01.e01, S1E1, S01E001
        new Regex(@"[Ss](?<s>\d{1,2})[\._\-\s]?[Ee](?<e>\d{1,3})", RegexOptions.Compiled),
        // 1x01, 12x345
        new Regex(@"\b(?<s>\d{1,2})x(?<e>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "Season 01, Episode 01" / "Season 1 Episode 1" / "Season.1.Episode.1"
        // / "Season 1 - Episode 1". The [\s\._,\-]* between the parts allows any
        // mix of spaces, dots, underscores, commas and dashes (so the common
        // "Season 01, Episode 01" parenthetical form matches).
        new Regex(@"Season[\s\._,\-]*(?<s>\d{1,3})[\s\._,\-]*Episode[\s\._,\-]*(?<e>\d{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Short forms: "Se01 Ep01", "Se 1 Ep 1"
        new Regex(@"Se[\s\._]*(?<s>\d{1,2})[\s\._,\-]*Ep[\s\._]*(?<e>\d{1,3})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Period-style "Show 1.01 - Title" / "Show.1.01.Title". The season and
        // episode are surrounded by separator-or-boundary characters so it
        // doesn't catch version numbers like "v2.0".
        new Regex(@"(?:^|[\s\._\-])(?<s>\d{1,2})\.(?<e>\d{2,3})(?=[\s\._\-]|$)",
            RegexOptions.Compiled),
        // Episode-only forms: "Show E01" / "Show Ep01" / "Show.E001". Defaults
        // season to 1. Requires an E/Ep prefix so plain numbers (years, parts)
        // aren't mistaken for episodes.
        new Regex(@"(?:^|[\s\._\-])[Ee]p?(?<e>\d{2,3})\b(?<s>)",
            RegexOptions.Compiled),
    };

    /// <summary>
    /// Episode-only / anime patterns don't capture a season — defaults to 1
    /// when we hit the parser's int.TryParse on an empty group.
    /// </summary>
    private const int DefaultSeasonForEpisodeOnly = 1;

    // Match "(2010)" / " 2010 " inside the show-name portion so we can strip
    // a release year from the show name — keeping it would produce filenames
    // like "Doctor Who 2005 - S01E01.mkv" which Plex sometimes mis-matches.
    private static readonly Regex YearInShowNamePattern = new(
        @"[\(\[]?\b(19\d{2}|20[0-3]\d)\b[\)\]]?", RegexOptions.Compiled);

    // Anime release-group prefix like "[HorribleSubs] " / "[SubsPlease] ".
    // The general LeadingPrefixPattern catches known scene groups but not these.
    private static readonly Regex AnimeGroupPrefixPattern = new(
        @"^\s*\[[^\]]{2,30}\]\s*", RegexOptions.Compiled);

    // Stray bracket characters left over after splitting around the SxxExx marker
    // (e.g. "Sherlock (" or "A Study in Pink)").
    private static readonly Regex StrayBracketPattern = new(
        @"[\(\)\[\]\{\}]", RegexOptions.Compiled);

    // First unambiguous technical tag after the SxxExx marker (resolution, codec,
    // release source, audio format, streaming service). A real episode title sits
    // BEFORE this tag; everything from it onward is release noise, e.g.
    //   "Lost.in.Space.S03E01.WEBRip.x264-ION10"  → title is empty, not "Ion10"
    // Deliberately narrower than JunkTagPattern: words like "Final Cut", "English"
    // or "Extended" can be real episode titles, so they don't end the title here.
    // Streaming tags and bare WEB are matched case-sensitively (uppercase only).
    private static readonly Regex TechAnchorPattern = new(
        @"(?:^|[\s\._\-\[\(])(?:" +
        @"\d{3,4}[pi]|4k|UHD" +
        @"|[xh]\.?26[45]|HEVC|AVC|XviD|DivX|AV1|VP9|10\-?bit|8\-?bit" +
        @"|WEB[\-\.]?DL|WEB[\-\.]?Rip|Blu[\-\.]?Ray|BDRip|BRRip|HDTV|HDRip|DVDRip|REMUX|PDTV|HDCAM" +
        @"|DDP?\d|E?AC3|AAC|DTS|TrueHD|Atmos|HDR(?:10)?" +
        @"|(?-i:WEB|AMZN|NF|DSNP|HMAX|ATVP|HULU|PCOK)" +
        @")(?=$|[\s\._\-\]\)])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record TvEpisodeParseResult(
        string ShowName,
        int Season,
        int Episode,
        string EpisodeTitle,
        double Confidence);

    /// <summary>
    /// Parses a TV episode filename into show name, season number, episode number,
    /// and episode title (when present after the SxxExx marker). Returns null if
    /// the file doesn't look like a TV episode.
    /// </summary>
    public static TvEpisodeParseResult? ParseTvEpisode(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return null;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename) ?? string.Empty;
        var working = RemoveCustomWords(NormalizeUnicode(nameWithoutExt));   // + Settings "words to always remove"

        // Strip a leading anime release-group bracket like "[HorribleSubs] " so
        // the show name parser sees the actual show name first. Doing this here
        // (before pattern matching) keeps best.Index relative to the cleaned
        // string instead of the raw bracketed form.
        working = AnimeGroupPrefixPattern.Replace(working, string.Empty);

        Match? best = null;
        foreach (var rx in TvEpisodeCaptures)
        {
            var m = rx.Match(working);
            if (m.Success) { best = m; break; }
        }
        if (best == null) return null;

        // Some patterns (episode-only, anime absolute) don't capture a season.
        // In that case the regex still defines the named group `s` so the
        // engine returns an empty string — fall back to season 1.
        var seasonText = best.Groups["s"].Value;
        var season = string.IsNullOrEmpty(seasonText)
            ? DefaultSeasonForEpisodeOnly
            : (int.TryParse(seasonText, out var s) ? s : DefaultSeasonForEpisodeOnly);

        if (!int.TryParse(best.Groups["e"].Value, out var episode)) return null;

        // Show name = everything BEFORE the SxxExx marker.
        // Then strip release-group prefix + brackets + junk + stray brackets +
        // release year (if it's part of the show name like "Sherlock (2010)") so
        // the result is the cleanest show name we can produce.
        var showPart = working[..best.Index];
        showPart = AnimeGroupPrefixPattern.Replace(showPart, " ");  // strip [HorribleSubs] etc.
        showPart = LeadingPrefixPattern.Replace(showPart, " ");
        showPart = BracketsPattern.Replace(showPart, " ");
        showPart = JunkTagPattern.Replace(showPart, " ");
        showPart = YearInShowNamePattern.Replace(showPart, " ");    // strip "(2010)" / "2010"
        showPart = StrayBracketPattern.Replace(showPart, " ");
        showPart = SeparatorPattern.Replace(showPart, " ");
        showPart = MultiWhitespacePattern.Replace(showPart, " ").Trim(' ', '-', '_', '.');
        var showName = SmartTitleCase(showPart);

        // Episode title = the text between the marker and the first technical tag,
        // cleaned the same way as the title-side. Order matters:
        //  1) cut at the first tech tag, so trailing release groups ("-ION10",
        //     "-mSD", "AMZN") can never become the title;
        //  2) strip the release group BEFORE junk tags. Junk stripping turns
        //     "x264" into a space, and the release-group pattern deliberately
        //     won't match a dash with a space before it (that's how it preserves
        //     " - Currency"), so running it second let "-ION10" survive.
        var afterStart = best.Index + best.Length;
        var afterPart = afterStart < working.Length ? working[afterStart..] : string.Empty;
        var anchor = TechAnchorPattern.Match(afterPart);
        if (anchor.Success) afterPart = afterPart[..anchor.Index];
        afterPart = BracketsPattern.Replace(afterPart, " ");
        afterPart = StripReleaseGroup(afterPart);
        afterPart = JunkTagPattern.Replace(afterPart, " ");
        afterPart = StrayBracketPattern.Replace(afterPart, " ");
        afterPart = SeparatorPattern.Replace(afterPart, " ");
        afterPart = MultiWhitespacePattern.Replace(afterPart, " ").Trim(' ', '-', '_', '.');
        var episodeTitle = string.IsNullOrEmpty(afterPart) ? string.Empty : SmartTitleCase(afterPart);

        // Confidence: high if we have a show name + plausible season/episode + maybe title
        double confidence = 0.50;
        if (!string.IsNullOrWhiteSpace(showName)) confidence += 0.30;
        if (season is >= 1 and <= 50) confidence += 0.10;
        if (episode is >= 1 and <= 200) confidence += 0.10;
        confidence = Math.Min(confidence, 1.0);

        return new TvEpisodeParseResult(showName, season, episode, episodeTitle, confidence);
    }

    // Common separators
    private static readonly Regex SeparatorPattern = new(@"[\._]+", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    // Edition tags — patterns and their canonical display form. Detected separately
    // from the title (and BEFORE junk-stripping removes them) so the cleaned title
    // is unchanged but the edition is surfaced as its own metadata field.
    // Order matters: more-specific patterns come first so "4K Remaster" wins over
    // bare "Remastered".
    private static readonly (Regex Match, string Canonical)[] EditionPatterns = new (Regex, string)[]
    {
        (new(@"\b4K[\.\s_\-]?(?:Remaster(?:ed)?|Restoration)\b",         RegexOptions.IgnoreCase | RegexOptions.Compiled), "4K Remaster"),
        (new(@"\bDirector(?:['']s|s)?\.?[\s_\.\-]?Cut\b",                RegexOptions.IgnoreCase | RegexOptions.Compiled), "Director's Cut"),
        (new(@"\bExtended\.?[\s_\.\-]?(?:Cut|Edition|Version)\b",        RegexOptions.IgnoreCase | RegexOptions.Compiled), "Extended"),
        (new(@"\bExtended\b",                                            RegexOptions.IgnoreCase | RegexOptions.Compiled), "Extended"),
        (new(@"\bIMAX(?:\.?Enhanced)?\b",                                RegexOptions.IgnoreCase | RegexOptions.Compiled), "IMAX"),
        (new(@"\bTheatrical(?:\.?[\s_\.\-]?(?:Cut|Version))?\b",         RegexOptions.IgnoreCase | RegexOptions.Compiled), "Theatrical"),
        (new(@"\bUnrated\b",                                             RegexOptions.IgnoreCase | RegexOptions.Compiled), "Unrated"),
        (new(@"\bUncut\b",                                               RegexOptions.IgnoreCase | RegexOptions.Compiled), "Uncut"),
        (new(@"\bSpecial\.?[\s_\.\-]?Edition\b",                         RegexOptions.IgnoreCase | RegexOptions.Compiled), "Special Edition"),
        (new(@"\bFinal\.?[\s_\.\-]?Cut\b",                               RegexOptions.IgnoreCase | RegexOptions.Compiled), "Final Cut"),
        (new(@"\bUltimate\.?[\s_\.\-]?Edition\b",                        RegexOptions.IgnoreCase | RegexOptions.Compiled), "Ultimate Edition"),
        (new(@"\bAnniversary\.?[\s_\.\-]?Edition\b",                     RegexOptions.IgnoreCase | RegexOptions.Compiled), "Anniversary Edition"),
        (new(@"\bLimited\.?[\s_\.\-]?Edition\b",                         RegexOptions.IgnoreCase | RegexOptions.Compiled), "Limited Edition"),
        (new(@"\bCollector(?:['']s|s)?\.?[\s_\.\-]?Edition\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled), "Collector's Edition"),
        (new(@"\bCriterion(?:\.?Collection)?\b",                         RegexOptions.IgnoreCase | RegexOptions.Compiled), "Criterion"),
        (new(@"\bOpen\.?[\s_\.\-]?Matte\b",                              RegexOptions.IgnoreCase | RegexOptions.Compiled), "Open Matte"),
        (new(@"\bRemaster(?:ed)?\b",                                     RegexOptions.IgnoreCase | RegexOptions.Compiled), "Remastered"),
    };

    /// <summary>
    /// Scans a filename for an edition tag and returns the canonical form
    /// ("Director's Cut", "Extended", "IMAX", "4K Remaster", …) or empty
    /// if none is present. The first matching pattern wins; patterns are
    /// ordered most-specific-first so e.g. "4K.Remaster" beats "Remastered".
    /// </summary>
    public static string DetectEdition(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return string.Empty;
        foreach (var (rx, canonical) in EditionPatterns)
            if (rx.IsMatch(filename)) return canonical;
        return string.Empty;
    }

    // Words that should stay lowercase in title case
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "for", "nor", "on", "at", "to",
        "from", "by", "in", "of", "as", "vs", "via", "into", "with", "over", "per"
    };

    // Roman-numeral validation: matches strict roman numerals up to a reasonable length.
    private static readonly Regex RomanNumeralPattern = new(
        @"^M{0,3}(?:CM|CD|D?C{0,3})(?:XC|XL|L?X{0,3})(?:IX|IV|V?I{0,3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record FilenameParseResult(string Title, int Year, double Confidence, string Edition);

    /// <summary>
    /// Parses a messy filename into title + year + edition + confidence score.
    /// </summary>
    public static FilenameParseResult ParseFilename(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename) ?? string.Empty;
        var working = nameWithoutExt;

        // 0a. Unicode normalization — replace fancy dashes / smart quotes with ASCII
        working = RemoveCustomWords(NormalizeUnicode(working));   // + Settings "words to always remove"

        // 0b. Normalize bracket-style years: [1996] / {1996} → (1996)
        working = BracketYearPattern.Replace(working, "($1)");

        // 0c. Detect edition NOW (before junk-stripping eats the words). The edition
        //     stays surfaced as a separate field even though the JunkTagPattern below
        //     will remove the same text from the title.
        var edition = DetectEdition(working);

        // 1. Strip tracker URLs at the start
        working = TrackerUrlPattern.Replace(working, " ");

        // 2. SMARTER YEAR DISAMBIGUATION (v1.1)
        //    Find ALL year candidates in the filename, then pick the best one:
        //      a) prefer years inside parens
        //      b) prefer years closer to the END (release year is usually trailing)
        //      c) skip a candidate if removing it leaves a too-short title
        var (year, yearStart, yearLength) = PickBestYear(working);

        // Split title from junk based on the chosen year
        string titlePart;
        bool endsAtYear = false;
        if (year > 0)
        {
            var beforeYear = working[..yearStart];
            var afterYear = working[(yearStart + yearLength)..];
            endsAtYear = beforeYear.Trim().Length >= 3;
            titlePart = endsAtYear ? beforeYear : afterYear;
        }
        else
        {
            titlePart = working;
        }

        // When the year didn't end the title, the first technical tag does:
        // "Some.Movie.1080p.WEB-DL.x264-GRP" → "Some.Movie."
        if (!endsAtYear)
        {
            var anchor = TechAnchorPattern.Match(titlePart);
            if (anchor.Success && anchor.Index > 0) titlePart = titlePart[..anchor.Index];
        }

        // 3. Strip leading release-group prefix
        titlePart = LeadingPrefixPattern.Replace(titlePart, " ");

        // 4. Strip remaining bracketed content
        titlePart = BracketsPattern.Replace(titlePart, " ");

        // 4b. Strip a trailing release group, but never from text that ended at
        //     the year: a group doesn't sit before the year, and stripping there
        //     turned "Spider-Man (2002)" into "Spider". Runs before the junk-tag
        //     strip, which would otherwise turn "x264-GRP" into " -GRP" and hide
        //     the group from the pattern (it skips a dash with a space before it).
        if (!endsAtYear)
            titlePart = StripReleaseGroup(titlePart);

        // 5. Strip technical junk tags
        titlePart = JunkTagPattern.Replace(titlePart, " ");

        // 6. Strip multi-part tags (CD1, Disc 2, etc.)
        //    Note: in v1.1 we DON'T strip "Vol 1" / "Part 2" / "Pt 3" if they appear
        //    BEFORE the year was found, since they're often part of legit titles
        //    (Kill Bill Vol. 1, etc). PartPattern still strips CD/Disc which are
        //    almost always junk.
        titlePart = PartPattern.Replace(titlePart, " ");

        // 8. Replace dots/underscores with spaces
        titlePart = SeparatorPattern.Replace(titlePart, " ");

        // 9. Collapse whitespace and trim
        titlePart = MultiWhitespacePattern.Replace(titlePart, " ").Trim(' ', '-', '_', '.');

        // 10. Smart title case
        var title = SmartTitleCase(titlePart);

        // 11. Confidence scoring
        double confidence = ComputeConfidence(title, year, nameWithoutExt);

        if (string.IsNullOrWhiteSpace(title))
        {
            title = nameWithoutExt;
            confidence = 0.20;
        }

        return new FilenameParseResult(title, year, confidence, edition);
    }

    /// <summary>
    /// Walks all year-candidates in the filename and picks the most likely RELEASE
    /// year (vs a number that happens to look like a year, e.g. "2001 A Space Odyssey").
    /// Scoring:
    ///   +50 if surrounded by parens
    ///   +30 if surrounded by brackets
    ///   +(index/len)*30 to favour years closer to the end
    ///   -100 if removing it would leave the title too short
    /// </summary>
    private static (int year, int matchStart, int matchLength) PickBestYear(string input)
    {
        var matches = YearPattern.Matches(input);
        if (matches.Count == 0) return (0, -1, 0);

        var current = DateTime.Now.Year + 2;
        int bestYear = 0, bestStart = -1, bestLen = 0;
        double bestScore = double.NegativeInfinity;
        int len = Math.Max(input.Length, 1);

        foreach (Match m in matches)
        {
            if (!int.TryParse(m.Groups[1].Value, out var y)) continue;
            if (y < 1900 || y > current) continue;

            // What's left if we strip this match?
            var beforeLen = m.Index;
            var afterLen = input.Length - (m.Index + m.Length);
            var titleSideLen = Math.Max(beforeLen, afterLen);
            // strip surrounding non-letter junk for the sanity check
            if (titleSideLen < 2) continue;  // killing this year leaves nothing — skip

            double score = 0;

            // Inside parens? Strong signal it's the canonical release year.
            var charBefore = m.Index > 0 ? input[m.Index] : ' ';
            var charLast = m.Index + m.Length - 1 >= 0 ? input[m.Index + m.Length - 1] : ' ';
            if (charBefore == '(' || charLast == ')') score += 50;
            else if (charBefore == '[' || charLast == ']') score += 30;

            // Position score — favour later years
            score += (m.Index / (double)len) * 30;

            if (score > bestScore)
            {
                bestScore = score;
                bestYear = y;
                bestStart = m.Index;
                bestLen = m.Length;
            }
        }

        return (bestYear, bestStart, bestLen);
    }

    /// <summary>
    /// Replaces fancy unicode characters (em-dash, en-dash, smart quotes) with ASCII
    /// so downstream patterns don't have to deal with them.
    /// </summary>
    private static string NormalizeUnicode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(c switch
            {
                '‐' or '‑' or '‒' or '–' or '—' or '―' => '-',  // hyphens / dashes
                '‘' or '’' or 'ʼ' => '\'',     // smart single quotes
                '“' or '”' => '"',                  // smart double quotes
                ' ' => ' ',                              // non-breaking space
                '·' or '•' or '‧' => ' ',      // middle dot / bullet
                _ => c
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Smart title case: capitalize each word except small words; always capitalize
    /// the first and last word. Handles roman numerals, hyphens, apostrophes.
    /// </summary>
    public static string SmartTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return input;

        var result = new string[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            bool isFirstOrLast = (i == 0 || i == words.Length - 1);
            bool keepLower = !isFirstOrLast && SmallWords.Contains(words[i]);
            result[i] = keepLower ? words[i].ToLowerInvariant() : CapitalizeWord(words[i]);
        }
        return string.Join(' ', result);
    }

    private static string CapitalizeWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Roman numerals (II, III, IV, etc.) — preserve uppercase
        if (RomanNumeralPattern.IsMatch(word))
            return word.ToUpperInvariant();

        // Handle hyphens (Spider-Man → each part capitalized), but keep small
        // words in the MIDDLE of a compound lowercase: "Monster-in-Law",
        // "Jack-in-the-Box". The last part stays capitalized, since there the
        // hyphen is often a separator: "Dhurandhar-The Revenge", "Hands-On".
        if (word.Contains('-'))
        {
            var parts = word.Split('-');
            return string.Join('-', parts.Select((part, idx) =>
                idx > 0 && idx < parts.Length - 1 && SmallWords.Contains(part)
                    ? part.ToLowerInvariant()
                    : CapitalizeWord(part)));
        }

        // Apostrophe handling: capitalize first letter only, keep rest as-is for
        // names like O'Brien / Marvel's
        if (word.Contains('\''))
        {
            var parts = word.Split('\'');
            return string.Join('\'', parts.Select((p, idx) =>
                idx == 0 ? CapitalizeWord(p) : (p.Length > 0 ? char.ToLowerInvariant(p[0]) + p[1..].ToLowerInvariant() : p)));
        }

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    private static double ComputeConfidence(string title, int year, string original)
    {
        double score = 0;

        if (year >= 1900 && year <= DateTime.Now.Year + 2) score += 0.40;

        if (!string.IsNullOrWhiteSpace(title))
        {
            int len = title.Length;
            if (len >= 2 && len <= 80) score += 0.30;
            else if (len > 0) score += 0.10;
        }

        int letterCount = title.Count(char.IsLetter);
        int totalCount = Math.Max(title.Length, 1);
        if ((double)letterCount / totalCount > 0.6) score += 0.20;

        if (original.Length > 0 && title.Length < original.Length * 0.9) score += 0.10;

        return Math.Min(score, 1.0);
    }
}
