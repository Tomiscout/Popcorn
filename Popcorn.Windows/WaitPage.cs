using System.Net;

namespace Popcorn.Windows;

/// <summary>Waiting page (wait.html with the popcorn images inlined), shown until the server answers.</summary>
internal static class WaitPage
{
    private static readonly Lazy<string> Template = new(() => Read("wait.html", stream => new StreamReader(stream).ReadToEnd())
        .Replace("{{ICON}}", Read("popcorn.png", Base64))
        .Replace("{{KERNEL}}", Read("kernel.png", Base64)));

    public static string Html(string status) => Template.Value.Replace("{{STATUS}}", WebUtility.HtmlEncode(status));

    private static string Base64(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Convert.ToBase64String(memory.ToArray());
    }

    private static T Read<T>(string resource, Func<Stream, T> read)
    {
        using var stream = typeof(WaitPage).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing resource {resource}");
        return read(stream);
    }
}
