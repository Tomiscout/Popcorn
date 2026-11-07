using Microsoft.Extensions.Logging;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Repositories;

namespace Popcorn.Application.Services;

/// <summary>
/// Poster and backdrop of library items: fetches the picked image (an image provider's http(s) URL, or a
/// <c>data:image/...;base64,</c> URL for a file the user uploaded), stores it in the images folder and replaces the
/// item's previous one. Every save gets a new file name, so clients never show a cached old picture.
/// </summary>
public class MediaImageService(IMediaItemRepository mediaItemRepository, ImageRepository imageRepository, ILogger<MediaImageService> logger)
{
    private const int MaxImageBytes = 20 * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    /// <summary>
    /// Sets the item's <paramref name="kind"/> image from <paramref name="source"/> (null/empty = remove it). Throws
    /// <see cref="InvalidDataException"/> for unsupported or too large images and when the download fails.
    /// </summary>
    public async Task SetImageAsync(MediaItem item, MediaImageKind kind, string? source, CancellationToken cancellationToken = default)
    {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            var (data, extension) = await FetchAsync(source.Trim(), cancellationToken);
            path = await imageRepository.SaveImage(data, $"{item.Id}-{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}", extension);
        }

        var previous = kind == MediaImageKind.Poster ? item.ImagePath : item.BackdropPath;
        await mediaItemRepository.SetImagePath(item.Id, kind, path);
        if (kind == MediaImageKind.Poster) item.ImagePath = path;
        else item.BackdropPath = path;
        imageRepository.DeleteImage(previous);
    }

    /// <summary><see cref="SetImageAsync"/> that only logs failures (adding an item must not fail on its pictures).</summary>
    public async Task TrySetImageAsync(MediaItem item, MediaImageKind kind, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            await SetImageAsync(item, kind, source);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not set the {kind} of {title}", kind, item.Title);
        }
    }

    /// <summary>Deletes the stored pictures of a removed item.</summary>
    public void DeleteImages(MediaItem item)
    {
        imageRepository.DeleteImage(item.ImagePath);
        imageRepository.DeleteImage(item.BackdropPath);
    }

    private static async Task<(byte[] Data, string Extension)> FetchAsync(string source, CancellationToken cancellationToken)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');
            var header = comma > 0 ? source[5..comma] : string.Empty; // "image/png;base64"
            var mime = header.Split(';')[0];
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) || !Extensions.TryGetValue(mime, out var dataExtension))
                throw new InvalidDataException("Only base64 jpeg, png, webp and gif images are supported");
            if ((source.Length - comma) / 4 * 3 > MaxImageBytes) throw TooLarge();
            try
            {
                return (Convert.FromBase64String(source[(comma + 1)..]), dataExtension);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("Invalid image data", ex);
            }
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException("Image URL must be http(s) or a data: URL");

        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var mime = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var extension = Extensions.GetValueOrDefault(mime)
                            ?? throw new InvalidDataException($"{uri} is not a supported image ({mime})");
            if (response.Content.Headers.ContentLength > MaxImageBytes) throw TooLarge();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes) throw TooLarge();
                buffer.Write(chunk, 0, read);
            }
            return (buffer.ToArray(), extension);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidDataException($"Could not download {uri}: {ex.Message}", ex);
        }
    }

    private static InvalidDataException TooLarge() => new($"Images over {MaxImageBytes / 1024 / 1024} MB are not supported");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Popcorn/1.0");
        return client;
    }
}
