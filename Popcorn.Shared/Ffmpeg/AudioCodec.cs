namespace Popcorn.Shared.Ffmpeg;

public enum AudioCodec
{
    Unknown,
    AAC,
    MP3,
    Opus,
    Vorbis,
}

public static class AudioCodecUtils
{
    public static AudioCodec ToAudioCodec(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
            return AudioCodec.Unknown;

        // TryParse will ignore case when the third parameter is true
        return Enum.TryParse(codecName.Trim(), ignoreCase: true, out AudioCodec result)
            ? result
            : AudioCodec.Unknown;
    }
}