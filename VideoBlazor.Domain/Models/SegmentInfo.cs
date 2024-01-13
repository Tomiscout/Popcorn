
namespace VideoBlazor.Domain.Models;
public class SegmentInfo
{
    public Guid Id { get; set; }
    public string FilePath { get; set; }
    public double Length { get; set; }
    public SegmentType Type { get; set; }
}

public enum SegmentType
{
    Intro,
    Outro
}
