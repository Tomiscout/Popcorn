using VideoBlazor.Shared.Utils;

namespace VideoBlazor.Persistence
{
    public class ImageRepository
    {
        private Dictionary<string, string> _imageCache = new Dictionary<string, string>();
        private readonly string _imageFolder = Path.Join(DirUtils.DataFolder, "images/");

        public ImageRepository()
        {
            EnsureImageFolder();
        }

        public string GetImageFolder()
        {
            return _imageFolder; 
        }
       

        public void SaveImage(string imagePath, string newFilename)
        {
            //Save image
            var extension = Path.GetExtension(imagePath);
            var newFilePath = Path.Join(_imageFolder, newFilename + extension);
            File.Copy(imagePath, newFilePath);

            //Update cache
            LoadImage(newFilePath);
        }

        public async Task<string> LoadImage(string fileName, bool saveCache = true)
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

        private string GetWebImageFormat(string format) => format.ToLower() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".gif" => "image/gif",
            _ => throw new BadImageFormatException("Image format not found")
        };

        public void EnsureImageFolder()
        {
            Directory.CreateDirectory(GetImageFolder());
        }
    }
}
