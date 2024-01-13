namespace VideoBlazor.Domain.Models;

public class SegmentLocation
{
    public Guid SegmentId { get; set; }
    public double StartSeconds { get; set; }
    public double Length { get; set; }

    public override string ToString()
    {
        return $"{StartSeconds.ToString("0.###")}, seconds:{Length.ToString("#.###")}";
    }
}
