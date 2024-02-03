using MudBlazor;
using Popcorn.Domain;

namespace Popcorn.Core.Utils
{
    public static class MudUtils
    {
        public static string GetIconFromFilename(string filename)
        {
            var extension = "." + filename.Split('.').Last();
            
            if(MediaLoader.VideoFormats.Any(f => f == extension))
            {
                return Icons.Material.Filled.VideoFile;
            }
            
            switch (extension)
            {
                case ".mp3":
                case ".ogg":
                case ".opus":
                case ".m4a":
                case ".wav":
                case ".flac":
                    return Icons.Material.Filled.AudioFile;
                case ".jpeg":
                case ".jpg":
                case ".png":
                case ".gif":
                case ".tiff":
                case ".webp":
                    return Icons.Material.Filled.Image;
                case ".srt":
                    return Icons.Material.Filled.Subtitles;
                default:
                    return Icons.Custom.FileFormats.FileDocument;
            }
        }
    }
}
