using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Core.Model;

namespace VideoBlazor.Core
{
	//With given folder scan media and subfolders, group folders
	internal static class MediaLoader
	{
		private static readonly string[] _videoFormats = new string[]{ ".mkv", ".mp4", ".m4v", ".mpeg", ".avi", ".wmv", ".vp6", ".vp7", ".webm", ".amc", ".psv",
			".3gp", ".av1", ".hdmov", ".ogv", ".vc1", ".mp4v", ".mpeg4", ".mpg", ".mov", ".vid", ".h264", ".mp2v", ".m1v", ".xvid", ".divx"};

		public static async Task<IList<MediaScanResult>> ScanFolder(string folder)
		{
			var resultList = new List<MediaScanResult>();

            var rootDirectories = Directory.GetDirectories(folder);
			//TODO filter out existing dirs
			
			//Dirs
			foreach (var rootDirectory in rootDirectories)
			{
                var allPaths = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories).ToList();
                var scanResult = new MediaScanResult
                {
                    FolderPath = rootDirectory,
                    Title = Path.GetFileName(rootDirectory),
                    Files = new List<string>(),
                };

				foreach (var format in _videoFormats)
				{
					var resultPaths = allPaths.Where(p => p.EndsWith(format));
                    scanResult.Files.AddRange(resultPaths.ToList());
                }

				if(scanResult.Files.Count > 0)
				{
					resultList.Add(scanResult);
				}
            }

			//Files
			var allFiles = Directory.GetFiles(folder);
			foreach(var file in allFiles)
			{
                foreach (var format in _videoFormats)
                {
                    if(file.EndsWith(format))
					{
						resultList.Add(new MediaScanResult
                        {
                            FolderPath = folder,
                            Title = Path.GetFileName(file),
                            Files = new List<string>() { file },
                        });
						break;
					}
                }
            }

			return resultList.OrderBy(r => r.Files.Count).Reverse().ToList();
        }
	}
}
