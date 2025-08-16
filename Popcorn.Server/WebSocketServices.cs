using Popcorn.Domain.Models;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Services;

namespace Popcorn.Server;

public static class WebSocketServices
{
    public static void RegisterWebServices(WebSocketBus bus)
    {
        bus.RegisterWebServiceHandler<MediaItemWebService, MediaItemRequest>(SocketRequests.MediaItem, (service, request) => service.SendMediaItems(request));
        bus.RegisterWebServiceHandler<MediaItemWebService, MediaItemFileListRequest>(SocketRequests.MediaItemFileList, (service, request) => service.SendMediaItemFileList(request));
        bus.RegisterWebServiceHandler<MediaItemWebService, Guid>(SocketRequests.RemoveMediaItem, (service, request) => service.DeleteMediaItem(request));
        bus.RegisterWebServiceHandler<MediaItemWebService, Guid>(SocketRequests.RefreshMediaItem, (service, request) => service.RescanMediaItem(request));
        bus.RegisterWebServiceHandler<MediaItemWebService, Guid>(SocketRequests.OpenMediaItemFolder, (service, request) => service.OpenMediaItemFolder(request));
        bus.RegisterWebServiceBinaryHandler<MediaItemWebService>(SocketRequests.CreateMediaItem, (service, metadata, fileBytes) => service.CreateMediaItem(metadata, fileBytes));
        
        bus.RegisterWebServiceHandler<HomeWebService, object>(SocketRequests.MediaList, (service, _) => service.SendHomeScreenMediaList());

        bus.RegisterWebServiceHandler<FileWebService, Guid>(SocketRequests.FileOpen, (service, request) => service.OpenFile(request));
        bus.RegisterWebServiceHandler<FileWebService, Guid>(SocketRequests.FolderOpen, (service, request) => service.OpenFolder(request));
        bus.RegisterWebServiceHandler<FileWebService, List<string>>(SocketRequests.ImageFile, (service, request) => service.SendImageFiles(request));
        bus.RegisterWebServiceHandler<FileWebService, Dictionary<string, ParsedTags>>(SocketRequests.ParseTags, (service, request) => service.HandleParsedTags(request));

        bus.RegisterWebServiceHandler<LibraryWebService, object>(SocketRequests.LibraryFolders, (service, _) => service.SendLibraryFolders());
        bus.RegisterWebServiceHandler<LibraryWebService, string>(SocketRequests.RemoveLibraryFolder, (service, request) => service.RemoveLibraryFolder(request));
        bus.RegisterWebServiceHandler<LibraryWebService, object>(SocketRequests.AddLibraryFolderPicker, (service, _) => service.AddLibraryFolderPicker());
        bus.RegisterWebServiceHandler<LibraryWebService, object>(SocketRequests.LibraryFoldersData, (service, _) => service.SendLibraryFoldersData());

        bus.RegisterWebServiceHandler<SettingsWebService, object>(SocketRequests.GetSettings, (service, _) => service.SendSettings());
        bus.RegisterWebServiceHandler<SettingsWebService, SettingsModel>(SocketRequests.UpdateSettings, (service, request) => service.UpdateSettings(request));
        bus.RegisterWebServiceHandler<SettingsWebService, object>(SocketRequests.OpenProgramFolder, (service, _) => service.OpenProgramFolder());
    }
}