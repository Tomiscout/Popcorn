using System.Text.Json;

namespace PopcornPlayer.Extensions;
internal static class EventExtensions
{
    public static string GetJsonString(this object obj)
    {
        return ((JsonElement)obj).GetString();
    }

    public static int GetJsonInt32(this object obj)
    {
        return ((JsonElement)obj).GetInt32();
    }

    public static long GetJsonInt64(this object obj)
    {
        return ((JsonElement)obj).GetInt64();
    }
}
