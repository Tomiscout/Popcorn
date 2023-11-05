using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SegmentFinder;
public class SegmentLocation
{
    public double StartSeconds { get; set; }
    public double Length { get; set; }

    public override string ToString()
    {
        return $"{StartSeconds.ToString("0.###")}, seconds:{Length.ToString("#.###")}";
    }
}
