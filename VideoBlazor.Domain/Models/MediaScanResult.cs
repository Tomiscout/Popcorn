using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Domain.Models
{
	public class MediaScanResult
	{
		public string FolderPath { get; set; }
		public List<string> Files { get; set; }
		public string Title { get; set; }
	}
}
