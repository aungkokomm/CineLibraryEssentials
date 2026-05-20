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
    };

    // Stray bracket characters left over after splitting around the SxxExx marker
    // (e.g. "Sherlock (" or "A Study in Pink)").
    private static readonly Regex StrayBracketPattern = new(
        @"[\(\)\[\]\{\}]", RegexOptions.Compiled);

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
        var working = NormalizeUnicode(nameWithoutExt);

        Match? best = null;
        foreach (var rx in TvEpisodeCaptures)
        {
            var m = rx.Match(working);
            if (m.Success) { best = m; break; }
        }
        if (best == null) return null;

        if (!int.TryParse(best.Groups["s"].Value, out var season)) return null;
        if (!int.TryParse(best.Groups["e"].Value, out var episode)) return null;

        // Show name = everything BEFORE the SxxExx marker.
        // Then strip junk tags + release-group prefix + brackets like the movie path.
        // The marker may sit inside parentheses — e.g. "Sherlock (Season 01, Episode 01
        // - A Study in Pink)" — which leaves a dangling "(" on the show side and ")"
        // on the title side, so we explicitly remove stray bracket characters too.
        var showPart = working[..best.Index];
        showPart = LeadingPrefixPattern.Replace(showPart, " ");
        showPart = BracketsPattern.Replace(showPart, " ");
        showPart = JunkTagPattern.Replace(showPart, " ");
        showPart = StrayBracketPattern.Replace(showPart, " ");
        showPart = SeparatorPattern.Replace(showPart, " ");
        showPart = MultiWhitespacePattern.Replace(showPart, " ").Trim(' ', '-', '_', '.');
        var showName = SmartTitleCase(showPart);

        // Episode title = whatever sits AFTER the marker, cleaned the same way as
        // the title-side. Order matters: release-group strip runs BEFORE separator
        // replacement so " - Currency" (TV title separator) is preserved while
        // "-RARBG" (real release group) is still caught.
        var afterStart = best.Index + best.Length;
        var afterPart = afterStart < working.Length ? working[afterStart..] : string.Empty;
        afterPart = BracketsPattern.Replace(afterPart, " ");
        afterPart = JunkTagPattern.Replace(afterPart, " ");
        afterPart = ReleaseGroupPattern.Replace(afterPart, " ");
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
        working = NormalizeUnicode(working);

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
        if (year > 0)
        {
            var beforeYear = working[..yearStart];
            var afterYear = working[(yearStart + yearLength)..];
            titlePart = beforeYear.Trim().Length >= 3 ? beforeYear : afterYear;
        }
        else
        {
            titlePart = working;
        }

        // 3. Strip leading release-group prefix
        titlePart = LeadingPrefixPattern.Replace(titlePart, " ");

        // 4. Strip remaining bracketed content
        titlePart = BracketsPattern.Replace(titlePart, " ");

        // 5. Strip technical junk tags
        titlePart = JunkTagPattern.Replace(titlePart, " ");

        // 6. Strip multi-part tags (CD1, Disc 2, etc.)
        //    Note: in v1.1 we DON'T strip "Vol 1" / "Part 2" / "Pt 3" if they appear
        //    BEFORE the year was found, since they're often part of legit titles
        //    (Kill Bill Vol. 1, etc). PartPattern still strips CD/Disc which are
        //    almost always junk.
        titlePart = PartPattern.Replace(titlePart, " ");

        // 7. Strip trailing release group — MUST happen before dots become spaces,
        //    so "-RARBG" still has its dot-or-letter prefix and the lookbehind
        //    correctly distinguishes it from a " - Title" episode separator.
        titlePart = ReleaseGroupPattern.Replace(titlePart, " ");

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

        // Handle hyphens (Spider-Man → each part capitalized)
        if (word.Contains('-'))
            return string.Join('-', word.Split('-').Select(CapitalizeWord));

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
