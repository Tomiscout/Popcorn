using System.Text.Json.Serialization;

namespace Popcorn.Domain.Enum;

/// <summary>The pictures of a library item: <see cref="Models.MediaItem.ImagePath"/> / <see cref="Models.MediaItem.BackdropPath"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaImageKind>))]
public enum MediaImageKind
{
    Poster,
    Backdrop,
}
