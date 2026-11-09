namespace Popcorn.Shared.Ffmpeg;

public static class CodecUtils
{
    public static bool IsAudioCodecMp4Compatible(string codecName) =>
        codecName.ToLowerInvariant() switch
        {
            "aac" or "aac_lc" or "mp4a" or "he-aac" or "heaac" => true,
            "mp3" => true,
            "alac" => true,
            "ac3" => true,
            "eac3" or "ec-3" => true,
            "truehd" => true,
            "opus" => true,
            "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_s16be"
                or "pcm_s24be" or "pcm_s32be" or "pcm_f32le" or "pcm_f64le" => true,
            _ => false
        };
}