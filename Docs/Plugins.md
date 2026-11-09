# Plugin API

Popcorn's **Search** page and its details pages are fed entirely by plugins. A plugin is a .NET assembly; it can also
ship an ES module with its own React components. The app renders whatever the plugin returns. When the plugin has no
UI module, built-in components render it.

There are three kinds of plugins (one plugin may be several):

* **Media sources** (`IMediaSourcePlugin`) say *what exists*: search + details pages. Search page **Media** tab.
  Reference: [`Plugins/Popcorn.Plugin.Tmdb`](../Plugins/Popcorn.Plugin.Tmdb) (TMDb, custom details page with a cast row).
* **File sources** (`IFileSourcePlugin`) say *where to get it*: torrent sites, web pages, FTP servers, archives,
  anything. They only return links; Popcorn decides what to do with a link by its kind: `Magnet`/`Torrent`
  links are torrent downloads, `Direct` links are direct downloads, `Page` links open in the browser (see
  [Links](#links) below). Search page **Files** tab, and the **Files**
  section under every media details page, searched with that item's title / year / IMDb id. One plugin can provide
  several file sources (`IFileSourceProvider`, e.g. one plugin for a few torrent sites sharing code); each is shown
  and searched like a plugin of its own, with its own name and icon.
* **Image providers** (`IImageProvider`) supply pictures for library items: posters and/or backdrops (a plugin
  declares which in `ImageKinds`). Used by the **Add to Library** dialog (Folders page; the first result of the first
  provider in the plugin order is preselected) and **Edit pictures** on an item's page. The user picks among the
  results of all enabled providers or uploads an own file. TMDb provides both kinds.

Results are listed in the plugin order set in **Settings → Plugins** (drag to reorder). The Media tab groups results
per plugin (icon, name, count); a group can be collapsed (remembered) and shows the first 10 results with a Show all /
Show less toggle. File sources are only searched when they are needed: on the search page once the **Files** tab is
open, on a details page when the user clicks *Search*. File results are shown as one combined list (default, each
result marked with its plugin's icon) or grouped per plugin (switch *Combine providers*), sorted by a column the
plugins expose (`SortFields`; default: the first column of the first plugin that has any, e.g. seeders), and every
plugin can be searched again with its refresh button. The view choice and sort are remembered.

API version: **1** (`PluginApi.Version`).

## How it fits together

```
plugins/<id>/plugin.json ──► PluginManager (Popcorn.Server/PluginHost) ── loads ──► IMediaSourcePlugin and/or
                                     │                                                 IFileSourcePlugin (your .dll)
   WebSocket /ws: PluginList, PluginSearch, PluginDetails, PluginFileSearch, OpenLink  (PluginWebService)
                                     │
Popcorn.Web: Search page ─► PluginSearchCard / PluginDetails / PluginDetailsCard / PluginFileCard slots
                                     │   (src/plugins/plugin-components.tsx)
           your web/index.js exports SearchCard / Details / DetailsCard / FileCard?  ── yes ─► your component
                                                                                     └─ no ──► built-in component
```

## Plugin folder

```
<id>/
  plugin.json
  <Assembly>.dll            + its dependencies and <Assembly>.deps.json (the build output)
  web/                      optional, served at /plugins/<id>/
    index.js                UI module (plugin.json "ui")
    icon.svg                icon (plugin.json "icon")
```

`plugin.json`:

```json
{
  "id": "tmdb",
  "name": "TMDb",
  "version": "1.0.0",
  "apiVersion": 1,
  "assembly": "Popcorn.Plugin.Tmdb.dll",
  "ui": "index.js",
  "icon": "icon.svg"
}
```

| Field | |
|---|---|
| `id` | Unique. Letters, digits, `-`, `_`, `.`. Used in URLs (`/plugins/<id>/`, `/media-details/<id>/...`). |
| `name` | Shown in the UI (result group headings, settings). Defaults to `id`. |
| `version` | Informational. |
| `apiVersion` | Must equal the server's `PluginApi.Version`, otherwise the plugin is not loaded. |
| `assembly` | Main assembly, relative to the plugin folder. |
| `ui` | Optional. UI module path, relative to `web/`. If the file is missing the plugin still loads and uses the built-in UI. |
| `icon` | Optional. Image (png/svg/...) relative to `web/`, shown next to the name (square works best). Without one the first letter of the name is shown. |
| `settings` | Optional. Fields of the plugin's settings form, see [Settings](#settings). |

### Settings

A plugin that needs configuration (login details, an API key, a host) declares its fields in `plugin.json`. Popcorn
then shows a gear button in **Settings → Plugins** with a form generated from them; no UI code is needed.

```json
"settings": [
  { "key": "username", "label": "Username", "group": "Account", "required": true },
  { "key": "password", "label": "Password", "type": "password", "group": "Account", "required": true },
  { "key": "maxResults", "label": "Max results", "type": "number", "default": 50 },
  { "key": "minRatio", "label": "Minimum ratio", "type": "float", "default": 0.5 },
  { "key": "mirrors", "label": "Mirrors", "list": true, "group": "Connection", "placeholder": "https://..." },
  { "key": "ports", "label": "Ports", "type": "number", "list": true, "group": "Connection", "default": [80, 443] },
  { "key": "freeleechOnly", "label": "Freeleech only", "type": "boolean", "description": "Hide other torrents" },
  { "key": "category", "label": "Category", "type": "select",
    "options": [{ "value": "all", "label": "All" }, { "value": "hd", "label": "HD" }] }
]
```

| Field property | |
|---|---|
| `key` | Required, unique. Key in `settings.json`. |
| `label` | Defaults to `key`. |
| `type` | `text` (default), `password`, `number` (whole number), `float` (any number), `boolean` or `select`. |
| `list` | `true`: the value is a list of `type` values (text, password, number, float), edited with add/remove buttons. Blank entries are dropped; an empty list is no value. |
| `group` | Optional heading. Fields with the same group are shown together in a box under it, groups in order of first use; fields without one are shown without heading. |
| `description`, `placeholder` | Optional texts of the input. |
| `default` | Used while there is no saved value. |
| `required` | Saving is refused without a value (or default); for a list, at least one entry. |
| `options` | For `select`: `{ value, label? }`; values are strings. |

An invalid `settings` list makes the `plugin.json` invalid: the plugin stays in the list with the error.

Values are saved as a flat JSON object in **`settings.json` in the plugin folder**, readable by the owner only.
It survives updates through **Install plugin (.zip)** and is deleted on uninstall. A built-in plugin's folder is
replaced on every app install, so its file is in `<data>/plugins/<id>/settings.json` instead. Saving only touches
declared fields, so a plugin may keep other keys in the file. A value equal to the default is not stored.

**Password values are never sent back to the browser.** The form leaves them empty; an empty input keeps the saved one.
A password list is replaced as a whole: entering entries replaces the saved list, leaving it empty keeps it.

Read them through `IPluginSettings`, which also applies the defaults, by asking for it in the plugin class constructor:

```csharp
public sealed class MySite(IPluginSettings settings, ILogger<MySite> logger) : IFileSourcePlugin
{
    private string? Username => settings.GetString("username");   // also GetNumber, GetBool, GetStringList, GetNumberList
}
```

Saving settings **reloads the plugin** (`Dispose`, then a new instance), so sessions and anything else derived from the
old values start over and values can be read once in the constructor. Settings can be edited while the plugin is
disabled or failed to load, and saving retries the load, so a constructor may throw on a bad value and the user can
still fix it. Throw from a search with a message pointing to Settings → Plugins when something required is missing.

### Actions

Buttons in the plugin's settings dialog ("Test connection", "Log in", "Clear cache"...). Implement `IPluginActions` on
the plugin class, next to its other interfaces:

```csharp
public sealed class MySite(IPluginSettings settings) : IFileSourcePlugin, IPluginActions
{
    public IReadOnlyList<PluginAction> Actions =>
    [
        new("test", "Test connection") { Group = "Account", Description = "Logs in with the saved credentials" },
        new("clear-cache", "Clear cache") { Color = "red", Confirm = "Forget all cached results?" },
    ];

    public async Task<PluginActionResult> RunActionAsync(string actionId, CancellationToken cancellationToken)
    {
        switch (actionId)
        {
            case "test":
                await LoginAsync(cancellationToken);              // throw on failure: the message is shown
                return PluginActionResult.Ok("Logged in");
            case "clear-cache":
                var count = ClearCache();
                return count == 0 ? new("Nothing to clear", Warning: true) : PluginActionResult.Ok($"{count} entries removed");
            default:
                throw new ArgumentException($"Unknown action {actionId}");
        }
    }
}
```

* A plugin with actions gets the settings dialog (gear in Settings → Plugins) even without `settings` fields. Actions
  only show while the plugin is loaded.
* `Group` puts the button under the settings group of that name (plugin.json `group`, or a new heading), null under
  the fields without a group. `Description` is shown next to it, `Color` is a Mantine color, `Confirm` asks the user
  that question first, `Disabled` greys it out.
* Actions run on the loaded instance, so they see the **saved** settings: the buttons are disabled while the form has
  unsaved changes. Saving keeps the dialog open when the plugin has actions (the reloaded plugin's actions and values
  are shown).
* The result's `Message` is shown in a notification (green, or yellow with `Warning`); an exception is shown as an
  error. Actions run off the socket's message loop and are cancelled after 2 minutes.
* `Actions` is read when the dialog opens and after every run, so it can change (e.g. "Log out" once logged in).
  Ids must be unique, or the plugin shows no actions (logged).

### Shared variables

Plugins can share values through the app, which acts as the mediator: no plugin references another one. A plugin
publishes a string under a name and any plugin that knows the name reads it. Ask for `IPluginVariables` in the
constructor:

```csharp
// publisher, e.g. a proxy plugin: the value comes from its own settings
public sealed class ProxyPlugin(IPluginSettings settings, IPluginVariables variables) : IFileSourcePlugin
{
    // in the constructor body (or wherever the value becomes known)
    // variables.Set("proxy.url", settings.GetString("url"));   // null removes it
}

// consumer: read it when it is used, not once in the constructor
public sealed class MySite(IPluginVariables variables) : IFileSourcePlugin
{
    private string? Proxy => variables.Get("proxy.url");
}
```

* Values live in memory only. Each one belongs to the plugin that set it last and is **removed when that plugin is
  unloaded** (disabled, uninstalled, updated, reloaded after its settings were saved), so publish values again in the
  constructor; a reloaded plugin does that by itself.
* Plugins load in no particular order and others may come and go at runtime: read values when you need them (a
  dictionary lookup) and handle `null` (nobody publishes it). `GetAll()` returns every current value.
* Names are case-sensitive and global: prefix them with a topic (`proxy.url`). Setting a name
  another plugin set replaces its value (logged).
* Values are strings so that no types cross plugin load contexts; use JSON for anything structured.

### Status messages

Tell the user what is wrong (mainly "not set up yet") without waiting for a search to fail. Ask for `IPluginStatus` in
the constructor; messages show on the plugin's row in Settings → Plugins (an `Error` adds a "Needs attention" badge)
and at the top of its settings dialog:

```csharp
public sealed class MySite : IFileSourcePlugin
{
    private readonly IPluginStatus _status;

    public MySite(IPluginSettings settings, IPluginStatus status)
    {
        _status = status;
        if (string.IsNullOrWhiteSpace(settings.GetString("apiKey")))
            status.Set(PluginStatusLevel.Error, "Set the API key in the plugin settings", "setup");
    }

    public async Task<IReadOnlyList<FileResult>> SearchFilesAsync(FileQuery query, CancellationToken ct)
    {
        try
        {
            var results = await SearchSiteAsync(query, ct);
            _status.Clear("connection");
            return results;
        }
        catch (HttpRequestException ex)
        {
            _status.Set(PluginStatusLevel.Warning, $"Site unreachable: {ex.Message}", "connection");
            throw;
        }
    }
}
```

* Levels `Info`, `Warning`, `Error`. Messages are kept by key (default `""`), in the order the keys were first set;
  `Set` replaces the message of its key, `Clear` removes it.
* They can change at any time (constructor, searches, actions, timers): open pages update right away.
* They belong to the loaded instance, so unloading the plugin (also the reload after saving settings) clears them; the
  new instance's constructor checks its settings again.

### Where plugins are loaded from

The server scans these at startup and again whenever plugins are managed from **Settings → Plugins**:

* **Order** (drag handle): stored in `<data>/plugins/plugins.json` (`order`); plugins not listed follow by name.
* **Load/unload** (switch): disabled ids are stored in `<data>/plugins/plugins.json` and survive restarts.
  Unloading disposes the plugin instance (if it is `IDisposable`) and unloads its load context.
* **Install plugin (.zip)**: uploads to `POST /api/plugins` (multipart field `file`, max 512 MB). The zip must have
  `plugin.json` at its root or inside a single top-level folder. It is extracted to `<data>/plugins/<id>/`,
  replacing an installed plugin with the same id, and loaded right away. Zip entries that point outside the folder
  are rejected. Errors come back as 400 with the message.
* **Uninstall** (installed plugins only): deletes `<data>/plugins/<id>/`. If a built-in plugin has the same id, it
  takes over again. Built-in plugins can only be disabled.

Copying a folder in by hand works too, but it is only picked up at the next scan (any action above, or a restart).

1. **Data folder:** `~/.local/share/Popcorn/plugins/<id>/` (Windows: `%LOCALAPPDATA%\Popcorn\plugins\<id>\`).
   Separately distributed plugins (e.g. torrent scrapers) go here. The server creates the folder at startup.
   Plugins here take precedence, so a newer copy of a bundled plugin with the same `id` overrides it.
2. **Program folder:** `<server dir>/plugins/<id>/`. Plugins built into the app go here: `Plugins/*` in this repo,
   currently only TMDb. They are replaced on every install/update of Popcorn, so don't put anything there by hand.

Folders without `plugin.json` (and folders starting with `.`) are ignored, so plugin sources can live next to the plugins. A plugin with the same `id` as one already loaded is skipped. A plugin that fails to load stays in the list with its
error (shown in Settings, details in `logs/*-Popcorn.Server.txt` and Settings → Logs). Everything else keeps working.

## Server side (.NET)

### Project setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- copies dependencies to the output + writes .deps.json, needed by the plugin loader -->
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- The contract: compile against it, never ship it -->
    <ProjectReference Include="path/to/Popcorn.Plugins.Abstractions.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <!-- Optional: logging from the host -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" ExcludeAssets="runtime" />
    <!-- Your own dependencies, any version -->
    <PackageReference Include="TMDbLib" Version="3.0.0" />
  </ItemGroup>
</Project>
```

The build output folder plus `plugin.json` (and `web/`, if any) is the plugin. Out of tree, `Popcorn.Plugins.Abstractions`
can be referenced as a built `Popcorn.Plugins.Abstractions.dll` (`<Reference>` with `<Private>false</Private>`).

### Contract (`Popcorn.Plugins.Abstractions`)

```csharp
public interface IMediaSourcePlugin
{
    Task<IReadOnlyList<MediaCard>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<MediaDetails?> GetDetailsAsync(string id, CancellationToken cancellationToken);
}

public interface IFileSourcePlugin
{
    Task<IReadOnlyList<FileResult>> SearchFilesAsync(FileQuery query, CancellationToken cancellationToken);
    IReadOnlyList<FileSortField> SortFields => [];   // optional
}

// Several file sources in one plugin, instead of IFileSourcePlugin
public interface IFileSourceProvider
{
    IReadOnlyList<FileSource> FileSources { get; }
}
public record FileSource(string Id, string Name, IFileSourcePlugin Source) { public string? Icon { get; init; } }

public interface IImageProvider
{
    IReadOnlyList<ImageKind> ImageKinds { get; }   // Poster, Backdrop or both
    Task<IReadOnlyList<ImageResult>> SearchImagesAsync(ImageQuery query, ImageKind kind, CancellationToken cancellationToken);
}
```

* The assembly must contain **exactly one** public, non-abstract class implementing `IMediaSourcePlugin`,
  `IFileSourcePlugin`, `IFileSourceProvider`, `IImageProvider` or `IServicePlugin` (a class can implement several, but
  not both file kinds). `IServicePlugin` = no search role: a plugin that only serves others or the user through
  [shared variables](#shared-variables), [status messages](#status-messages) and [actions](#actions), e.g. one that
  runs a helper program and publishes its address. Implement `IDisposable` to stop what it started: plugins are
  disposed when unloaded and when the server stops.
* It is created **once** (singleton) with `ActivatorUtilities`, so its constructor can take host services. The ones
  safe to depend on are `ILogger<T>` / `ILoggerFactory`, `IPluginSettings` ([Settings](#settings)) and
  `IPluginVariables` ([Shared variables](#shared-variables)) and `IPluginStatus` ([Status messages](#status-messages)). The class may also implement `IPluginActions`
  ([Actions](#actions)): buttons in its settings dialog. Calls can run
  concurrently.
* `SearchAsync`: return `[]` for no results. Throw on failure. The exception message is shown on the search page.
* `GetDetailsAsync`: `id` is a `MediaCard.Id` your plugin returned. Return `null` for "not found".
* `SearchFilesAsync`: same rules as `SearchAsync`. Use the most precise part of the query your source supports
  (e.g. `ImdbId` when set, else `Text`), and return `[]` for queries you can't serve (e.g. `Kind == Show` on a
  movies-only site).
* `SortFields` (optional): numeric columns the results can be sorted by; the first one is the default sort. Each
  `FileResult` carries the values in `SortValues` (key → number). Use the presets `FileSortField.Seeders`,
  `.Leechers`, `.Size` (bytes) and `.Added` (Unix seconds) or the same keys where they fit: the combined list merges
  columns of all plugins by key, so results of different plugins sort together. Results without a value sort last.
* `Origin` / `OriginId` (optional): the provider a result really comes from (a tag shared by all plugins, lowercase
  name or domain, e.g. `"1337x"`) and its id there (e.g. the site's torrent id or the info hash; null = `Id`). An
  aggregator sets them per result, a plugin for one site sets its own tag. Results of different plugins with the same
  pair are one item: the combined list shows it once when the user turns on Remove duplicates (the copy with the most
  seeders, ties: the first provider in the user's order). Without `Origin` a result is never deduplicated.
* `FileSources` (`IFileSourceProvider`): the sources in display order. `Id` must be unique within the plugin and
  stable (letters, digits, `-`, `_`, `.`), since the UI remembers collapsed groups by it. `Name` replaces the plugin
  name in the UI, `Icon` (path in `web/`, like plugin.json `icon`) the plugin icon. Each `Source` is a plain
  `IFileSourcePlugin` object you create (DI only creates the plugin class, so pass it what it needs) and has its own
  `SortFields`. The list is read every time the app lists plugins, so it may change at runtime (e.g. sources the
  user configured); a source that disappears is just not searched anymore. Enabling, order and uninstalling stay
  per plugin. Dispose the sources from the plugin's `Dispose` if they need it.

  ```csharp
  public sealed class TorrentSites(ILoggerFactory logs) : IFileSourceProvider
  {
      public IReadOnlyList<FileSource> FileSources { get; } =
      [
          new("piratebay", "The Pirate Bay", new PirateBaySource(logs)) { Icon = "piratebay.svg" },
          new("yts", "YTS", new YtsSource(logs)) { Icon = "yts.png" },
      ];
  }
  ```
* `SearchImagesAsync`: only called with a `kind` listed in `ImageKinds`. Best match first; return `[]` for no
  results, throw on failure (shown under the provider's results). `ImageResult.Url` must be an absolute http(s) URL
  of the full-size image (the server downloads and stores the picked one: jpeg/png/webp/gif, at most 20 MB);
  `ThumbnailUrl` is shown in the picker. All image providers of a kind are queried in parallel. Identify the title
  in this order: `ImageQuery.MediaId` (your own `MediaDetails.Id`, set only when the item was added from your
  plugin's media page), `ImdbId` (from the media page the item was added from, or written in its name), and only
  then `Title`/`Year`/`Kind`. A name search can be ambiguous (a show and its remake share the name), so return images
  of several matches with `ImageResult.Group` set to a heading per title (e.g. `"Dune (2021) · Movie"`), the best
  match's group first: the picker shows each group under its heading.
* Each call gets a 30 s timeout through `cancellationToken`.

Models (serialized to the UI as camelCase JSON):

| `MediaCard` | used for search results and small cards on details pages |
|---|---|
| `Id` (required) | Your id. Any string, e.g. `movie:603`. It ends up URL-encoded in the details route. |
| `Title` (required) | |
| `Subtitle` | Short secondary line (year, "10 episodes"). |
| `Description` | Long text (search results show up to 8 lines). |
| `ImageUrl` | Absolute URL of a portrait image (2:3). |
| `Badges` | `MediaBadge(Label, Color?)`. Color is any CSS color or a Mantine color name. |
| `HasDetails` | `false`: the card is not clickable (e.g. people, seasons). Default `true`. |
| `Data` | Any object, passed through to your UI components as-is. |

| `MediaDetails` | the details page |
|---|---|
| `Id`, `Title` (required) | |
| `Tagline`, `Overview` | `Overview` also becomes the description of new library items (best search match of the first media source plugin that has one) |
| `PosterUrl`, `BackdropUrl` | Absolute URLs. The backdrop is drawn behind the header. |
| `Badges` | as above |
| `Fields` | `MediaField(Label, Value)` rows (release date, runtime, director...). |
| `Sections` | `MediaCardSection(Title, Cards)`, horizontal rows of small cards (seasons, recommendations...). Clicking a card with `HasDetails` opens its details page. |
| `FileQuery` | What file sources should search for this item (see below). Null = `{ Text = Title }`. |
| `Data` | Any object, for your UI components. |

| `FileQuery` | input of a file search |
|---|---|
| `Text` (required) | Free text: what the user typed on the search page, or e.g. `The Matrix 1999` for a media item. |
| `Title`, `Year` | Set for media items (TMDb: year only for movies). |
| `ImdbId` | e.g. `tt0133093`, when the media source knows it. |
| `Kind` | `MediaKind.Movie` / `MediaKind.Show`, when known. |

On the search page only `Text` is set.

| `FileResult` | one file (or set of files, e.g. a torrent) |
|---|---|
| `Id` (required) | Unique within one result list. |
| `Title` (required) | e.g. the release name. |
| `Subtitle` | Short line: size, file count, date, uploader... |
| `Description` | Up to 2 lines. |
| `Badges` | quality, category, seeders... (as above) |
| `SortValues` | Key → number for the plugin's `SortFields`, e.g. `{ "seeders": 120, "size": 1.8e9 }`. |
| `Links` | `FileLink(Kind, Url, Label?)`, one button each. `Kind`: `Magnet` (magnet: URI), `Torrent` (.torrent URL), `Direct` (the file itself, http/https/ftp), `Page` (a web page about it). `Label` overrides the button text. `Resolve = true`: fetched on use, see [below](#resolve). |
| `Data` | Any object, for your UI components. |

| `ImageQuery` | input of an image search: `Title`, `Year`, `Kind`, `ImdbId`, `MediaId` |
|---|---|
| `Title` (required) | The item name (folder name or what the user typed), cleaned of release tags when it parses (`The.Matrix.1999.1080p` → `The Matrix`). |
| `Year` | When the name has one. |
| `Kind` | `MediaKind.Show` when the name looks like episodes/seasons, else null (unknown). |

| `ImageResult` | one picture (`Url`, `ThumbnailUrl`, `Title` tooltip, `Group` heading) |
|---|---|
| `Url` (required) | Full-size image, absolute http(s). |
| `ThumbnailUrl` | Smaller version for the picker; null = `Url`. |
| `Title` | What it is of, e.g. `The Matrix (1999)`; tooltip. |

<a id="links"></a>The link `Kind` decides the download action, so a file source chooses torrent vs. direct download simply
by the kind it returns. Each download link can go to one of two targets:

* **internal**: Popcorn's own downloader (Downloads page): `Magnet`/`Torrent` → built-in torrent client, `Direct` →
  built-in HTTP downloader. Downloads started from a media details page are added to the library under that item's
  title and poster, and can be played while they download. Only `magnet:`, `http(s):` and .torrent `data:` URLs.
* **external**: in a browser, `Magnet` goes to the OS (torrent program), a .torrent `data:` URL is saved as a
  .torrent file and the rest opens in a new tab; in the desktop app the server opens them with the system handler
  (`xdg-open`; a .torrent `data:` URL as a temporary .torrent file). Only `magnet:`, `http(s):`, `ftp:` and .torrent
  `data:` URLs.

The default target per kind is a user setting (Settings → Downloads); the built-in card has a menu for the other one.
`Page` links are always external. A link that needs a login (private trackers) only works if the browser (external)
is logged in too — the internal downloader sends no cookies. For .torrent files behind your plugin's own login,
return a `Torrent` link with `Resolve = true` and download the file in `ResolveLinkAsync` with your session: return
it as `data:application/x-bittorrent;base64,<file>` (both targets accept that).

<a id="resolve"></a>**Links fetched on use.** When the link is not in the search results (e.g. the magnet is only on
each torrent's own page), don't fetch every result's page during the search: return the link with `Resolve = true`,
its `Kind` = the kind it will be and `Url` = whatever you need to find it (the page URL), and implement
`IFileLinkResolver` on the file source. When the user downloads or opens it (also several at once, for a batch
download), Popcorn calls `ResolveLinkAsync(link)` (30 s limit) and uses the link it returns. Check the `Url` you get:
it comes from the browser.

### Loading rules (assembly isolation)

Each plugin gets its own `AssemblyLoadContext` (`PluginLoadContext`):

* Your dependencies are resolved from your folder through your `.deps.json`. Two plugins can use different versions
  of the same library, and a version the server also uses doesn't clash.
* `Popcorn.Plugins.Abstractions` and `Microsoft.Extensions.*` **always come from the server**. Shipping your own copy
  has no effect. This is what makes `IMediaSourcePlugin` and `ILogger<T>` the same types on both sides.
* Native libraries are resolved through your `.deps.json` too (`runtimes/<rid>/native/...`).
* The context is collectible, so the plugin can be unloaded or replaced without a restart. Don't leave anything
  running after `Dispose` (timers, background threads, static event subscriptions into the server); that keeps
  the old copy in memory. Native libraries are never unloaded.

Plugins run in the server process with full trust. Only install plugins you trust.

## UI side (optional)

Without a UI module, the built-in components render your data: search card, details page and small card
(`Popcorn.Web/src/plugins/default-components.tsx`). To customize, ship `web/index.js`, an **ES module** with any of
these named exports:

| Export | Props | Replaces |
|---|---|---|
| `SearchCard` | `{ pluginId, card, onOpen? }` | one search result |
| `Details` | `{ pluginId, details, onOpenCard }` | the details page body (the app adds a Back button at the top left) |
| `DetailsCard` | `{ pluginId, card, onOpen? }` | one small card in a details section |
| `FileCard` | `{ pluginId, sourceId, file, openLink, defaultTarget }` | one file source result; `sourceId` is the `FileSource.Id` (`""` for an `IFileSourcePlugin` plugin); call `openLink(link)` to open a link the way the app does (the user's default target), or `openLink(link, 'internal' \| 'external')` to pick one; `defaultTarget(kind)` tells which target that is (e.g. to label a button); optional `add` = `{ onAdd, hint }`, the main "Add" action (download into the library / the open item), absent when nothing is downloadable |

* Every component also receives **`Default`**, the built-in component for that slot. Render `<Default {...props} />`
  to wrap or extend it instead of rebuilding everything. TMDb's `Details` renders `Default` and appends a cast row.
* `onOpen` is undefined when `card.hasDetails` is false. Navigate only through `onOpen` / `onOpenCard`.
* `card.data` / `details.data` hold whatever your C# side put into `Data`.
* A component that throws while rendering is replaced by the built-in one. A module that fails to load disables
  your UI and the built-in UI is used. Both cases are logged in the browser console.
* Don't talk to the server socket from plugin components. Everything the UI needs should come through `Data`.

The built-in `Details` renders section cards through the `DetailsCard` slot, so a custom `DetailsCard` is also used
when you keep the default `Details`.

### Shared modules: one React, one Mantine

Plugin components run inside the app's React tree, so they **must** use the app's React instance. A second copy
breaks hooks, context and the Mantine theme. The app publishes these modules through an
[import map](https://developer.mozilla.org/docs/Web/HTML/Element/script/type/importmap) in `index.html`:

```
react   react/jsx-runtime   react-dom   @mantine/core   @mantine/hooks
```

Your bundle must leave them as **bare imports** (external) and bundle everything else, such as icons, dayjs and your
own code. The list lives in `Popcorn.Web/plugin-sdk/shared.js`. Versions are whatever the app ships (React 19,
Mantine 8). Other modules, including react-router and app internals, are not available.

CSS: Mantine's styles and the app theme are already loaded. A separate CSS file from your build is **not** loaded
automatically, so use Mantine props / `style` or inject CSS from your module.

### `@popcorn/plugin-sdk`

`Popcorn.Web/plugin-sdk` (link it: `"@popcorn/plugin-sdk": "file:<path>/Popcorn.Web/plugin-sdk"`):

* `@popcorn/plugin-sdk`: TypeScript types (`MediaCard`, `MediaDetails`, `FileQuery`, `FileResult`, `FileLink`,
  `SearchCardProps`, `DetailsProps`, `DetailsCardProps`, `FileCardProps`, `WithDefault`, `PluginUiModule`).
* `@popcorn/plugin-sdk/vite`: `popcornPluginConfig({ entry?, outDir? })`, a Vite library-mode config that emits
  `dist/index.js` as ESM with the shared modules external.

Minimal plugin UI project:

```jsonc
// package.json
{
  "type": "module",
  "scripts": { "build": "tsc --noEmit && vite build" },
  "devDependencies": {
    "@popcorn/plugin-sdk": "file:../../../Popcorn.Web/plugin-sdk",
    "@mantine/core": "^8.3.13", "@mantine/hooks": "^8.3.13",   // types only, not bundled
    "react": "^19.2.4", "react-dom": "^19.2.4", "@types/react": "^19.2.10",
    "@vitejs/plugin-react-swc": "^3.8.0", "typescript": "~5.7.2", "vite": "^7.3.1"
  }
}
```

```ts
// vite.config.ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { popcornPluginConfig } from '@popcorn/plugin-sdk/vite';

export default defineConfig({ plugins: [react()], ...popcornPluginConfig() });
```

```tsx
// src/index.tsx
import { Badge } from '@mantine/core';
import type { SearchCardProps, WithDefault } from '@popcorn/plugin-sdk';

interface MyData { rating: number }

export function SearchCard({ Default, ...props }: WithDefault<SearchCardProps<MyData>>) {
  return (
    <div style={{ position: 'relative' }}>
      <Default {...props} />
      {props.card.data && <Badge style={{ position: 'absolute', top: 8, right: 8 }}>{props.card.data.rating}</Badge>}
    </div>
  );
}
```

Copy `dist/*` into the plugin's `web/` folder.

## Developing

* **In this repo:** add `Plugins/<Name>/` (C# project + `plugin.json` + optional `ui/`) and copy the
  `AssemblePlugin` target from `Popcorn.Plugin.Tmdb.csproj`. It assembles build output + `plugin.json` + `ui/dist`
  into `Popcorn.Server/plugins/<id>/` (gitignored), and the server build copies that next to the server. `make
  build-plugins` (part of `make all`) and `build_windows.ps1` build every `Plugins/*` folder: `ui/` first
  (`npm ci && npm run build`), then `dotnet build`. For the dev loop:
  `(cd Plugins/X/ui && npm run build) && dotnet build Plugins/X && dotnet run --project Popcorn.Server`.
* **Separate plugins (out of tree, e.g. scrapers):** compile against the `Popcorn.Plugins.Abstractions.dll` that
  ships next to the server (`~/.local/bin/Popcorn/`), using `<Reference Include="Popcorn.Plugins.Abstractions"><HintPath>…</HintPath><Private>false</Private></Reference>`.
  Zip the output + `plugin.json` (+ `web/`) and install it from Settings → Plugins (it lands in
  `~/.local/share/Popcorn/plugins/<id>/`). `make install-linux` only replaces the program folder, so these survive app updates.
* **Vite dev server** (`npm run dev` in Popcorn.Web): the import map points at Vite's own module URLs, and plugin UI
  modules are imported from the backend (`:5000/plugins/...`; CORS is enabled in Development).
* **Protocol** (if you build another client): `PluginList` → `PluginInfo[] { id, name, version, uiUrl, iconUrl,
  media, fileSources: { id, name, iconUrl, sortFields }[], hasSettings, enabled, loaded, bundled, error, status: { level, message }[] }` (all plugin folders in
  the user's order, also pushed unasked when a plugin's status messages change; only `loaded` ones can be searched; `media` = media source; `fileSources` empty = no file source,
  one with id `""` = the plugin itself is the file source);
  `PluginSetEnabled { pluginId, enabled }`, `PluginSetOrder { pluginIds }` and `PluginUninstall { pluginId }` →
  `PluginList`;
  `PluginSettings { pluginId }` → `{ pluginId, fields, values, secretsSet, actions, error }` (`values` without passwords,
  `secretsSet` = password keys that have a value, `actions` = buttons of the loaded plugin);
  `PluginAction { pluginId, actionId }` → `{ pluginId, actionId, message, warning, error, actions }` (runs off the
  socket's message loop, `actions` = the list after the run);
  `PluginSettingsSave { pluginId, values }` → `{ pluginId, error }`, then `PluginList` (a key left out stays as it is,
  null or blank removes the value);
  `PluginSearch { pluginId, query }` → `{ pluginId, query, results, error }`;
  `PluginDetails { pluginId, id }` → `{ pluginId, id, details, error }`;
  `PluginFileSearch { pluginId, sourceId, requestId, query: FileQuery }` → `{ pluginId, sourceId, requestId, results, error }`;
  `OpenLink { url }` (opens it on the server machine, no response).

## Versioning

`PluginApi.Version` (C#) is bumped on breaking changes to the contract, the models, the UI props or the shared module
set. Plugins declaring another `apiVersion` are refused at load time. Adding optional model fields or new optional UI
exports is not a breaking change.
