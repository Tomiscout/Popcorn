
namespace Popcorn.Domain.Models
{
	public class MediaScanResult
	{
		public string FolderPath { get; set; }
		public List<string> Files { get; set; }
		public List<string> Folders { get; set; }
		public string Title { get; set; }
	}
}
