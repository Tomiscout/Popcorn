using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Core.Model
{
	public class MediaScanResult
	{
		public string FolderPath { get; set; }
		public List<string> Files { get; set; }
		public string Title { get; set; }
	}
}
