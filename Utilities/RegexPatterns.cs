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

    // Trailing release group like "-RARBG" — only if it follows a hyphen/underscore at the end
    private static readonly Regex ReleaseGroupPattern = new(
        @"[\-_]\s*[A-Za-z0-9]{2,20}\s*$",
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

    // Common separators
    private static readonly Regex SeparatorPattern = new(@"[\._]+", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespacePattern = new(@"\s+", RegexOptions.Compiled);

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

    public record FilenameParseResult(string Title, int Year, double Confidence);

    /// <summary>
    /// Parses a messy filename into title + year + confidence score.
    /// </summary>
    public static FilenameParseResult ParseFilename(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename) ?? string.Empty;
        var working = nameWithoutExt;

        // 0a. Unicode normalization — replace fancy dashes / smart quotes with ASCII
        working = NormalizeUnicode(working);

        // 0b. Normalize bracket-style years: [1996] / {1996} → (1996)
        working = BracketYearPattern.Replace(working, "($1)");

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

        // 7. Replace dots/underscores with spaces
        titlePart = SeparatorPattern.Replace(titlePart, " ");

        // 8. Strip trailing release group
        titlePart = ReleaseGroupPattern.Replace(titlePart, " ");

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

        return new FilenameParseResult(title, year, confidence);
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
