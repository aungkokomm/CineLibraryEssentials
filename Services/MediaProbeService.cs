using System.Reflection;
using CineLibraryEssentials.Models;

namespace CineLibraryEssentials.Services;

/// <summary>
/// Probes a video file for the metadata that goes into Kodi's
/// &lt;fileinfo&gt;&lt;streamdetails&gt; block: duration, dimensions, video codec,
/// and per-track audio/subtitle language &amp; codec.
///
/// Uses TagLib# (already a dependency for the MKV metadata cleaner). For MKV
/// files it digs into TagLib.Matroska.Track via reflection because TagLib#'s
/// public surface doesn't expose per-track language directly. For other
/// containers it falls back to Properties.Codecs which covers MP4/AVI/MOV.
/// Probing is best-effort: anything we can't read is left blank and the NFO
/// writer simply skips that element.
/// </summary>
public class MediaProbeService
{
    public StreamDetails Probe(string filePath)
    {
        var details = new StreamDetails();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return details;

        try
        {
            using var file = TagLib.File.Create(filePath);

            // --- Duration + width/height come from TagLib# Properties for any format ---
            if (file.Properties != null)
            {
                details.Video.DurationInSeconds = (int)file.Properties.Duration.TotalSeconds;
                details.Video.Width = file.Properties.VideoWidth;
                details.Video.Height = file.Properties.VideoHeight;

                if (details.Video.Width > 0 && details.Video.Height > 0)
                    details.Video.Aspect = ((double)details.Video.Width / details.Video.Height).ToString("F2");

                // Codecs are typed (IVideoCodec / IAudioCodec) — pick the first of each
                // for the fallback. MKV path below replaces these with proper per-track
                // info when available.
                foreach (var codec in file.Properties.Codecs)
                {
                    if (codec == null) continue;
                    var desc = codec.Description ?? string.Empty;

                    if ((codec.MediaTypes & TagLib.MediaTypes.Video) != 0
                        && string.IsNullOrEmpty(details.Video.Codec))
                    {
                        details.Video.Codec = NormalizeVideoCodec(desc);
                    }
                    else if ((codec.MediaTypes & TagLib.MediaTypes.Audio) != 0
                             && details.AudioTracks.Count == 0)
                    {
                        var audioStream = new AudioStream
                        {
                            Codec = NormalizeAudioCodec(desc)
                        };
                        if (codec is TagLib.IAudioCodec ac)
                            audioStream.Channels = ac.AudioChannels;
                        details.AudioTracks.Add(audioStream);
                    }
                }
            }

            // --- For MKV, dig into TagLib.Matroska.Track for per-track language info ---
            if (string.Equals(Path.GetExtension(filePath), ".mkv", StringComparison.OrdinalIgnoreCase))
            {
                EnrichFromMatroska(file, details);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaProbeService.Probe failed for {filePath}: {ex.Message}");
        }

        return details;
    }

    /// <summary>
    /// MKV-specific enrichment: walks TagLib.Matroska.File.Tracks via reflection
    /// to extract per-track Language + Codec + TrackType (1=video, 2=audio, 17=sub).
    /// We use reflection because TagLib# doesn't expose Track as a public property
    /// on its abstract File base class, and we don't want to take a hard dependency
    /// on its Matroska namespace at compile time.
    /// </summary>
    private static void EnrichFromMatroska(TagLib.File file, StreamDetails details)
    {
        try
        {
            var tracksProp = file.GetType().GetProperty("Tracks",
                BindingFlags.Public | BindingFlags.Instance);
            if (tracksProp?.GetValue(file) is not System.Collections.IEnumerable tracks)
                return;

            // Reset audio so we capture EVERY audio track in order (not just the first).
            details.AudioTracks.Clear();
            details.SubtitleTracks.Clear();

            foreach (var track in tracks)
            {
                if (track == null) continue;
                var trackType = track.GetType();

                int typeCode = trackType.GetProperty("TrackType")?.GetValue(track) is var tt && tt != null
                    ? Convert.ToInt32(tt) : 0;
                string lang = trackType.GetProperty("Language")?.GetValue(track) as string ?? string.Empty;
                string codecId = trackType.GetProperty("CodecID")?.GetValue(track) as string ?? string.Empty;

                // Matroska track types: 1=video, 2=audio, 17=subtitle
                if (typeCode == 1)
                {
                    if (string.IsNullOrEmpty(details.Video.Codec))
                        details.Video.Codec = NormalizeMatroskaCodec(codecId);
                }
                else if (typeCode == 2)
                {
                    var audioStream = new AudioStream
                    {
                        Codec = NormalizeMatroskaCodec(codecId),
                        Language = NormalizeLanguage(lang)
                    };
                    var channelsProp = trackType.GetProperty("AudioChannels");
                    if (channelsProp?.GetValue(track) is int ch && ch > 0)
                        audioStream.Channels = ch;
                    details.AudioTracks.Add(audioStream);
                }
                else if (typeCode == 17)
                {
                    details.SubtitleTracks.Add(new SubtitleStream
                    {
                        Language = NormalizeLanguage(lang)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnrichFromMatroska reflection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps TagLib#'s human codec description ("H.264", "Microsoft AVC1") onto
    /// the short ids Kodi/Plex/Jellyfin recognise ("h264", "hevc", "av1", …).
    /// </summary>
    private static string NormalizeVideoCodec(string description)
    {
        if (string.IsNullOrEmpty(description)) return string.Empty;
        var lower = description.ToLowerInvariant();
        if (lower.Contains("h.265") || lower.Contains("hevc")) return "hevc";
        if (lower.Contains("h.264") || lower.Contains("avc")) return "h264";
        if (lower.Contains("av1")) return "av1";
        if (lower.Contains("vp9")) return "vp9";
        if (lower.Contains("vp8")) return "vp8";
        if (lower.Contains("mpeg-4") || lower.Contains("xvid") || lower.Contains("divx")) return "mpeg4";
        if (lower.Contains("mpeg-2")) return "mpeg2";
        return lower.Split(' ')[0];
    }

    private static string NormalizeAudioCodec(string description)
    {
        if (string.IsNullOrEmpty(description)) return string.Empty;
        var lower = description.ToLowerInvariant();
        if (lower.Contains("dts-hd")) return "dtshd";
        if (lower.Contains("dts")) return "dts";
        if (lower.Contains("truehd")) return "truehd";
        if (lower.Contains("e-ac-3") || lower.Contains("eac3")) return "eac3";
        if (lower.Contains("ac-3") || lower.Contains("ac3")) return "ac3";
        if (lower.Contains("aac")) return "aac";
        if (lower.Contains("mp3")) return "mp3";
        if (lower.Contains("flac")) return "flac";
        if (lower.Contains("opus")) return "opus";
        if (lower.Contains("vorbis")) return "vorbis";
        return lower.Split(' ')[0];
    }

    /// <summary>
    /// Matroska CodecID prefixes are well-defined (e.g. "V_MPEG4/ISO/AVC" → h264,
    /// "A_AAC" → aac). This maps the common ones; unknown values pass through
    /// lower-cased as a reasonable last-resort.
    /// </summary>
    private static string NormalizeMatroskaCodec(string codecId)
    {
        if (string.IsNullOrEmpty(codecId)) return string.Empty;
        var id = codecId.ToUpperInvariant();

        // Video
        if (id.StartsWith("V_MPEG4/ISO/AVC") || id == "V_MPEGH/ISO/HVC1") return id.Contains("HVC") ? "hevc" : "h264";
        if (id.StartsWith("V_MPEGH/ISO/HEVC") || id.StartsWith("V_MPEG-H")) return "hevc";
        if (id.StartsWith("V_AV1")) return "av1";
        if (id.StartsWith("V_VP9")) return "vp9";
        if (id.StartsWith("V_VP8")) return "vp8";
        if (id.StartsWith("V_MPEG4")) return "mpeg4";
        if (id.StartsWith("V_MPEG2")) return "mpeg2";

        // Audio
        if (id.StartsWith("A_AAC")) return "aac";
        if (id.StartsWith("A_AC3")) return "ac3";
        if (id.StartsWith("A_EAC3")) return "eac3";
        if (id.StartsWith("A_DTS")) return "dts";
        if (id.StartsWith("A_TRUEHD")) return "truehd";
        if (id.StartsWith("A_MPEG/L3")) return "mp3";
        if (id.StartsWith("A_FLAC")) return "flac";
        if (id.StartsWith("A_OPUS")) return "opus";
        if (id.StartsWith("A_VORBIS")) return "vorbis";
        if (id.StartsWith("A_PCM")) return "pcm";

        // Subtitle
        if (id.StartsWith("S_TEXT/UTF8") || id.StartsWith("S_TEXT/ASCII")) return "srt";
        if (id.StartsWith("S_TEXT/ASS") || id.StartsWith("S_TEXT/SSA")) return "ass";
        if (id.StartsWith("S_VOBSUB")) return "vobsub";
        if (id.StartsWith("S_HDMV/PGS")) return "pgs";

        return codecId.ToLowerInvariant();
    }

    /// <summary>
    /// Normalises a Matroska Language tag to ISO 639-2 (3-letter) lowercase,
    /// the convention used in Kodi NFOs. "und" / blank stays blank so the NFO
    /// writer omits the &lt;language&gt; element for that track.
    /// </summary>
    private static string NormalizeLanguage(string lang)
    {
        if (string.IsNullOrEmpty(lang) || lang.Equals("und", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return lang.ToLowerInvariant();
    }
}
