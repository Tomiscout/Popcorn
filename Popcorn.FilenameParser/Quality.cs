namespace Popcorn.FilenameParser;

public enum QualitySource
{
    Unknown,
    Cam,
    Telesync,
    Telecine,
    Workprint,
    DVD,
    Television,
    WebDL,
    WebRip,
    Bluray,
}

public enum QualityModifier
{
    None,
    Regional,
    Screener,
    RawHD,
    BRDisk,
    Remux,
}

/// <summary>
/// Where a release comes from and its vertical resolution (0 = not known). The parser fills in upstream's defaults when
/// the name only says part of it (plain "HDTV" is SD, "BluRay" alone 720p); see <see cref="ParsedQuality"/> for what the
/// name actually said.
/// </summary>
public sealed record Quality(QualitySource Source, int Resolution, QualityModifier Modifier = QualityModifier.None)
{
    public static readonly Quality Unknown = new(QualitySource.Unknown, 0);

    // Pre-release
    public static readonly Quality WORKPRINT = new(QualitySource.Workprint, 0);
    public static readonly Quality CAM = new(QualitySource.Cam, 0);
    public static readonly Quality TELESYNC = new(QualitySource.Telesync, 0);
    public static readonly Quality TELECINE = new(QualitySource.Telecine, 0);
    public static readonly Quality DVDSCR = new(QualitySource.DVD, 480, QualityModifier.Screener);
    public static readonly Quality REGIONAL = new(QualitySource.DVD, 480, QualityModifier.Regional);

    // SD
    public static readonly Quality SDTV = new(QualitySource.Television, 480);
    public static readonly Quality DVD = new(QualitySource.DVD, 480);
    public static readonly Quality DVDR = new(QualitySource.DVD, 480, QualityModifier.Remux);

    public static readonly Quality HDTV720p = new(QualitySource.Television, 720);
    public static readonly Quality HDTV1080p = new(QualitySource.Television, 1080);
    public static readonly Quality HDTV2160p = new(QualitySource.Television, 2160);
    public static readonly Quality RAWHD = new(QualitySource.Television, 1080, QualityModifier.RawHD);

    public static readonly Quality WEBDL480p = new(QualitySource.WebDL, 480);
    public static readonly Quality WEBDL720p = new(QualitySource.WebDL, 720);
    public static readonly Quality WEBDL1080p = new(QualitySource.WebDL, 1080);
    public static readonly Quality WEBDL2160p = new(QualitySource.WebDL, 2160);

    public static readonly Quality WEBRip480p = new(QualitySource.WebRip, 480);
    public static readonly Quality WEBRip720p = new(QualitySource.WebRip, 720);
    public static readonly Quality WEBRip1080p = new(QualitySource.WebRip, 1080);
    public static readonly Quality WEBRip2160p = new(QualitySource.WebRip, 2160);

    public static readonly Quality Bluray480p = new(QualitySource.Bluray, 480);
    public static readonly Quality Bluray576p = new(QualitySource.Bluray, 576);
    public static readonly Quality Bluray720p = new(QualitySource.Bluray, 720);
    public static readonly Quality Bluray1080p = new(QualitySource.Bluray, 1080);
    public static readonly Quality Bluray2160p = new(QualitySource.Bluray, 2160);
    public static readonly Quality Remux1080p = new(QualitySource.Bluray, 1080, QualityModifier.Remux);
    public static readonly Quality Remux2160p = new(QualitySource.Bluray, 2160, QualityModifier.Remux);
    public static readonly Quality BRDISK = new(QualitySource.Bluray, 1080, QualityModifier.BRDisk);
}

/// <param name="Version">2+ for PROPER / REPACK / v2 releases.</param>
/// <param name="Real">Number of REAL tags.</param>
public sealed record Revision(int Version = 1, int Real = 0, bool IsRepack = false);

public sealed class ParsedQuality
{
    public Quality Quality { get; set; } = Quality.Unknown;
    public Revision Revision { get; set; } = new();

    /// <summary>The name states the source (BluRay, WEB-DL, HDTV, ...); otherwise <see cref="Quality"/>'s source is a guess.</summary>
    public bool SourceFromName { get; set; }

    /// <summary>The name states the resolution (1080p, 1920x1080, UHD, ...); otherwise <see cref="Quality"/>'s resolution is a guess.</summary>
    public bool ResolutionFromName { get; set; }

    public override string ToString() => $"{Quality} v{Revision.Version}";
}
