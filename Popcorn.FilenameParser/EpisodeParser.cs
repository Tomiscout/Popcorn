using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>
/// Episode release names: S01E02, 1x02, daily (2024.03.05), anime absolute (Show - 105), season packs. Sonarr's parser.
/// It assumes the name is an episode; movie names usually fail, but a bare year can read as season + episode
/// ("Movie.2010" -> S20E10), see <see cref="MediaNameParser"/>.
/// </summary>
public static partial class EpisodeParser
{
    private static readonly Regex[] ReportTitleRegex =
    [
        ReportTitle0,
        ReportTitle1,
        ReportTitle2,
        ReportTitle3,
        ReportTitle4,
        ReportTitle5,
        ReportTitle6,
        ReportTitle7,
        ReportTitle8,
        ReportTitle9,
        ReportTitle10,
        ReportTitle11,
        ReportTitle12,
        ReportTitle13,
        ReportTitle14,
        ReportTitle15,
        ReportTitle16,
        ReportTitle17,
        ReportTitle18,
        ReportTitle19,
        ReportTitle20,
        ReportTitle21,
        ReportTitle22,
        ReportTitle23,
        ReportTitle24,
        ReportTitle25,
        ReportTitle26,
        ReportTitle27,
        ReportTitle28,
        ReportTitle29,
        ReportTitle30,
        ReportTitle31,
        ReportTitle32,
        ReportTitle33,
        ReportTitle34,
        ReportTitle35,
        ReportTitle36,
        ReportTitle37,
        ReportTitle38,
        ReportTitle39,
        ReportTitle40,
        ReportTitle41,
        ReportTitle42,
        ReportTitle43,
        ReportTitle44,
        ReportTitle45,
        ReportTitle46,
        ReportTitle47,
        ReportTitle48,
        ReportTitle49,
        ReportTitle50,
        ReportTitle51,
        ReportTitle52,
        ReportTitle53,
        ReportTitle54,
        ReportTitle55,
        ReportTitle56,
        ReportTitle57,
        ReportTitle58,
        ReportTitle59,
        ReportTitle60,
        ReportTitle61,
        ReportTitle62,
        ReportTitle63,
        ReportTitle64,
        ReportTitle65,
        ReportTitle66,
        ReportTitle67,
        ReportTitle68,
        ReportTitle69,
        ReportTitle70,
        ReportTitle71,
        ReportTitle72,
        ReportTitle73,
        ReportTitle74,
        ReportTitle75,
        ReportTitle76,
        ReportTitle77,
        ReportTitle78,
        ReportTitle79,
        ReportTitle80,
        ReportTitle81,
        ReportTitle82,
        ReportTitle83,
        ReportTitle84,
        ReportTitle85,
        ReportTitle86,
        ReportTitle87,
        ReportTitle88,
        ReportTitle89,
        ReportTitle90,
        ReportTitle91,
        ReportTitle92,
        ReportTitle93,
        ReportTitle94,
        ReportTitle95,
        ReportTitle96,
        ReportTitle97,
        ReportTitle98,
        ReportTitle99,
        ReportTitle100,
        ReportTitle101,
        ReportTitle102,
        ReportTitle103,
        ReportTitle104,
        ReportTitle105,
        ReportTitle106,
        ReportTitle107,
        ReportTitle108,
    ];

    // Anime - Absolute Episode Number + Title + Season+Episode
    // Todo: This currently breaks series that start with numbers
    //                new Regex(@"^(?:(?<absoluteepisode>\d{2,3})(?:_|-|\s|\.)+)+(?<title>.+?)(?:\W|_)+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+)",
    //                          RegexOptions.IgnoreCase | RegexOptions.Compiled),

    // Daily episode with year in series title and air time after date (Plex DVR format)
    [GeneratedRegex(@"^^(?<title>.+?\((?<titleyear>\d{4})\))[-_. ]+(?<airyear>19[4-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])[-_. ]\d{2}[-_. ]\d{2}[-_. ]\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle0 { get; }

    // Daily episodes without title (2018-10-12, 20181012) (Strict pattern to avoid false matches)
    [GeneratedRegex(@"^(?<airyear>19[6-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle1 { get; }

    // Multi-Part episodes without a title (S01E05.S01E06)
    [GeneratedRegex(@"^(?:\W*S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:e{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle2 { get; }

    // Multi-Part episodes without a title (1x05.1x06)
    [GeneratedRegex(@"^(?:\W*(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle3 { get; }

    // Episodes without a title, Multi (S01E04E05, 1x04x05, etc)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_]|[ex]){1,2}(?<episode>\d{2,3}(?!\d+))){2,})", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle4 { get; }

    // Split episodes (S01E05a, S01E05b, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))(?<splitepisode>[a-d])(?:[ _.])))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle5 { get; }

    // Episodes without a title, Single (S01E05, 1x05)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle6 { get; }

    // Anime - [SubGroup] Title Absolute (Season+Episode)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ])+\((?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:\)(?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle7 { get; }

    // Anime - [SubGroup] Title Absolute - Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ](?<![()\[!]))+(?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle8 { get; }

    // Anime - [SubGroup] Title Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+)(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle9 { get; }

    // Anime - [SubGroup] Title Episode Absolute Episode Number ([SubGroup] Series Title Episode 01)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle10 { get; }

    // Anime - [SubGroup] Title Absolute Episode Number + Season+Episode
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?))+(?:_|-|\s|\.)+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle11 { get; }

    // Anime - [SubGroup] Title Multi-episode + optional Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))){2,})(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))*.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle12 { get; }

    // Anime - [SubGroup] Title Single episode + Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle13 { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number - Batch separated with tilde
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\s?~\s?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle14 { get; }

    // Anime - [SubGroup] Title with season number in brackets Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)[_. ]+?\(Season[_. ](?<season>\d+)\)[-_. ]+?(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle15 { get; }

    // Anime - [SubGroup] Title with trailing 3-digit number and sub title - Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+?)\][-_. ]?(?<title>[^]]+?)(?:[-_. ]{3}?(?<absoluteepisode>\d{2}(\.\d{1,2})?(?!-?\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle16 { get; }

    // Anime - [SubGroup] Multi-season pack
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:-S?|[-_. ]{1,3}S)(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle17 { get; }

    // Anime - [SubGroup] Partial season pack
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle18 { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle19 { get; }

    // Anime - [SubGroup] Title with trailing number S## (Full season)
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+(?:S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?![ex]?\d+))).+?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle20 { get; }

    // Anime - [SubGroup] Title with trailing number Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+)[_ ]+)(?:[-_. ]?(?<absoluteepisode>\d{3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle21 { get; }

    // Anime - [SubGroup] Title - Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?:[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-])))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle22 { get; }

    // Anime - [SubGroup] Title Absolute Episode Number - Absolute Episode Number (batches without full separator between title and absolute episode numbers)
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle23 { get; }

    // Anime - [SubGroup] Title (Season only)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ])?(?<title>.+?)[-_. ]+?[\[(](?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. )\]]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle24 { get; }

    // Anime - [SubGroup] Title Absolute Episode Number
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+\(?(?:[-_. ]?#?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+\)?(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle25 { get; }

    // Multi-episode Repeated (S01E05 - S01E06)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:e|[-_. ]e){1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle26 { get; }

    // Multi-episode Repeated (1x05 - 1x06)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle27 { get; }

    // Single episodes with a title (S01E05, 1x05, etc) followed by ".5 [SP]"
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))(?<special>\.5[ .]\[SP\]))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle28 { get; }

    // Single episodes with a title (S01E05, 1x05, etc) and trailing info in slashes
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))).+?(?:\[.+?\])(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle29 { get; }

    // Anime - Title Season EpisodeNumber + Absolute Episode Number [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]|-){1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+)[-_. (]+?(?:[-_. ]?(?<absoluteepisode>(?<!\d+)\d{3}(\.\d{1,2})?(?!\d+|[pi])))+.+?\[(?<subgroup>.+?)\](?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle30 { get; }

    // Multi-Episode with a title (S01E05E06, S01E05-06, S01E05 E06, etc) and trailing info in slashes
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))+).+?(?:\[.+?\])(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle31 { get; }

    // Anime - Title Absolute Episode Number [SubGroup] [Hash]? (Series Title Episode 99-100 [RlsGroup] [ABCD1234])
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]Episode(?:[-_. ]+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle32 { get; }

    // Anime - Title Absolute Episode Number [SubGroup] [Hash]
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{3}(\.\d{1,2})(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle33 { get; }

    // Anime - Title Absolute Episode Number (Year) [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2}(?!\d+))[-_. ](\(\d{4}\))[-_. ]\[(?<subgroup>.+?)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle34 { get; }

    // Anime - Title with trailing number, Absolute Episode Number and hash
    [GeneratedRegex(@"^(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle35 { get; }

    // Anime - Title Absolute Episode Number [Hash]
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?[-_. ]+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle36 { get; }

    // Episodes with airdate AND season/episode number, capture season/episode only
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airdate>\d{4}\W+[0-1][0-9]\W+[0-3][0-9])(?!\W+[0-3][0-9])[-_. ](?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle37 { get; }

    // Episodes with airdate AND season/episode number
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle38 { get; }

    // Episodes with absolute episode number AND airdate (TJET Wrestling)
    [GeneratedRegex(@"^(?<title>.+?)?[-_. ](?:e\d{2,3}(?!\d+))[-_. ](?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:\[[a-z]+\])", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle39 { get; }

    // Single or multi episode releases with multiple titles, then season and episode numbers after the last title. (Title1 / Title2 / ... / S1E1-2 of 6)
    [GeneratedRegex(@"^((?<title>.*?)[ ._]\/[ ._])+\(?S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?(?:[ ._]of[ ._](?<episodecount>\d{1,2}))?\)?[ ._][\(\[]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle40 { get; }

    // Multi-episode with title (S01E99-100, S01E05-06)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{2,3}(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+)))+(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle41 { get; }

    // Multi-episode with title (S01E05-06, S01E05-6)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{1,2}(?!\d+))(?:-(?<episode>\d{1,2}(?!\d+)))+(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle42 { get; }

    // Episodes with a Chinese title and English title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>[\u4E00-\u9FCC]+)[_. ](?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle43 { get; }

    // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle44 { get; }

    // Episodes with a title, 4 digit season number, Single episodes (S2016E05, etc) & Multi-episode (S2016E05E06, S2016E05-06, S2016E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:e|\We|_){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|e|\We|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle45 { get; }

    // Episodes with a title, 4 digit season number, Single episodes (2016x05, etc) & Multi-episode (2016x05x06, 2016x05-06, 2016x05 x06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:x|\Wx){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|x|\Wx|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle46 { get; }

    // Episodes with a title, Single episodes (s01.05)
    [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{2})(?!\d+))(?:\.)(?<episode>\d{2,3}(?!\d+))(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle47 { get; }

    // Multi-season pack
    [GeneratedRegex(@"^(?<title>.+?)(Complete Series)?[-_. ]+(?:S|(?:Season|Saison|Series|Stagione)[_. ])(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:[-_. ]{1}|[-_. ]{3})(?:S|(?:Season|Saison|Series|Stagione)[_. ])?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle48 { get; }

    // Partial season pack
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle49 { get; }

    // Anime - 4 digit absolute episode number in batch (1017-1088) or 1017-1088
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\(?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))[-](?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))\)?", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle50 { get; }

    // Anime - 4 digit absolute episode number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle51 { get; }

    // Mini-Series, treated as season 1, multi episodes are labelled as E1-E2
    [GeneratedRegex(@"^(?<title>.+?)(?:[-._ ][e])(?<episode>\d{2,3}(?!\d+))(?:(?:\-?[e])(?<episode>\d{2,3}(?!\d+)))+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle52 { get; }

    // 5 digit episode number with a title
    [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle53 { get; }

    // 5 digit multi-episode with a title
    [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:[-_. ]{1,3}ep){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle54 { get; }

    // Episodes with airdate and part (2018.04.28.Part.2)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ]+Part[-_. ]?(?<part>[1-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle55 { get; }

    // Episodes with airdate (2018.04.28)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle56 { get; }

    // Anime - Title 4-digit Absolute Episode Number [SubGroup]
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{4}(?!\d+))[-_. ]\[(?<subgroup>.+?)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle57 { get; }

    // Mini-Series with year in title, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
    [GeneratedRegex(@"^(?<title>.+?\d{4})(?:\W+(?:(?:Part\W?|e)(?<episode>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle58 { get; }

    // 3-digit season number
    // Single episode: Title.S001E01
    // Multi episode: Title.S001E01-E02
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)\d{3}(?!\d+))E(?<episode>(?<!\d+)\d{2}(?!\d+))(?:-[ex](?<episode>(?<!\d+)\d{2}(?!\d+)))?([_. ]|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle59 { get; }

    // Mini-Series, treated as season 1, episodes are labelled as XofY
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?<episode>(?<!\d+)\d{1,2}(?!\d+))of\d+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle60 { get; }

    // Supports Season 01 Episode 03
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:[-_\W](?<![()\[]))+(?:\W?Season\W?)(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)+(?:Episode\W)(?:[-_. ]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle61 { get; }

    // Multi-episode with episodes in square brackets (Series Title [S01E11E12] or Series Title [S01E11-12])
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\[S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+\]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle62 { get; }

    // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S01E11-12) or Series Title (S01E1-3 of 12))
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle63 { get; }

    // Multi-episode release with no space between series title and season (S01E11E12)
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)S(?<season>(?<!\d+)\d{2}(?!\d+))(?:E(?<episode>(?<!\d+)\d{2}(?!\d+)))+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle64 { get; }

    // Multi-episode with single episode numbers (S6.E1-E2, S6.E1E2, S6E1E2, etc)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:[-_. ]?[ex]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle65 { get; }

    // Single or multi episode releases with multiple titles, each followed by season and episode numbers in brackets
    [GeneratedRegex(@"^(?<title>.*?)[ ._]\(S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?\)(?:[ ._]\/[ ._])(?<title>.*?)[ ._]\(", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle66 { get; }

    // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S1E11-12) or Series Title (S1E1-3 of 12))
    [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{1}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle67 { get; }

    // Single episode season or episode S1E1 or S1-E1 or S1.Ep1 or S01.Ep.01
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?Ep?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle68 { get; }

    // 3 digit season S010E05
    [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{3}(?!\d+))(?:\W|_)?E(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle69 { get; }

    // Separated season and episode numbers S01 - E01
    [GeneratedRegex(@"^(?<title>.+?)(?:_|-|\s|\.)+S(?<season>\d{2}(?!\d+))(\W-\W)E(?<episode>(?<!\d+)\d{2}(?!\d+))(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle70 { get; }

    // Season and episode numbers in square brackets (single and multi-episode)
    // Series Title - [02x01] - Episode 1
    // Series Title - [02x01x02] - Episode 1
    [GeneratedRegex(@"^(?<title>.+?)?(?:[-_\W](?<![()\[!]))+\[(?:s)?(?<season>(?<!\d+)\d{1,2})(?:(?:[ex])(?<episode>\d{2}))(?:(?:[-ex]){1,2}(?<episode>\d{2}))*\].+?(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle71 { get; }

    // Anime - Title with season number - Absolute Episode Number (Title S01 - EP14)
    [GeneratedRegex(@"^(?<title>.+?S\d{1,2})[-_. ]{3,}(?:EP)?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle72 { get; }

    // Anime - French titles with single episode numbers, with or without leading sub group ([RlsGroup] Title - Episode 1)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+?(?:Episode[-_. ]+?)(?<absoluteepisode>\d{1}(\.\d{1,2})?(?!\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle73 { get; }

    // Anime - Absolute episode number in square brackets
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\[(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle74 { get; }

    // Japanese variety shows with leading date
    [GeneratedRegex(@"^(?<airyear>\d{2})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ](?<title>.+?)[-_. ](?:Season[-_. ]?(?<season>\d{1,2})[-_. ])?(?:ep|#)(?<episode>\d{2,3})", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle75 { get; }

    // Season only releases followed by year
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?=[-_. ]\d{4}[-_. ]+))(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle76 { get; }

    // Season only releases
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle77 { get; }

    // 4 digit season only releases
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{4}(?![-_. ]?\d+))(\W+|_|$)(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle78 { get; }

    // Spanish tracker releases
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_. ]+?Temporada.+?|\[.+?\])\[Cap)(?:[-_. ]+(?<season>(?<!\d+)\d{1,2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])))+(?:\])", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle79 { get; }

    // Supports 103/113 naming
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[_.-](?<![()\[!]))+(?<season>(?<!\d+)[1-9])(?<episode>[1-9][0-9]|[0][1-9])(?![a-z]|\d+))+(?:[_.]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle80 { get; }

    // 4 digit episode number
    // Episodes without a title, Single (S01E05, 1x05) AND Multi (S01E04E05, 1x04x05, etc)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle81 { get; }

    // 4 digit episode number
    // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]|\d{1,2}-))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)\W?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle82 { get; }

    // Turkish tracker releases (01 BLM, 3. Blm, 04.Bolum, etc)
    [GeneratedRegex(@"^(?<title>.+?)[_. ](?<absoluteepisode>\d{1,4})(?:[_. ]+)(?:BLM|B[oö]l[uü]m)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle83 { get; }

    // Episodes with airdate (04.28.2018)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle84 { get; }

    // Episodes with airdate (28.04.2018)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle85 { get; }

    // Episodes with airdate (20180428)
    [GeneratedRegex(@"^(?<title>.+?)?\W*(?<!\d+)(?<airyear>\d{4})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?!\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle86 { get; }

    // Anime - Title Absolute Episode Number (E195 or E1206)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>(\d{3}|\d{4})(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle87 { get; }

    // Daily episodes that use short month format instead of number
    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<airday>[1-2]\d|3[01]|[1-9])(?:th|st|rd)[-_. ](?<shortairmonth>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[-_. ](?<airyear>19[4-9]\d|20\d\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle88 { get; }

    // Supports 1103/1113 naming
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_. ](?<![()\[!]))*(?<!\d{1,2}-)(?<season>(?<!\d+|\(|\[|e|x)\d{2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])(?!p|i|\d+|\)|\]|\W\d+|\W(?:e|ep|x)\d+)))+([-_. ]+|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle89 { get; }

    // Dutch/Flemish release titles
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Se\.(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?afl\.)(?<episode>\d{1,3}(?!\d+))(?:(?:[-]|[-_ ]en[-_ ])(?<episode>\d{1,3}(?!\d+)))*))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle90 { get; }

    // Episodes with single digit episode number (S01E1, S01E5E6, etc)
    [GeneratedRegex(@"^(?<title>.*?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]){1,2}(?<episode>\d{1}))+)+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle91 { get; }

    // iTunes Season 1\05 Title (Quality).ext
    [GeneratedRegex(@"^(?:Season(?:_|-|\s|\.)(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:_|-|\s|\.)(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle92 { get; }

    // iTunes 1-05 Title (Quality).ext
    [GeneratedRegex(@"^(?:(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+))))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle93 { get; }

    // Mini-Series, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?:(?<!\()Part\W?|(?<!\d+\W+)e)(?<episode>\d{1,2}(?!\d+|\))))+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle94 { get; }

    // Mini-Series, treated as season 1, episodes are labelled as Part One/Two/Three/...Nine, Part.One, Part_One
    [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:Part[-._ ](?<episode>One|Two|Three|Four|Five|Six|Seven|Eight|Nine)(?>[-._ ])))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle95 { get; }

    // Anime Range - Title Absolute Episode Number (ep01-12)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:_|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,3}(\.\d{1,2})?)-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle96 { get; }

    // Anime - Title Absolute Episode Number (e66)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,4}(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle97 { get; }

    // Anime - Title Episode Absolute Episode Number (Series Title Episode 01)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle98 { get; }

    // Anime Range - Title Absolute Episode Number (1 or 2 digit absolute episode numbers in a range, 1-10)
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[_. ]+(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+))-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle99 { get; }

    // Anime - Title Episode/Episodio Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+(?:Episode|Episodio)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle100 { get; }

    // Anime - Title [Absolute Episode Number] from AniLibriaTV
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]\[)(?:(?:-?)(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+(?:\][-_. ]).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle101 { get; }

    // Simple season releases in the format 'Series (year) - Temporada season'
    [GeneratedRegex(@"^(?<title>.+?)[_. ]-[_. ](?:Temporada)[-_. ]?(?<season>\d{1,2}(?![_. ]?\d+))(?:[_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle102 { get; }

    // Anime - Title Absolute Episode Number
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle103 { get; }

    // Anime - Title {Absolute Episode Number}
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle104 { get; }

    // Extant, terrible multi-episode naming (extant.10708.hdtv-lol.mp4)
    [GeneratedRegex(@"^(?<title>.+?)[-_. ](?<season>[0]?\d?)(?:(?<episode>\d{2}){2}(?!\d+))[-_. ]", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle105 { get; }

    // Episodes without a title, Single episode numbers (S1E1, 1x1)
    [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{1}(?!\d+))))", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle106 { get; }

    // Anime OVA special
    [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:[-_. ]+(?<special>special|ova|ovd|ncop|nced)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle107 { get; }

    // Episodes with a title, 3 digit season number, Single episodes (S001E05, 1x05, etc) & Multi-episode (S001E05E06, S001E05-06, S001E05 E06, etc)
    [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{3})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTitle108 { get; }

    // "Season 1" / "Specials" folders alone are not releases.
    [GeneratedRegex(@"^(Season[ ._-]*\d+|Specials)$")]
    private static partial Regex SeasonFolderNameRegex { get; }

    [GeneratedRegex(@"\[[^\[\]]+\]|\([^()]+\)|\{[^{}]+\}")]
    private static partial Regex AnimeSeasonReleaseMetadataRegex { get; }

    [GeneratedRegex(@"\b(special|ova|ovd|ncop|nced)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnimeSpecialMarkerRegex { get; }

    // Quality tags that would otherwise read as numbers (720p, x264, 5.1, 10bit).
    private static readonly RegexReplace SimpleTitleRegex = new(SimpleTitlePattern, string.Empty);

    [GeneratedRegex(@"(?:(480|540|576|720|1080|1440|2160)[ip]|[xh][\W_]?26[456]|DD\W?5\W1|[<>?*]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(?<![a-f0-9])(8|10)[ -]?(b(?![a-z0-9])|bit))\s*?", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleTitlePattern { get; }

    [GeneratedRegex(@"(?<=[_.-])(?<airdate>(?<!\d)(?<airyear>[1-9]\d{1})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9]))(?=[_.-])", RegexOptions.IgnoreCase)]
    private static partial Regex SixDigitAirDateRegex { get; }

    [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?[\(\[]?(?<year>\d{4})[\]\)]?", RegexOptions.IgnoreCase)]
    private static partial Regex YearInTitleRegex { get; }

    [GeneratedRegex(@"^(?:(?<title>.+?) \((?<title>.+?)\)|(?<title>.+?) \| (?<title>.+?)|(?<title>.+?) AKA (?<title>.+?)|(?<title>.+?) / (?<title>.+?))$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleComponentsRegex { get; }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex DuplicateSpacesRegex { get; }

    [GeneratedRegex(@"^(?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]*(?<season>(?<!\d+)\d{1,4}(?!\d+))(?:[_. ]+(?!\d+)|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex { get; }

    [GeneratedRegex(@"^[ex]?(?<episode>(?<!\d+)\d{1,3}(?!\d+))(?:[ex-](?<episode>(?<!\d+)\d{1,3}(?!\d+)))?(?:[_. ](?!\d+)(?<remaining>.+)|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleEpisodeNumberRegex { get; }

    // Season and episode number separated by a space (S02 01)
    private static readonly RegexReplace SeasonEpisodeSpaceRegex = new(SeasonEpisodeSpacePattern, "${separator}S${season}E${episode}");

    [GeneratedRegex(@"(?<separator>^|[-_. ])S(?<season>(?<!\d+)\d{1,2}(?!\d+)) (?<episode>(?<!\d+)\d{1,2}(?!\d+))(?!\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodeSpacePattern { get; }

    private static readonly string[] ShortMonths = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Parses the file name, falling back to its folder names ("Season 2/05 - Title.mkv", "Show S01E05/file.mkv").</summary>
    public static ParsedEpisodeInfo? ParsePath(string path)
    {
        var fileInfo = new FileInfo(path);
        var fileName = SeasonEpisodeSpaceRegex.Replace(fileInfo.Name);
        var directoryName = fileInfo.Directory?.Name;

        var result = ParseTitle(fileName);

        // "Season 02/03 - Title.mkv": episode number from the file, season from the folder.
        var episodeNumberMatch = SimpleEpisodeNumberRegex.Match(fileName);

        if (episodeNumberMatch.Success && directoryName != null && (result == null || result.IsMiniSeries || result.AbsoluteEpisodeNumbers.Length > 0))
        {
            var seasonMatch = SeasonFolderRegex.Match(directoryName);

            if (seasonMatch.Success)
            {
                var episodeCaptures = episodeNumberMatch.Groups["episode"].Captures;
                var first = ParseNumber(episodeCaptures[0].Value);
                var last = ParseNumber(episodeCaptures[^1].Value);
                var pathTitle = $"S{seasonMatch.Groups["season"].Value}E{first:00}";

                if (first != last)
                {
                    pathTitle += $"-E{last:00}";
                }

                if (episodeNumberMatch.Groups["remaining"].Success)
                {
                    pathTitle += $" {episodeNumberMatch.Groups["remaining"].Value}";
                }

                return ParseTitle(pathTitle);
            }
        }

        if (directoryName == null)
        {
            return result;
        }

        // "Show S01E01-E03/02.mkv": the folder names the episodes, the file picks one.
        if (result == null && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out var number))
        {
            result = ParseTitle(directoryName);

            if (result != null && result.AbsoluteEpisodeNumbers.Contains(number))
            {
                result.AbsoluteEpisodeNumbers = [number];
            }
            else if (result != null && result.EpisodeNumbers.Contains(number))
            {
                result.EpisodeNumbers = [number];
            }
            else
            {
                result = null;
            }
        }

        return result
               ?? ParseTitle(directoryName + " " + fileName)
               ?? ParseTitle(directoryName + fileInfo.Extension);
    }

    /// <summary>Parses a release / file name; null if it does not look like an episode.</summary>
    public static ParsedEpisodeInfo? ParseTitle(string title)
    {
        try
        {
            if (!ParserCommon.IsParsable(title) || SeasonFolderNameRegex.IsMatch(ParserCommon.RemoveFileExtension(title)))
            {
                return null;
            }

            title = ParserCommon.UnreverseTitle(title);

            var releaseTitle = ParserCommon.RemoveFileExtension(title).Replace("【", "[").Replace("】", "]");

            foreach (var replace in ParserCommon.PreSubstitutionRegex)
            {
                replace.TryReplace(ref releaseTitle);
            }

            var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);
            simpleTitle = ParserCommon.RemoveWebsites(simpleTitle);
            simpleTitle = ParserCommon.RemoveQualityBrackets(simpleTitle);

            // 6-digit air dates (YYMMDD) to the 8-digit form the daily regexes expect.
            var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);

            if (sixDigitAirDateMatch.Success)
            {
                var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                if (airMonth != "00" || airDay != "00")
                {
                    simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, $"20{sixDigitAirDateMatch.Groups["airyear"].Value}.{airMonth}.{airDay}");
                }
            }

            foreach (var regex in ReportTitleRegex)
            {
                var matches = regex.Matches(simpleTitle);

                if (matches.Count == 0)
                {
                    continue;
                }

                ParsedEpisodeInfo? result;

                try
                {
                    result = ParseMatchCollection(matches, releaseTitle);
                }
                catch (InvalidDateException)
                {
                    // The pattern matched a date that cannot be one; no other pattern is going to do better.
                    return null;
                }

                if (result == null)
                {
                    continue;
                }

                if (result.FullSeason && result.ReleaseTokens.Contains("Special", StringComparison.InvariantCultureIgnoreCase))
                {
                    result.FullSeason = false;
                    result.Special = true;
                }

                result.Languages = LanguageParser.ParseLanguages(result.ReleaseTokens);
                result.Quality = QualityParser.ParseQuality(title);
                result.ReleaseGroup = ParserCommon.GetSubGroup(matches[0]) ?? ReleaseGroupParser.ParseReleaseGroup(releaseTitle);
                result.ReleaseHash = ParserCommon.GetReleaseHash(matches[0]);

                return result;
            }
        }
        catch (Exception)
        {
            // Captures that do not convert (numbers, dates): file names are arbitrary input, never fail on one.
        }

        return null;
    }

    /// <summary>
    /// Anime season packs commonly named after the season's own title, without season or episode numbers
    /// ("[Group] Show Season Title [1080p]"). Only the title, quality, languages and group are known.
    /// </summary>
    public static ParsedEpisodeInfo? ParseSeasonTitle(string title)
    {
        var releaseTitle = ParserCommon.RemoveFileExtension(title);

        if (AnimeSpecialMarkerRegex.IsMatch(releaseTitle))
        {
            return null;
        }

        var seriesTitle = AnimeSeasonReleaseMetadataRegex.Replace(releaseTitle, " ");
        seriesTitle = DuplicateSpacesRegex.Replace(seriesTitle, " ").Trim();

        // If any brackets remain we were unable to extract the season title correctly.
        if (string.IsNullOrWhiteSpace(seriesTitle) || seriesTitle.IndexOfAny(['[', ']', '(', ')', '{', '}']) >= 0)
        {
            return null;
        }

        return new ParsedEpisodeInfo
        {
            ReleaseTitle = releaseTitle,
            SeriesTitle = seriesTitle,
            SeriesTitleInfo = new SeriesTitleInfo { Title = seriesTitle, TitleWithoutYear = seriesTitle },
            FullSeason = true,
            IsSeasonTitle = true,
            Languages = LanguageParser.ParseLanguages(releaseTitle),
            Quality = QualityParser.ParseQuality(title),
            ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
        };
    }

    private static ParsedEpisodeInfo? ParseMatchCollection(MatchCollection matchCollection, string releaseTitle)
    {
        var firstMatch = matchCollection[0];
        var seriesName = firstMatch.Groups["title"].Value.Replace('.', ' ').Replace('_', ' ');
        seriesName = ParserCommon.RequestInfoRegex.Replace(seriesName, "").Trim(' ');

        int.TryParse(firstMatch.Groups["airyear"].Value, out var airYear);

        var lastSeasonEpisodeStringIndex = EndIndex(firstMatch.Groups["title"]);

        var result = airYear < 1900
            ? ParseNumbering(matchCollection, releaseTitle, ref lastSeasonEpisodeStringIndex)
            : ParseAirDate(firstMatch, releaseTitle, airYear, ref lastSeasonEpisodeStringIndex);

        if (result == null)
        {
            return null;
        }

        result.ReleaseTokens = lastSeasonEpisodeStringIndex < releaseTitle.Length ? releaseTitle[lastSeasonEpisodeStringIndex..] : releaseTitle;
        result.SeriesTitle = seriesName;
        result.SeriesTitleInfo = GetSeriesTitleInfo(seriesName, firstMatch);

        return result;
    }

    private static ParsedEpisodeInfo? ParseNumbering(MatchCollection matchCollection, string releaseTitle, ref int lastIndex)
    {
        var firstMatch = matchCollection[0];
        var result = new ParsedEpisodeInfo { ReleaseTitle = releaseTitle, SeasonNumbers = [0] };

        foreach (Match match in matchCollection)
        {
            var episodeCaptures = match.Groups["episode"].Captures;
            var absoluteEpisodeCaptures = match.Groups["absoluteepisode"].Captures;

            // No episodes at all is fine: that is a full season release.
            if (episodeCaptures.Count > 0)
            {
                var first = ParseNumber(episodeCaptures[0].Value);
                var last = ParseNumber(episodeCaptures[^1].Value);

                if (first > last)
                {
                    return null;
                }

                result.EpisodeNumbers = Enumerable.Range(first, last - first + 1).ToArray();
                lastIndex = Math.Max(lastIndex, EndIndex(episodeCaptures[^1]));

                if (match.Groups["special"].Success)
                {
                    result.Special = true;
                }

                if (match.Groups["splitepisode"].Success)
                {
                    result.IsSplitEpisode = true;
                }
            }

            if (absoluteEpisodeCaptures.Count > 0)
            {
                var first = ParseDecimal(absoluteEpisodeCaptures[0].Value);
                var last = ParseDecimal(absoluteEpisodeCaptures[^1].Value);

                if (first > last)
                {
                    return null;
                }

                if (first % 1 != 0 || last % 1 != 0)
                {
                    // Decimal numbers (12.5) are specials between episodes; ranges of them are not allowed.
                    if (absoluteEpisodeCaptures.Count != 1)
                    {
                        return null;
                    }

                    result.SpecialAbsoluteEpisodeNumbers = [first];
                    result.Special = true;
                    lastIndex = Math.Max(lastIndex, EndIndex(absoluteEpisodeCaptures[0]));
                }
                else
                {
                    result.AbsoluteEpisodeNumbers = Enumerable.Range((int)first, (int)(last - first + 1)).ToArray();

                    if (match.Groups["special"].Success)
                    {
                        result.Special = true;
                    }

                    lastIndex = Math.Max(lastIndex, EndIndex(absoluteEpisodeCaptures[^1]));
                }
            }

            if (episodeCaptures.Count == 0 && absoluteEpisodeCaptures.Count == 0)
            {
                // "Extras" / "SUBPACK" season releases.
                if (!string.IsNullOrWhiteSpace(firstMatch.Groups["extras"].Value))
                {
                    result.IsSeasonExtra = true;
                }

                // Partial season packs have a seasonpart group, to tell them apart from a full season.
                var seasonPart = firstMatch.Groups["seasonpart"].Value;

                if (!string.IsNullOrWhiteSpace(seasonPart))
                {
                    result.SeasonPart = Convert.ToInt32(seasonPart);
                    result.IsPartialSeason = true;
                }
                else if (firstMatch.Groups["special"].Success)
                {
                    result.Special = true;
                }
                else
                {
                    result.FullSeason = true;
                }
            }

            // "S01E01-E10 of 10" is the full season.
            if (episodeCaptures.Count == 2 && firstMatch.Groups["episodecount"].Success && episodeCaptures[^1].Value == firstMatch.Groups["episodecount"].Value)
            {
                result.EpisodeNumbers = [];
                result.FullSeason = true;
            }
        }

        var seasons = new List<int>();

        foreach (Capture seasonCapture in firstMatch.Groups["season"].Captures)
        {
            if (int.TryParse(seasonCapture.Value, out var parsedSeason))
            {
                seasons.Add(parsedSeason);
                lastIndex = Math.Max(lastIndex, EndIndex(seasonCapture));
            }
        }

        seasons = seasons.Distinct().Order().ToList();

        if (seasons.Count == 2)
        {
            // S01-S03
            result.SeasonNumbers = Enumerable.Range(seasons[0], seasons[1] - seasons[0] + 1).ToList();
        }
        else if (seasons.Count > 0)
        {
            result.SeasonNumbers = seasons;
        }
        else if (result.AbsoluteEpisodeNumbers.Length == 0 && result.EpisodeNumbers.Length > 0)
        {
            // Episode numbers without a season and not absolute: a mini series, season 1.
            result.SeasonNumbers = [1];
            result.IsMiniSeries = true;
        }

        return result;
    }

    private static ParsedEpisodeInfo ParseAirDate(Match match, string releaseTitle, int airYear, ref int lastIndex)
    {
        int airMonth;
        int airDay;

        if (match.Groups["ambiguousairmonth"].Success && match.Groups["ambiguousairday"].Success)
        {
            airMonth = Convert.ToInt32(match.Groups["ambiguousairmonth"].Value);
            airDay = Convert.ToInt32(match.Groups["ambiguousairday"].Value);

            if (airDay <= 12 && airMonth <= 12)
            {
                throw new InvalidDateException(); // cannot tell month from day
            }
        }
        else if (match.Groups["shortairmonth"].Success)
        {
            airMonth = Array.IndexOf(ShortMonths, match.Groups["shortairmonth"].Value.ToLowerInvariant()) + 1;
            airDay = Convert.ToInt32(match.Groups["airday"].Value);

            if (airMonth == 0)
            {
                throw new InvalidDateException();
            }
        }
        else
        {
            airMonth = Convert.ToInt32(match.Groups["airmonth"].Value);
            airDay = Convert.ToInt32(match.Groups["airday"].Value);
        }

        // Swap day and month if month is bigger than 12 (scene fail)
        if (airMonth > 12)
        {
            (airDay, airMonth) = (airMonth, airDay);
        }

        DateTime airDate;

        try
        {
            airDate = new DateTime(airYear, airMonth, airDay);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDateException();
        }

        // Allow early releases, but reject dates too far in the future (most likely a parse error).
        // Before 1970 only with the year in the title (Plex DVR format).
        if (airDate > DateTime.Now.AddDays(14).Date ||
            (airDate < new DateTime(1970, 1, 1) && string.IsNullOrWhiteSpace(match.Groups["titleyear"].Value)))
        {
            throw new InvalidDateException();
        }

        lastIndex = Math.Max(lastIndex, EndIndex(match.Groups["airyear"]));
        lastIndex = Math.Max(lastIndex, EndIndex(match.Groups["airmonth"]));
        lastIndex = Math.Max(lastIndex, EndIndex(match.Groups["airday"]));

        return new ParsedEpisodeInfo
        {
            ReleaseTitle = releaseTitle,
            SeasonNumbers = [0],
            AirDate = airDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DailyPart = match.Groups["part"].Success ? Convert.ToInt32(match.Groups["part"].Value) : null,
        };
    }

    private static SeriesTitleInfo GetSeriesTitleInfo(string title, Match match)
    {
        var info = new SeriesTitleInfo { Title = title, TitleWithoutYear = title };
        var yearMatch = YearInTitleRegex.Match(title);

        if (yearMatch.Success)
        {
            info.TitleWithoutYear = yearMatch.Groups["title"].Value;
            info.Year = Convert.ToInt32(yearMatch.Groups["year"].Value);
        }

        var components = TitleComponentsRegex.Match(info.TitleWithoutYear);

        if (components.Success)
        {
            info.AllTitles = components.Groups["title"].Captures.Select(c => c.Value).ToArray();
        }
        else if (match.Groups["title"].Captures.Count > 1)
        {
            info.AllTitles = match.Groups["title"].Captures.Select(c => c.Value.Replace('.', ' ').Replace('_', ' ')).ToArray();
        }

        return info;
    }

    private static int EndIndex(Capture capture) => capture.Index + capture.Length;

    // Digits of any script ("０５", "٠٥") or English number words ("Part.Two").
    private static int ParseNumber(string value)
    {
        if (int.TryParse(ToAsciiDigits(value), out var number))
        {
            return number;
        }

        number = Array.IndexOf(ParserCommon.NumberWords, value.ToLowerInvariant());

        return number != -1 ? number : throw new FormatException($"{value} isn't a number");
    }

    private static decimal ParseDecimal(string value) =>
        decimal.TryParse(ToAsciiDigits(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException($"{value} isn't a number");

    private static string ToAsciiDigits(string input)
    {
        var result = new StringBuilder(input.Length);

        foreach (var c in input.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsNumber(c))
            {
                result.Append(char.GetNumericValue(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private sealed class InvalidDateException : Exception;
}
