using System.Text.Json;
using System.Text.Json.Serialization;

namespace Popcorn.Domain.Enum;

/// <summary>
/// When the player skips intros/outros on its own (count-down on the skip button). Global in
/// <see cref="Models.SettingsModel.SegmentAutoSkip"/>, overridable per library item.
/// </summary>
[JsonConverter(typeof(SegmentAutoSkipJsonConverter))]
public enum SegmentAutoSkip
{
    /// <summary>Every intro/outro.</summary>
    Always = 0,
    // 1 was AfterFirstInItem, stored rows and settings read it as AfterFirstOccurrence.
    /// <summary>Not the first occurrence of each segment (a new series or a new season's new intro is shown once).</summary>
    AfterFirstOccurrence = 2,
    /// <summary>Only the skip button.</summary>
    Never = 3,
}

/// <summary>Enum names as JSON; settings saved with the removed <c>AfterFirstInItem</c> still load.</summary>
public sealed class SegmentAutoSkipJsonConverter : JsonConverter<SegmentAutoSkip>
{
    public override SegmentAutoSkip Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value == "AfterFirstInItem") return SegmentAutoSkip.AfterFirstOccurrence;
        return System.Enum.TryParse<SegmentAutoSkip>(value, out var mode) ? mode : SegmentAutoSkip.Always;
    }

    public override void Write(Utf8JsonWriter writer, SegmentAutoSkip value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
