using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Shared.Utils
{
    public static class StringUtils
    {
        public static string FormatBytes(long bytes, bool perSecond = false)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double length = bytes;
            int order = 0;
            while (length >= 1024 && order < sizes.Length - 1)
            {
                order++;
                length /= 1024;
            }

            string sizeString = string.Format("{0:0.##} {1}", length, sizes[order]);
            if (perSecond)
            {
                sizeString += "/s";
            }

            return sizeString;
        }
    }
}
