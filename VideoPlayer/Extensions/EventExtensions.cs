using System.Text.Json;

namespace VideoPlayer.Extensions;
internal static class EventExtensions
{
    public static string GetJsonString(this object obj)
    {
        return ((JsonElement)obj).GetString();
    }
}
