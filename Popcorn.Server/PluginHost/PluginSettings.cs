using System.Text.Json;
using System.Text.Json.Nodes;
using Popcorn.Plugins;

namespace Popcorn.Server.PluginHost;

/// <summary>A choice of a <c>select</c> setting.</summary>
public class PluginSettingOption
{
    public string Value { get; set; } = string.Empty;
    /// <summary>Defaults to <see cref="Value"/>.</summary>
    public string? Label { get; set; }
}

/// <summary>One entry of <c>settings</c> in plugin.json; the settings dialog renders a form from these.</summary>
public class PluginSettingField
{
    public const string Text = "text", Password = "password", Number = "number", Float = "float", Boolean = "boolean", Select = "select";

    /// <summary>Key in settings.json.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Defaults to <see cref="Key"/>.</summary>
    public string? Label { get; set; }
    public string Type { get; set; } = Text;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    /// <summary>Used while settings.json has no value.</summary>
    public JsonElement? Default { get; set; }
    public bool Required { get; set; }
    /// <summary>Choices of a <c>select</c>.</summary>
    public List<PluginSettingOption>? Options { get; set; }
    /// <summary>The value is a JSON array of values of <see cref="Type"/> (text, password, number, float).</summary>
    public bool List { get; set; }
    /// <summary>Heading the form shows the field under; fields of one group are shown together, groups in order of first use.</summary>
    public string? Group { get; set; }

    /// <summary>Why the value does not fit this field; null when it does.</summary>
    internal string? ValueError(JsonElement value)
    {
        if (!List) return ItemError(value);
        if (value.ValueKind != JsonValueKind.Array) return "must be a list";
        foreach (var item in value.EnumerateArray())
        {
            if (IsBlank(item)) return "has an empty entry";
            if (ItemError(item) is { } error) return $"entry {error}";
        }
        return null;
    }

    private string? ItemError(JsonElement value) => Type switch
    {
        Text or Password => value.ValueKind == JsonValueKind.String ? null : "must be text",
        Number => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _) ? null : "must be a whole number",
        Float => value.ValueKind == JsonValueKind.Number ? null : "must be a number",
        Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be true or false",
        Select => value.ValueKind == JsonValueKind.String && Options!.Any(o => o.Value == value.GetString()) ? null : "is not one of the options",
        _ => $"has unknown type '{Type}'",
    };

    /// <summary>Null, blank text or an empty list: no value.</summary>
    internal static bool IsBlank(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    /// <summary>Validates the <c>settings</c> of a plugin.json and fills in defaults.</summary>
    internal static void Normalize(List<PluginSettingField> fields)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || !keys.Add(field.Key))
                throw new InvalidDataException($"plugin.json: settings key '{field.Key}' is empty or not unique");
            if (field.Type is not (Text or Password or Number or Float or Boolean or Select))
                throw new InvalidDataException($"plugin.json: setting '{field.Key}' has unknown type '{field.Type}'");
            if (field.List && field.Type is Boolean or Select)
                throw new InvalidDataException($"plugin.json: setting '{field.Key}' of type '{field.Type}' cannot be a list");
            if (string.IsNullOrWhiteSpace(field.Group)) field.Group = null;
            if (field.Type == Select && field.Options is not { Count: > 0 })
                throw new InvalidDataException($"plugin.json: select setting '{field.Key}' needs options");
            if (string.IsNullOrWhiteSpace(field.Label)) field.Label = field.Key;
            foreach (var option in field.Options ?? []) option.Label ??= option.Value;
            if (field.Default is { } value && value.ValueKind != JsonValueKind.Null && field.ValueError(value) is { } error)
                throw new InvalidDataException($"plugin.json: default of setting '{field.Key}' {error}");
        }
    }
}

/// <summary>
/// A plugin's settings.json: a flat JSON object, keys as declared in plugin.json. Saving keeps keys that plugin.json does
/// not declare. Password values never leave the server through <see cref="PublicValues"/>.
/// </summary>
public sealed class PluginSettingsFile(string path, IReadOnlyList<PluginSettingField> fields) : IPluginSettings
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public IReadOnlyList<PluginSettingField> Fields => fields;

    public string? GetString(string key) => TryGet<string>(key);
    public double? GetNumber(string key) => TryGetStruct<double>(key);
    public bool? GetBool(string key) => TryGetStruct<bool>(key);
    public IReadOnlyList<string>? GetStringList(string key) => GetList<string>(key);
    public IReadOnlyList<double>? GetNumberList(string key) => GetList<double>(key);

    /// <summary>Current values and defaults for the settings form, without passwords.</summary>
    public Dictionary<string, JsonElement> PublicValues()
    {
        var file = Read();
        var values = new Dictionary<string, JsonElement>();
        foreach (var field in fields.Where(f => f.Type != PluginSettingField.Password))
        {
            var stored = file[field.Key] is { } node ? JsonSerializer.SerializeToElement(node) : (JsonElement?)null;
            if (stored is { } value && field.ValueError(value) is null) values[field.Key] = value; // else a bad hand edit
            else if (field.Default is { ValueKind: not JsonValueKind.Null } fallback) values[field.Key] = fallback;
        }
        return values;
    }

    /// <summary>Keys of the password fields that have a value.</summary>
    public List<string> SecretsSet()
    {
        var file = Read();
        return fields
            .Where(f => f.Type == PluginSettingField.Password && !IsBlankNode(file[f.Key]))
            .Select(f => f.Key)
            .ToList();
    }

    /// <summary>
    /// Applies the values of declared fields: a missing key stays as it is, null, blank or the default removes the value
    /// (so the default applies). Throws <see cref="InvalidDataException"/> on an invalid value or a missing required one.
    /// </summary>
    public void Save(IReadOnlyDictionary<string, JsonElement> values)
    {
        var file = Read();
        foreach (var field in fields)
        {
            if (!values.TryGetValue(field.Key, out var value)) continue;
            // The form sends defaults back too: not storing them lets a plugin update change its default
            if (PluginSettingField.IsBlank(value) || (field.Default is { } fallback && JsonElement.DeepEquals(fallback, value)))
            {
                file.Remove(field.Key);
                continue;
            }
            if (field.ValueError(value) is { } error) throw new InvalidDataException($"{field.Label} {error}");
            file[field.Key] = JsonSerializer.SerializeToNode(value);
        }

        var missing = fields.FirstOrDefault(f => f.Required && IsBlankNode(file[f.Key]) && f.Default is not { ValueKind: not JsonValueKind.Null });
        if (missing is not null) throw new InvalidDataException($"{missing.Label} is required");

        Write(file.ToJsonString(WriteOptions));
    }

    private T? TryGet<T>(string key) where T : class =>
        Node(key) is JsonValue value && value.TryGetValue<T>(out var result) ? result : null;

    private T? TryGetStruct<T>(string key) where T : struct =>
        Node(key) is JsonValue value && value.TryGetValue<T>(out var result) ? result : null;

    private List<T>? GetList<T>(string key)
    {
        if (Node(key) is not JsonArray array) return null;
        var list = new List<T>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<T>(out var result)) return null;
            list.Add(result);
        }
        return list;
    }

    private JsonNode? Node(string key)
    {
        if (Read()[key] is { } stored) return stored;
        var fallback = fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))?.Default;
        return fallback is { } value ? JsonSerializer.SerializeToNode(value) : null;
    }

    private JsonObject Read()
    {
        var options = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
        if (!File.Exists(path)) return new JsonObject(options);
        return JsonNode.Parse(File.ReadAllText(path), options) as JsonObject
               ?? throw new InvalidDataException($"{path} is not a JSON object");
    }

    /// <summary>Replaces the file in one step, readable by the owner only (it holds credentials).</summary>
    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var writer = new StreamWriter(temp, options)) writer.Write(json);
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsBlankNode(JsonNode? node) =>
        node is null || PluginSettingField.IsBlank(JsonSerializer.SerializeToElement(node));
}
