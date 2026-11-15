namespace Popcorn.Server.Models;

public static class SocketRequests
{
    /// <summary>Push instead of polling: <c>SubscriptionRequest</c> topic (+ key) the server sends whenever it changes, the current state right away.</summary>
    public const string Subscribe = "Subscribe";
    public const string Unsubscribe = "Unsubscribe";
    public const string MediaList = "MediaList";
    public const string PlaybackProgress = "PlaybackProgress";
    /// <summary>
    /// Web player, ~2 min before the outro of the current file (file id): prepare the next file for the browser (remux,
    /// separate audio track) in the background. Playing it before that is done joins the running work.
    /// </summary>
    public const string PlaybackPreload = "PlaybackPreload";
    /// <summary>Item page: <c>ContinueWatching</c> of the item (null when it has no file), sent with <see cref="MediaItem"/>.</summary>
    public const string MediaItemContinue = "MediaItemContinue";
    public const string MediaItem = "MediaItem";
    public const string RemoveMediaItem = "RemoveMediaItem";
    public const string RefreshMediaItem = "RefreshMediaItem";
    public const string OpenMediaItemFolder = "OpenMediaItemFolder";
    public const string MediaItemFileList = "MediaItemFileList";
    public const string MediaItemHistory = "MediaItemHistory";
    /// <summary>Item page History tab: deletes one entry, or the whole history without <c>HistoryId</c>; answered with <see cref="MediaItemHistoryChanged"/>.</summary>
    public const string MediaItemHistoryDelete = "MediaItemHistoryDelete";
    /// <summary>
    /// Subscribable (+ item id), a ping with the item id: its history changed (played files, what Play continues), also
    /// when the native player played something. The page asks for the item again.
    /// </summary>
    public const string MediaItemHistoryChanged = "MediaItemHistoryChanged";
    /// <summary>Item page: every file of the item in play order (<c>MediaItemFlatFileList</c> response), sent with <see cref="MediaItem"/>.</summary>
    public const string MediaItemFlatFileList = "MediaItemFlatFileList";
    public const string RenameMediaItem = "RenameMediaItem";
    /// <summary>Library items ranked by how well their title fits a release / file name (<c>MediaItemMatchResponse</c>).</summary>
    public const string MediaItemMatch = "MediaItemMatch";
    /// <summary>Merges one item into another: each of its sources becomes a virtual folder of the target.</summary>
    public const string MediaItemMerge = "MediaItemMerge";
    /// <summary>Removes a source (virtual folder) from an item: its files and downloads leave the library, optionally the disk.</summary>
    public const string MediaItemSourceRemove = "MediaItemSourceRemove";
    public const string ImageFile = "ImageFile";
    public const string ThumbnailFile = "ThumbnailFile";
    public const string ImageFilesBatchSent = "ImageFilesBatchSent";
    public const string FileOpen = "FileOpen";
    public const string FileOpenWith = "FileOpenWith";
    public const string FolderOpen = "FolderOpen";
    public const string FolderOpenPlayer = "FolderOpenPlayer";
    public const string NextFile = "NextFile";
    public const string PlayerNextFile = "PlayerNextFile";
    public const string PlayerRefreshFiles = "PlayerRefreshFiles";
    public const string LibraryFolders = "LibraryFolders";
    public const string LibraryFoldersData = "LibraryFoldersData";
    public const string RemoveLibraryFolder = "RemoveLibraryFolder";
    public const string AddLibraryFolderPicker = "AddLibraryFolderPicker";
    public const string CreateMediaItem = "CreateMediaItem";
    public const string SetMediaItemImage = "SetMediaItemImage";
    public const string MediaImageSearch = "MediaImageSearch";
    public const string GetSettings = "GetSettings";
    public const string UpdateSettings = "UpdateSettings";
    public const string DefaultPlayerName = "DefaultPlayerName";
    public const string PickPlayerProgram = "PickPlayerProgram";
    public const string OpenProgramFolder = "OpenProgramFolder";
    public const string Autostart = "Autostart";
    public const string AutostartSet = "AutostartSet";
    public const string LanguageList = "LanguageList";
    public const string UpdateStatus = "UpdateStatus";
    public const string UpdateCheck = "UpdateCheck";
    public const string UpdateInstall = "UpdateInstall";
    public const string UpdateOpenInstaller = "UpdateOpenInstaller";
    public const string CacheList = "CacheList";
    public const string CacheClear = "CacheClear";
    public const string CacheClearLogs = "CacheClearLogs";
    public const string ErrorWhenHandlingEvent = "ErrorWhenHandlingEvent";
    public const string PluginList = "PluginList";
    public const string PluginSearch = "PluginSearch";
    public const string PluginDetails = "PluginDetails";
    public const string PluginSetEnabled = "PluginSetEnabled";
    public const string PluginUninstall = "PluginUninstall";
    public const string PluginSetOrder = "PluginSetOrder";
    public const string PluginFileSearch = "PluginFileSearch";
    public const string PluginSettings = "PluginSettings";
    public const string PluginSettingsSave = "PluginSettingsSave";
    public const string PluginAction = "PluginAction";
    public const string PluginResolveLinks = "PluginResolveLinks";
    public const string OpenLink = "OpenLink";
    public const string SegmentsState = "SegmentsState";
    public const string SegmentsDetect = "SegmentsDetect";
    public const string SegmentsScan = "SegmentsScan";
    public const string SegmentsSetType = "SegmentsSetType";
    public const string SegmentsDelete = "SegmentsDelete";
    public const string SegmentsApply = "SegmentsApply";
    public const string SegmentsEditInfo = "SegmentsEditInfo";
    public const string SegmentsSetRange = "SegmentsSetRange";
    public const string SegmentSeen = "SegmentSeen";
    public const string SegmentAutoSkipSet = "SegmentAutoSkipSet";
    public const string SegmentAutoSkipDelaySet = "SegmentAutoSkipDelaySet";
    public const string DownloadList = "DownloadList";
    public const string DownloadDetails = "DownloadDetails";
    public const string DownloadStats = "DownloadStats";
    public const string DownloadAdd = "DownloadAdd";
    public const string DownloadAddTorrentFile = "DownloadAddTorrentFile";
    public const string DownloadAddBatch = "DownloadAddBatch";
    public const string DownloadAdded = "DownloadAdded";
    public const string DownloadPause = "DownloadPause";
    public const string DownloadResume = "DownloadResume";
    public const string DownloadRemove = "DownloadRemove";
    public const string DownloadSetFilePriority = "DownloadSetFilePriority";
    public const string DownloadSetPriority = "DownloadSetPriority";
    public const string DownloadOpenFolder = "DownloadOpenFolder";
    public const string PickDownloadFolder = "PickDownloadFolder";
}
