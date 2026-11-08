import type { FileLink, FileQuery, FileResult, FileSortField, MediaCard, MediaDetails } from '@popcorn/plugin-sdk';

// Popcorn.Server/Models/Requests/PluginRequests.cs
export interface PluginInfo {
  id: string;
  name: string;
  version: string;
  /** Server-relative URL of the plugin's UI module, null when it has none. */
  uiUrl: string | null;
  /** Server-relative URL of the plugin's icon, null when it has none. */
  iconUrl: string | null;
  /** Loaded and a media information source (Media tab). */
  media: boolean;
  /** File sources of a loaded plugin (Files tab, Files section of details pages); empty = none. */
  fileSources: FileSourceInfo[];
  /** Image kinds a loaded image provider supplies (posters/backdrops of library items); empty = none. */
  imageKinds: ImageKind[];
  /** plugin.json declares settings or the loaded plugin has actions: it has a settings dialog. */
  hasSettings: boolean;
  enabled: boolean;
  /** Enabled and loaded without errors: usable for search. */
  loaded: boolean;
  /** Ships with the program (can be disabled, not uninstalled). */
  bundled: boolean;
  /** Did not load (or plugin.json is unreadable). */
  error: string | null;
  /** Messages of the loaded plugin (C# `IPluginStatus`); empty = none. */
  status: PluginStatusMessage[];
}

export type PluginStatusLevel = 'Info' | 'Warning' | 'Error';

export interface PluginStatusMessage {
  level: PluginStatusLevel;
  message: string;
}

/** A file source of a plugin; a plugin that is itself the file source has one with id "". */
export interface FileSourceInfo {
  id: string;
  name: string;
  /** Server-relative URL of the source's icon (else the plugin's), null when there is none. */
  iconUrl: string | null;
  /** Sort columns; the first one is the source's default sort. */
  sortFields: FileSortField[];
}

export interface PluginSetEnabledRequest {
  pluginId: string;
  enabled: boolean;
}

export interface PluginSetOrderRequest {
  pluginIds: string[];
}

export interface PluginUninstallRequest {
  pluginId: string;
}

// Popcorn.Server/PluginHost/PluginSettings.cs
export interface PluginSettingField {
  key: string;
  label: string;
  /** `number` = whole number, `float` = any number. */
  type: 'text' | 'password' | 'number' | 'float' | 'boolean' | 'select';
  description: string | null;
  placeholder: string | null;
  default: PluginSettingValue;
  required: boolean;
  options: { value: string; label: string }[] | null;
  /** The value is an array of `type` values (text, password, number, float). */
  list: boolean;
  /** Heading the field is shown under; null = no heading. */
  group: string | null;
}

export type PluginSettingValue = string | number | boolean | string[] | number[] | null;

export interface PluginSettingsRequest {
  pluginId: string;
}

export interface PluginSettingsResponse {
  pluginId: string;
  fields: PluginSettingField[];
  /** Key to current value or default; password fields are never sent, see `secretsSet`. */
  values: Record<string, PluginSettingValue>;
  /** Keys of the password fields that have a value. */
  secretsSet: string[];
  /** Buttons of a loaded plugin; empty = none. */
  actions: PluginAction[];
  error: string | null;
}

/** A button in the plugin's settings dialog (C# `IPluginActions`, Popcorn.Plugins.Abstractions). */
export interface PluginAction {
  id: string;
  label: string;
  description: string | null;
  /** Settings group to show it in; null = after the fields without a group. */
  group: string | null;
  /** Mantine color name; null = default. */
  color: string | null;
  /** Question the user confirms before it runs; null = runs right away. */
  confirm: string | null;
  disabled: boolean;
}

export interface PluginActionRequest {
  pluginId: string;
  actionId: string;
}

export interface PluginActionResponse {
  pluginId: string;
  actionId: string;
  /** Message of a successful run. */
  message: string | null;
  /** It ran but the plugin reports a warning. */
  warning: boolean;
  /** Set when it failed. */
  error: string | null;
  /** The plugin's actions after the run. */
  actions: PluginAction[];
}

export interface PluginSettingsSaveRequest {
  pluginId: string;
  /** A key left out stays as it is; null or blank removes the value. */
  values: Record<string, PluginSettingValue>;
}

export interface PluginSettingsSaveResponse {
  pluginId: string;
  error: string | null;
}

export interface PluginSearchRequest {
  pluginId: string;
  query: string;
}

export interface PluginSearchResponse {
  pluginId: string;
  query: string;
  results: MediaCard[];
  error: string | null;
}

export interface PluginDetailsRequest {
  pluginId: string;
  id: string;
}

export interface PluginDetailsResponse {
  pluginId: string;
  id: string;
  details: MediaDetails | null;
  error: string | null;
}

export interface PluginFileSearchRequest {
  pluginId: string;
  sourceId: string;
  /** Echoed in the response. */
  requestId: string;
  query: FileQuery;
}

export interface PluginFileSearchResponse {
  pluginId: string;
  sourceId: string;
  requestId: string;
  results: FileResult[];
  error: string | null;
}

export interface PluginResolveLinksRequest {
  pluginId: string;
  sourceId: string;
  /** Echoed in the response. */
  requestId: string;
  links: FileLink[];
}

export interface PluginResolveLinksResponse {
  requestId: string;
  /** Same order as the request; null where it failed. */
  links: (FileLink | null)[];
  errors: (string | null)[];
}

export interface OpenLinkRequest {
  url: string;
}

// Popcorn.Plugins.Abstractions (IImageProvider)
export type ImageKind = 'Poster' | 'Backdrop';

export interface ImageResult {
  /** Full-size image, stored by the server when picked. */
  url: string;
  thumbnailUrl: string | null;
  title: string | null;
}

// Popcorn.Server/Models/Requests/PluginRequests.cs
export interface MediaImageSearchRequest {
  /** Item name as typed or the folder name; the server parses title/year. */
  name: string;
  kind: ImageKind;
  requestId: string;
}

export interface MediaImageSearchResponse {
  kind: ImageKind;
  requestId: string | null;
  /** One per enabled image provider of `kind`, in the plugin order. */
  providers: MediaImageProviderResult[];
}

export interface MediaImageProviderResult {
  pluginId: string;
  pluginName: string;
  images: ImageResult[];
  error: string | null;
}
