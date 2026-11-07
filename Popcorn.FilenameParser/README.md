# Popcorn.FilenameParser

Scene / release file name parsing (title, year, season/episode numbers, air date, quality, languages, edition,
release group, subtitle file languages, ...). Plain C#, no dependencies.

Derived from the parsers of [Sonarr](https://github.com/Sonarr/Sonarr) (episodes) and [Radarr](https://github.com/Radarr/Radarr)
(movies), both **GPL-3.0**; this project is a derived work under the same license. Merged from Radarr
`35538cb5ed0e26f6b14342bfee404803fb87177a` (2026-11-02) and Sonarr `9a41dfc129d5bbf8f3950f871623b46eea4f914e` (2026-10-31).

## API

| Type | What |
|---|---|
| `MediaNameParser.Parse(path)` | Entry point used by Popcorn (`MediaFile.ParseTags()`): runs both title parsers and picks one → `ParsedMediaName` |
| `EpisodeParser.ParseTitle / ParsePath / ParseSeasonTitle` | Sonarr's episode parser → `ParsedEpisodeInfo` (null if the name is not an episode) |
| `MovieParser.ParseTitle / ParsePath` | Radarr's movie parser → `ParsedMovieInfo` (needs a year) |
| `QualityParser.ParseQuality` | Source / resolution / modifier (Remux, BR-DISK, ...) + revision (PROPER, REPACK, v2) → `ParsedQuality` |
| `LanguageParser` | Audio languages of a release; language, tags (forced, sdh) and title of subtitle files |
| `ReleaseGroupParser.ParseReleaseGroup` | "-GROUP", "[SubGroup]" |
| `IsoLanguages.Find` | "en", "eng", "pt-BR" → `Language` |

Each title parser assumes it is given its own kind of name, so each accepts some names of the other kind; that is
what `MediaNameParser` sorts out.

## What was merged / dropped

Both upstreams carried their own copies of the quality, language, release group and clean-up code. Here there is one
of each: Radarr's quality model (`Source` + `Resolution` + `Modifier`, superset of Sonarr's) with Sonarr's newer regex
additions, the union of both language lists and tag regexes (table-driven), the union of both release group exception
lists, Sonarr's hashed-release and reversed-title handling for both title parsers.

Where the two disagreed the tests decided: exact release group names win over bracketed ones (Radarr), a DVD-R tag
wins over NTSC/PAL, HD-DVD + VC-1 is BR-DISK (Radarr; the one Sonarr case was moved to `should_parse_brdisk_quality`).

Dropped as indexer/database machinery: quality ids/profiles/weights, guessing the quality from the file extension
(`.mkv` = 720p), title normalisation for database matching (`CleanSeriesTitle`, `ToUrlSlug`, roman numerals,
episode-title normalisation), release type, the logger, `NzbDrone.Common` shims.

## Tests

`../Popcorn.FilenameParser.Tests`: both upstreams' parser fixtures (`Movies/` = Radarr, `Tv/` = Sonarr) run against
the merged code, plus `MediaNameParserFixture`. Fixtures for the dropped helpers were removed; extension-guess cases now
assert that nothing is guessed. FluentAssertions is pinned to 7.x (Apache-2.0; 8.x is commercial).

To pick up upstream fixes: diff the upstream `src/NzbDrone.Core/Parser` (+ `Qualities`, `Languages`) between the
commits above and the new ones, apply the parser changes by hand, copy new test cases, bump the commits here and run
`dotnet test Popcorn.FilenameParser.Tests`.
