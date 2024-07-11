using System.Text.RegularExpressions;

namespace Popcorn.Player.Utils;

public abstract partial class TrackDescriptionParser
{
    public static (string Language, string Description) Parse(string input)
    {
        Match match = DescriptionRegex().Match(input);

        if (match.Success && match.Groups.Count == 3)
        {
            var description = match.Groups[1].Value;
            var language = match.Groups[2].Value;
            return (language, description);
        }
        else
        {
            return (input, string.Empty);
        }
    }

    [GeneratedRegex(@"(.*?) - \[(.*?)\]")]
    private static partial Regex DescriptionRegex();
}