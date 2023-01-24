using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Core
{
    public static class ImageUtils
    {
        private static Dictionary<string, string> _imageCache = new Dictionary<string, string>();
        private static readonly string _imageFolder = Path.Join(FileSystem.Current.AppDataDirectory, "images/");
        public static string GetImageFolder()
        {
            return _imageFolder;
        }
       

        public static void SaveImage(string imagePath, string newFilename)
        {
            //Save image
            var extension = Path.GetExtension(imagePath);
            var newFilePath = Path.Join(_imageFolder, newFilename + extension);
            File.Copy(imagePath, newFilePath);

            //Update cache
            LoadImage(newFilePath);
        }

        public static async Task<string> LoadImage(string fileName, bool saveCache = true)
        {
            var appPath = Path.Join(_imageFolder, fileName);
            if (!File.Exists(fileName) && File.Exists(appPath))
            {
                fileName = appPath;
            }

            //Load from cache
            if (_imageCache.ContainsKey(fileName))
            {
                return _imageCache[fileName];
            }

            //If cache miss - load file to cache and return
            var imageBytes = await File.ReadAllBytesAsync(fileName);
            var imageFormat = GetWebImageFormat(Path.GetExtension(fileName));

            var imageSource = Convert.ToBase64String(imageBytes);
            var imageData = $"data:{imageFormat};base64,{imageSource}";
            if(saveCache)
            {
                _imageCache.Add(fileName, imageData);
            }

            return imageData;
        }

        private static string GetWebImageFormat(string format) => format.ToLower() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".gif" => "image/gif",
            _ => throw new BadImageFormatException("Image format not found")
        };

        public static void EnsureImageFolder()
        {
            Directory.CreateDirectory(GetImageFolder());
        }
    }
}
