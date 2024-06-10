using System.ComponentModel;
using Microsoft.AspNetCore.StaticFiles;

namespace Popcorn.Domain;

public static class MimeUtils
{
    private const string DefaultContentType = "application/octet-stream";
    private static FileExtensionContentTypeProvider _provider;

    static MimeUtils()
    {
        _provider = new FileExtensionContentTypeProvider();

        _provider.AddMimeType(".torrent", "application/x-bittorrent");
    }

    public static string GetMimeTypeForFileExtension(string filePath)
    {
        if (!_provider.TryGetContentType(filePath, out string contentType))
        {
            contentType = DefaultContentType;
        }

        return contentType;
    }

    private static void AddMimeType(this FileExtensionContentTypeProvider provider, string extension, string mimeType)
    {
        if (!_provider.Mappings.TryGetValue(extension, out var _))
        {
            provider.Mappings.Add(extension, mimeType);
        }
        else
        {
            provider.Mappings[extension] = mimeType;
        }
    }
}
