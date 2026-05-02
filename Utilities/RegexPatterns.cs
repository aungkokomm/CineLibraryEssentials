using System.Text.RegularExpressions;

namespace CineLibraryEssentials.Utilities;

/// <summary>
/// Movie filename parser. Strips technical/junk tags and extracts the title and year.
/// Designed to handle the messiest real-world download names.
/// </summary>
public static class RegexPatterns
{
    // Step 0: normalize "[1996]" or "{1996}" → "(1996)" so the format is consistent.
    // Applied as a pre-pass before any other parsing so downstream patterns only have
    // to think about ONE year delimiter style.
    private static readonly Regex BracketYearPattern = new(
        @"[\[\{]\s*(19\d{2}|20[0-3]\d)\s*[\]\}]",
        RegexOptions.Compiled);

    // Year: 4-digit number 1900-2030, optionally in parens/brackets, with separator boundaries
    private static readonly Regex YearPattern = new(
        @"(?:[\(\[\{\.\-\s_]|^)(19\d{2}|20[0-3]\d)(?:[\)\]\}\.\-\s_]|$)",
        RegexOptions.Compiled);

    // Tracker URLs at the start: "www.SomeTracker.com - " or "[X.com]"
    private static readonly Regex TrackerUrlPattern = new(
        @"(?:www\.|http(?:s)?:\/\/)\S+?\.\w{2,5}(?:\s*[\-_]\s*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Leading release-group / source prefix that appears at the very start of the
    // filename. Two forms:
    //   1. Bracketed:  "[Group] Title.year..."
    //   2. Keyword + dot:  "UnTouch.Title.year..."
    // We require a literal dot for keyword-form prefixes so we don't accidentally
    // strip a movie that happens to start with the same word (e.g. "MAX (2002)").
    private static readonly Regex LeadingPrefixPattern = new(
        @"^\s*(?:" +
        // Bracketed prefix
        @"[\[\(\{][^\]\)\}]+[\]\)\}]" +
        @"|" +
        // Known release-group / source keywords followed by a dot
        @"(?:UnTouch(?:ed)?|AMZN|NF|HULU|DSNP|ATVP|iT|HBO|HMAX|PCOK|STAN|CR|FUNi" +
        @"|GalaxyRG|GalaxyTV|YIFY|YTS|RARBG|EVO|Vyndros|TGx|MeGusta|Tigole|EtHD" +
        @"|JustWatch|Cinephiles|Surtsy|FraMeSToR|Pahe|EZTV|FUM|PSA|RARBGx|d3g)" +
        @"\." +
        @")\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Brackets: [...], (...), {...} — anything inside (we extract year separately first)
    private static readonly Regex BracketsPattern = new(
        @"[\[\(\{][^\]\)\}]*[\]\)\}]",
        RegexOptions.Compiled);

    // Big "junk tags" pattern: resolution, codec, audio, HDR, quality source, edition, language
    // Combined into one alternation so a single Replace pass strips them all.
    private static readonly Regex JunkTagPattern = new(
        @"\b(?:" +
        // Resolution
        @"4k|2160p?|1440p?|1080p?|720p?|576p?|480p?|360p?|UHD|FHD|HD|SD" +
        // Video codec
        @"|x264|x265|h\.?264|h\.?265|HEVC|AVC|XviD|DivX|VP9|AV1|10bit|8bit|10\-bit|8\-bit" +
        // Audio
        @"|AAC|AC3|EAC3|DDP?(?:[57]\.[01])?|DD\+|DTS(?:\-?HD|\-?X)?|FLAC|MP3|TrueHD|Atmos|Opus|MA" +
        @"|[57]\.[01]" +
        // HDR
        @"|HDR(?:10\+?|10Plus)?|DV|DolbyVision|Dolby\.Vision" +
        // Source/quality
        @"|Blu\-?Ray|BDRip|BRRip|BRip|BD(?:25|50|66|100)?|REMUX|WEB\-?DL|WEB\-?Rip|WEBRip|WEB" +
        @"|HDTV|HDTC|HDCAM|HDRip|DVDRip|DVD\-?R|DVD|TVRip|CAM|TS|TC|PDTV|PPV|VHS" +
        // Edition
        @"|Extended|Director\.?s?\.?Cut|Theatrical|Unrated|Uncut|Remastered|Special\.?Edition" +
        @"|Final\.?Cut|IMAX|REPACK|PROPER|INTERNAL|LIMITED|RERIP|Criterion" +
        // Language hints (only standalone codes)
        @"|DUAL|MULTI|MULTi|VOSTFR|Hindi|English|Spanish|French|German|Italian" +
        @"|Japanese|Korean|Chinese|Tamil|Telugu" +
        @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Release group: trailing "-GROUP" (after all other cleaning)
    private static readonly Regex ReleaseGroupPattern = new(
        @"[\-_]\s*[A-Za-z0-9]{2,20}\s*$",
        RegexOptions.Compiled);

    // Multi-part tags
    private static readonly Regex PartPattern = new(
        @"\b(?:CD|Disc|Disk|Part|Pt)\s*[0-9]+\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // TV-episode markers: S01E03, 1x03, Episode 4, Season 1, etc.
    private static readonly Regex TvEpisodePattern = new(
        @"\b(?:S\d{1,2}E\d{1,2}|S\d{1,2}\.E\d{1,2}|\d{1,2}x\d{1,2}|Episode[\.\s_]\d+|Season[\.\s_]\d+|E\d{2,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Returns true if the filename looks like a TV episode rather than a movie.</summary>
    public static bool IsTvEpisode(string filename)
        => !string.IsNullOrEmpty(filename) && TvEpisodePattern.IsMatch(filename);

    // Common separators in filenames
    private static readonly Regex SeparatorPattern = new(
        @"[\._]+",
        RegexOptions.Compiled);

    private static readonly Regex MultiWhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled);

    // Words that should stay lowercase in title case (unless first/last word)
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "for", "nor", "on", "at", "to",
        "from", "by", "in", "of", "as", "vs", "via", "into", "with", "over", "per"
    };

    public record FilenameParseResult(string Title, int Year, double Confidence);

    /// <summary>
    /// Parses a messy filename into title + year + confidence score.
    /// </summary>
    public static FilenameParseResult ParseFilename(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename) ?? string.Empty;
        var working = nameWithoutExt;

        // 0. Normalize bracket-style years: [1996] / {1996} → (1996)
        //    Done first so the rest of the pipeline only sees one year format.
        working = BracketYearPattern.Replace(working, "($1)");

        // 1. Strip tracker URLs at the start
        working = TrackerUrlPattern.Replace(working, " ");

        // 2. Find year and SPLIT the filename into "before year" and "after year".
        //    For movies, everything after the year is technical junk (resolution,
        //    codecs, audio, subs, release group, etc.) and we just discard it.
        //    The title is whichever side has meaningful content.
        int year = 0;
        string titlePart = working;
        var yearMatch = YearPattern.Match(working);
        if (yearMatch.Success)
        {
            int.TryParse(yearMatch.Groups[1].Value, out year);
            var beforeYear = working.Substring(0, yearMatch.Index);
            var afterYear = working.Substring(yearMatch.Index + yearMatch.Length);
            // If "before" has content, use it; otherwise the year was at the start
            // and the title is on the right (e.g. "[1996] Movie Title").
            titlePart = beforeYear.Trim().Length >= 3 ? beforeYear : afterYear;
        }

        // 3. Strip leading release-group prefix: "[Group] " or "UnTouch." style
        titlePart = LeadingPrefixPattern.Replace(titlePart, " ");

        // 4. Strip any remaining bracketed content
        titlePart = BracketsPattern.Replace(titlePart, " ");

        // 5. Strip technical junk tags (in case any appear in the title portion)
        titlePart = JunkTagPattern.Replace(titlePart, " ");

        // 6. Strip multi-part tags (CD1, Disc 2, etc.)
        titlePart = PartPattern.Replace(titlePart, " ");

        // 7. Replace dots/underscores with spaces
        titlePart = SeparatorPattern.Replace(titlePart, " ");

        // 8. Strip trailing release group like "-RARBG"
        titlePart = ReleaseGroupPattern.Replace(titlePart, " ");

        // 9. Collapse whitespace and trim leading/trailing junk chars
        titlePart = MultiWhitespacePattern.Replace(titlePart, " ").Trim(' ', '-', '_', '.');

        // 10. Apply smart title case
        var title = SmartTitleCase(titlePart);

        // 11. Confidence scoring
        double confidence = ComputeConfidence(title, year, nameWithoutExt);

        // Fallback to original if cleaning destroyed everything
        if (string.IsNullOrWhiteSpace(title))
        {
            title = nameWithoutExt;
            confidence = 0.20;
        }

        return new FilenameParseResult(title, year, confidence);
    }

    /// <summary>
    /// Smart title case: capitalize each word except small words ("of", "the", "a", etc.),
    /// but always capitalize the first and last word. Handles hyphens (Spider-Man) and
    /// apostrophes (O'Brien) properly.
    /// </summary>
    public static string SmartTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return input;

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
        if (string.IsNullOrEmpty(word))
            return word;

        // Roman numerals (II, III, IV, etc.) — preserve uppercase
        if (Regex.IsMatch(word, @"^[IVX]+$", RegexOptions.IgnoreCase))
            return word.ToUpperInvariant();

        // Handle hyphens (Spider-Man → each part capitalized)
        if (word.Contains('-'))
            return string.Join('-', word.Split('-').Select(CapitalizeWord));

        // Default: first letter upper, rest lower
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    private static double ComputeConfidence(string title, int year, string original)
    {
        double score = 0;

        // Year present and plausible
        if (year >= 1900 && year <= DateTime.Now.Year + 2) score += 0.40;

        // Title is non-empty and reasonable length
        if (!string.IsNullOrWhiteSpace(title))
        {
            int len = title.Length;
            if (len >= 2 && len <= 80) score += 0.30;
            else if (len > 0) score += 0.10;
        }

        // Title contains mostly letters (not just numbers/symbols)
        int letterCount = title.Count(char.IsLetter);
        int totalCount = Math.Max(title.Length, 1);
        if ((double)letterCount / totalCount > 0.6) score += 0.20;

        // Cleaned title is significantly shorter than original (we removed a lot of junk)
        if (original.Length > 0 && title.Length < original.Length * 0.9) score += 0.10;

        return Math.Min(score, 1.0);
    }
}
