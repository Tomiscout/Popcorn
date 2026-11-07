using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>Source, resolution and revision (PROPER/REPACK/v2) of a release name.</summary>
public static partial class QualityParser
{
    [GeneratedRegex(@"\b(?:
                                                        (?<bluray>M?Blu[-_. ]?Ray|HD[-_. ]?DVD|BD(?!$)|UHD2?BD|BDISO|BDMux|BD25|BD50|BR[-_. ]?DISK)|
                                                        (?<webdl>WEB[-_. ]?DL(?:mux)?|AmazonHD|AmazonSD|iTunesHD|MaxdomeHD|NetflixU?HD|WebHD|HBOMaxHD|DisneyHD|[. ]WEB[. ](?:[xh][ .]?26[456]|AVC|HEVC|EAC3|DDP?[ .]?5[. ]1)|[. ](?-i:WEB)$|(?:\d{3,4}0p)[-. ](?:Hybrid[-_. ]?)?WEB[-. ]|[-. ]WEB[-. ]\d{3,4}0p|\b\s\/\sWEB\s\/\s\b|(?:AMZN|ATVP|DP|NF)[. -]WEB[. -](?!Rip))|
                                                        (?<webrip>WebRip|Web-Rip|WEBMux)|
                                                        (?<hdtv>HDTV)|
                                                        (?<bdrip>BDRip|BDLight|HD[-_. ]?DVDRip|UHDBDRip)|
                                                        (?<brrip>BRRip)|
                                                        (?<dvdr>\d?x?M?DVD-?[R59])|
                                                        (?<dvd>DVD(?!-R)|DVDRip|NTSC|PAL|xvidvd)|
                                                        (?<dsr>WS[-_. ]DSR|DSR)|
                                                        (?<regional>R[0-9]{1}|REGIONAL)|
                                                        (?<scr>SCR|SCREENER|DVDSCR|DVDSCREENER)|
                                                        (?<ts>TS[-_. ]|TELESYNCH?|HD-TS|HDTS|PDVD|TSRip|HDTSRip)|
                                                        (?<tc>TC|TELECINE|HD-TC|HDTC)|
                                                        (?<cam>CAMRIP|(?:NEW)?CAM|HD-?CAM(?:Rip)?|HQCAM)|
                                                        (?<wp>WORKPRINT|WP)|
                                                        (?<pdtv>PDTV)|
                                                        (?<sdtv>SDTV)|
                                                        (?<tvrip>TVRip)
                                                        )(?:\b|$|[ .])", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SourceRegex { get; }

    private static readonly string[] SourceGroups =
        ["bluray", "webdl", "webrip", "hdtv", "bdrip", "brrip", "dvdr", "dvd", "dsr", "regional", "scr", "ts", "tc", "cam", "wp", "pdtv", "sdtv", "tvrip"];

    [GeneratedRegex(@"\b(?<rawhd>RawHD|Raw[-_. ]HD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RawHDRegex { get; }

    [GeneratedRegex(@"\b(?<mpeg2>MPEG[-_. ]?2)\b")]
    private static partial Regex MPEG2Regex { get; }

    [GeneratedRegex(@"^(?!.*\b((?<!HD[._ -]|HD)DVD|BDRip|720p|MKV|XviD|WMV|d3g|(BD)?REMUX|^(?=.*1080p)(?=.*HEVC)|[xh][-_. ]?26[45]|German.*[DM]L|((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2)\b))\b)(((?=.*\b(Blu[-_. ]?ray|BD|HD[-_. ]?DVD)\b)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2|BDMV|ISO)\b))|^((?=.*\b(((?=.*\b((.*_)?COMPLETE.*|Dis[ck])\b)(?=.*(Blu[-_. ]?ray|HD[-_. ]?DVD)))|3D[-_. ]?BD|BR[-_. ]?DISK|Full[-_. ]?Blu[-_. ]?ray|^((?=.*((BD|UHD)[-_. ]?(25|50|66|100|ISO)))))))).*", RegexOptions.IgnoreCase)]
    private static partial Regex BRDISKRegex { get; }

    [GeneratedRegex(@"\b(?<proper>proper)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProperRegex { get; }

    [GeneratedRegex(@"\b(?<repack>repack\d?|rerip\d?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepackRegex { get; }

    [GeneratedRegex(@"\d[-._ ]?v(?<version>\d)[-._ ]|\[v(?<version>\d)\]|repack(?<version>\d)|rerip(?<version>\d)|(?:480|576|720|1080|2160)p[._ ]v(?<version>\d)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex { get; }

    [GeneratedRegex(@"\b(?<real>REAL)\b")]
    private static partial Regex RealRegex { get; }

    [GeneratedRegex(@"\b(?:(?<R360p>360p)|(?<R480p>480p|480i|640x480|848x480)|(?<R540p>540p)|(?<R576p>576p)|(?<R720p>720p|1280x720|960p)|(?<R1080p>1080p|1920x1080|1440p|FHD|1080i|4kto1080p)|(?<R2160p>2160p|3840x2160|4k[-_. ](?:UHD|HEVC|BD|H\.?26[56])|(?:UHD|HEVC|BD|H\.?26[56])[-_. ]4k))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex { get; }

    private static readonly int[] Resolutions = [360, 480, 540, 576, 720, 1080, 2160];

    // Handle cases where no resolution is in the release name (assume if UHD then 4k) or resolution is non-standard
    [GeneratedRegex(@"\b(?<R2160p>UHD)\b|(?<R2160p>\[4K\])", RegexOptions.IgnoreCase)]
    private static partial Regex AlternativeResolutionRegex { get; }

    [GeneratedRegex(@"\b(?:(?<x264>x264)|(?<h264>h264)|(?<xvidhd>XvidHD)|(?<xvid>X-?vid)|(?<divx>divx))\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodecRegex { get; }

    [GeneratedRegex(@"(?<hdtv>HD[-_. ]TV)|(?<sdtv>SD[-_. ]TV)", RegexOptions.IgnoreCase)]
    private static partial Regex OtherSourceRegex { get; }

    [GeneratedRegex(@"bd(?:720|1080|2160)|(?<=[-_. (\[])bd(?=[-_. )\]])", RegexOptions.IgnoreCase)]
    private static partial Regex AnimeBlurayRegex { get; }
    [GeneratedRegex(@"\[WEB\]|[\[\(]WEB[ .]", RegexOptions.IgnoreCase)]
    private static partial Regex AnimeWebDlRegex { get; }

    // "Bluray1080p" (no separator, so neither the source nor the resolution regex sees it).
    private static readonly (string Tag, Quality Quality)[] GluedBlurayTags =
        [("bluray720p", Quality.Bluray720p), ("bluray1080p", Quality.Bluray1080p), ("bluray2160p", Quality.Bluray2160p)];

    [GeneratedRegex(@"hr[-_. ]ws", RegexOptions.IgnoreCase)]
    private static partial Regex HighDefPdtvRegex { get; }

    [GeneratedRegex(@"(?:[_. \[]|\d{4}p-|\bHybrid-)(?<remux>(?:(BD|UHD)[-_. ]?)?Remux)\b|(?<remux>(?:(BD|UHD)[-_. ]?)?Remux[_. ]\d{4}p)", RegexOptions.IgnoreCase)]
    private static partial Regex RemuxRegex { get; }
    [GeneratedRegex(@"((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[_. -]?1|MVC|MPEG[_. -]?2))(?=.*Blu-?ray)", RegexOptions.IgnoreCase)]
    private static partial Regex GermanRemuxRegex { get; }

    public static ParsedQuality ParseQuality(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ParsedQuality();
        }

        name = name.Trim();
        var normalizedName = name.Replace('_', ' ').Trim();
        var resolution = ParseResolution(normalizedName);

        var result = new ParsedQuality
        {
            Revision = ParseRevision(name, normalizedName),
            ResolutionFromName = resolution != 0,
        };
        result.Quality = Classify(name, normalizedName, resolution, result);

        return result;
    }

    // Upstream's decision tree: the last source tag wins, then resolution, then codec / resolution-only hints.
    private static Quality Classify(string name, string normalizedName, int resolution, ParsedQuality result)
    {
        var sourceMatches = SourceRegex.Matches(normalizedName);
        var sourceMatch = sourceMatches.LastOrDefault();
        var codec = CodecRegex.Match(normalizedName);
        var remux = RemuxRegex.IsMatch(normalizedName) || GermanRemuxRegex.IsMatch(normalizedName);
        var brDisk = BRDISKRegex.IsMatch(normalizedName);

        if (RawHDRegex.IsMatch(normalizedName) && !brDisk)
        {
            result.SourceFromName = true;
            result.ResolutionFromName = true;
            return Quality.RAWHD;
        }

        if (sourceMatch != null)
        {
            result.SourceFromName = true;

            var source = SourceGroups.First(g => sourceMatch.Groups[g].Success);

            // NTSC / PAL only name the TV standard; a DVD5 / DVD-R tag elsewhere says more.
            if (source == "dvd" && sourceMatches.Any(m => m.Groups["dvdr"].Success))
            {
                source = "dvdr";
            }

            switch (source)
            {
                case "bluray":
                    if (brDisk)
                    {
                        return Quality.BRDISK;
                    }

                    if (codec.Groups["xvid"].Success || codec.Groups["divx"].Success)
                    {
                        return Quality.Bluray480p;
                    }

                    return resolution switch
                    {
                        2160 => remux ? Quality.Remux2160p : Quality.Bluray2160p,
                        1080 => remux ? Quality.Remux1080p : Quality.Bluray1080p,
                        720 => Quality.Bluray720p,
                        576 => Quality.Bluray576p,
                        360 or 480 or 540 => Quality.Bluray480p,

                        // A remux without a resolution is treated as 1080p.
                        _ => remux ? Quality.Remux1080p : Quality.Bluray720p,
                    };

                case "webdl":
                    return resolution switch
                    {
                        2160 => Quality.WEBDL2160p,
                        1080 => Quality.WEBDL1080p,
                        720 => Quality.WEBDL720p,
                        _ => name.Contains("[WEBDL]") ? Quality.WEBDL720p : Quality.WEBDL480p,
                    };

                case "webrip":
                    return resolution switch
                    {
                        2160 => Quality.WEBRip2160p,
                        1080 => Quality.WEBRip1080p,
                        720 => Quality.WEBRip720p,
                        _ => Quality.WEBRip480p,
                    };

                case "hdtv":
                    if (MPEG2Regex.IsMatch(normalizedName))
                    {
                        return Quality.RAWHD;
                    }

                    return resolution switch
                    {
                        2160 => Quality.HDTV2160p,
                        1080 => Quality.HDTV1080p,
                        720 => Quality.HDTV720p,
                        _ => name.Contains("[HDTV]") ? Quality.HDTV720p : Quality.SDTV,
                    };

                case "bdrip" or "brrip":
                    return resolution switch
                    {
                        720 => Quality.Bluray720p,
                        1080 => Quality.Bluray1080p,
                        2160 => Quality.Bluray2160p,
                        576 => Quality.Bluray576p,
                        _ => Quality.Bluray480p,
                    };

                case "dvdr":
                    return Quality.DVDR;
                case "dvd":
                    return Quality.DVD;
                case "scr":
                    return Quality.DVDSCR;
                case "regional":
                    return Quality.REGIONAL;
                case "cam":
                    return Quality.CAM;
                case "ts":
                    return Quality.TELESYNC with { Resolution = resolution };
                case "tc":
                    return Quality.TELECINE;
                case "wp":
                    return Quality.WORKPRINT;

                default: // pdtv, sdtv, dsr, tvrip
                    if (resolution == 1080 || Contains(normalizedName, "1080p"))
                    {
                        return Quality.HDTV1080p;
                    }

                    if (resolution == 720 || Contains(normalizedName, "720p"))
                    {
                        return Quality.HDTV720p;
                    }

                    if (HighDefPdtvRegex.IsMatch(normalizedName))
                    {
                        result.ResolutionFromName = true;
                        return Quality.HDTV720p;
                    }

                    return Quality.SDTV;
            }
        }

        if (remux)
        {
            var remuxQuality = resolution switch
            {
                480 => Quality.Bluray480p,
                720 => Quality.Bluray720p,
                1080 => Quality.Remux1080p,
                2160 => Quality.Remux2160p,
                _ => null,
            };

            if (remuxQuality != null)
            {
                return remuxQuality;
            }
        }

        if (AnimeBlurayRegex.IsMatch(normalizedName))
        {
            result.SourceFromName = true;

            if (resolution is 360 or 480 or 540 or 576 || Contains(normalizedName, "480p"))
            {
                result.ResolutionFromName = true;
                return Quality.DVD;
            }

            if (resolution == 1080 || Contains(normalizedName, "1080p"))
            {
                result.ResolutionFromName = true;
                return remux ? Quality.Remux1080p : Quality.Bluray1080p;
            }

            if (resolution == 2160 || Contains(normalizedName, "2160p"))
            {
                result.ResolutionFromName = true;
                return remux ? Quality.Remux2160p : Quality.Bluray2160p;
            }

            // A remux without a resolution is treated as 1080p; a 720p one as plain 720p Bluray.
            return remux && resolution != 720 ? Quality.Remux1080p : Quality.Bluray720p;
        }

        if (AnimeWebDlRegex.IsMatch(normalizedName))
        {
            result.SourceFromName = true;

            if (resolution is 360 or 480 or 540 or 576 || Contains(normalizedName, "480p"))
            {
                result.ResolutionFromName = true;
                return Quality.WEBDL480p;
            }

            if (resolution == 1080 || Contains(normalizedName, "1080p"))
            {
                result.ResolutionFromName = true;
                return Quality.WEBDL1080p;
            }

            if (resolution == 2160 || Contains(normalizedName, "2160p"))
            {
                result.ResolutionFromName = true;
                return Quality.WEBDL2160p;
            }

            return Quality.WEBDL720p;
        }

        if (resolution != 0)
        {
            if (remux)
            {
                // Remux at a resolution no remux quality exists for (360p, 540p, 576p).
                result.SourceFromName = true;
                return Quality.Unknown;
            }

            return resolution switch
            {
                2160 => Quality.HDTV2160p,
                1080 => Quality.HDTV1080p,
                720 => Quality.HDTV720p,
                _ => Quality.SDTV,
            };
        }

        if (codec.Groups["x264"].Success)
        {
            return Quality.SDTV;
        }

        foreach (var (tag, quality) in GluedBlurayTags)
        {
            if (Contains(normalizedName, tag))
            {
                result.SourceFromName = true;
                result.ResolutionFromName = true;
                return quality;
            }
        }

        var otherSource = OtherSourceRegex.Match(normalizedName);

        if (otherSource.Success)
        {
            result.SourceFromName = true;
            return otherSource.Groups["sdtv"].Success ? Quality.SDTV : Quality.HDTV720p;
        }

        return Quality.Unknown;
    }

    /// <summary>Vertical resolution the name states, 0 if none.</summary>
    private static int ParseResolution(string name)
    {
        var match = ResolutionRegex.Match(name);

        if (match.Success)
        {
            return Resolutions.First(r => match.Groups[$"R{r}p"].Success);
        }

        return AlternativeResolutionRegex.IsMatch(name) ? 2160 : 0;
    }

    private static Revision ParseRevision(string name, string normalizedName)
    {
        var versionMatch = VersionRegex.Match(normalizedName);
        var version = versionMatch.Success ? Convert.ToInt32(versionMatch.Groups["version"].Value) : 1;
        var isRepack = RepackRegex.IsMatch(normalizedName);

        if (isRepack || ProperRegex.IsMatch(normalizedName))
        {
            version = versionMatch.Success ? version + 1 : 2;
        }

        // TODO (upstream): only treat it as REAL if it comes after the season/episode number.
        return new Revision(version, RealRegex.Matches(name).Count, isRepack);
    }

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.InvariantCultureIgnoreCase);
}
