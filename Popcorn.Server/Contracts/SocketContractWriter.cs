using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;

namespace Popcorn.Server.Contracts;

/// <summary>
/// Writes the web UI's socket contract from this assembly (<c>Popcorn.Server --write-socket-contracts &lt;dir&gt;</c>,
/// run after every build of the server, see the csproj): <c>socket-requests.ts</c> = every <see cref="SocketRequests"/>
/// constant, <c>socket-payloads.ts</c> = the payload type of each <see cref="WebSocketEventAttribute"/> handler and the
/// TypeScript interfaces / enums of those types. Doc comments come along from the XML documentation file. Files are
/// only rewritten when their content changes (no needless Vite reloads).
/// </summary>
public static class SocketContractWriter
{
    private const string Header = "// Generated from Popcorn.Server (Contracts/SocketContractWriter.cs) on build. Do not edit.\n";

    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var assembly = typeof(SocketContractWriter).Assembly;
        var docs = XmlDocs.Load(assembly);

        WriteIfChanged(Path.Combine(directory, "socket-requests.ts"), Requests(docs));
        WriteIfChanged(Path.Combine(directory, "socket-payloads.ts"), new PayloadWriter(docs).Write(Handlers(assembly)));
    }

    private static string Requests(XmlDocs docs)
    {
        var sb = new StringBuilder(Header).Append('\n');
        sb.Append("/** Socket event names (server: Popcorn.Server/Models/SocketRequests.cs). */\n");
        sb.Append("export enum SocketRequests {\n");
        foreach (var field in typeof(SocketRequests).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
                     .OrderBy(f => f.MetadataToken))
        {
            AppendDoc(sb, docs.Of(field), "  ");
            sb.Append($"  {field.Name} = '{field.GetRawConstantValue()}',\n");
        }
        return sb.Append("}\n").ToString();
    }

    /// <summary>Event name → payload type (null = no payload) of every text handler, in event order.</summary>
    private static List<(string Event, Type? Payload)> Handlers(Assembly assembly)
    {
        var order = typeof(SocketRequests).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.MetadataToken);
        return assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<WebSocketServiceAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributes<WebSocketEventAttribute>()
                .Select(a => (Event: a.EventType, Payload: m.GetParameters().FirstOrDefault()?.ParameterType)))
            .DistinctBy(h => h.Event)
            .OrderBy(h => order.GetValueOrDefault(h.Event, int.MaxValue))
            .ToList();
    }

    private static void AppendDoc(StringBuilder sb, string? doc, string indent)
    {
        if (string.IsNullOrWhiteSpace(doc)) return;
        var lines = doc.Split('\n');
        if (lines.Length == 1)
        {
            sb.Append($"{indent}/** {lines[0]} */\n");
            return;
        }
        sb.Append($"{indent}/**\n");
        foreach (var line in lines) sb.Append($"{indent} * {line}".TrimEnd()).Append('\n');
        sb.Append($"{indent} */\n");
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        File.WriteAllText(path, content);
        Console.WriteLine($"Wrote {path}");
    }

    private sealed class PayloadWriter(XmlDocs docs)
    {
        private readonly NullabilityInfoContext _nullability = new();
        /// <summary>Types to declare, by TypeScript name (filled while mapping, so nested types come along).</summary>
        private readonly Dictionary<Type, string> _names = new();
        private readonly Queue<Type> _pending = new();

        public string Write(List<(string Event, Type? Payload)> handlers)
        {
            var map = new StringBuilder();
            map.Append("/** Payload the server's handler of each event takes (`sendEvent`). */\n");
            map.Append("export interface SocketPayloads {\n");
            foreach (var (eventName, payload) in handlers)
                map.Append($"  [SocketRequests.{EnumMember(eventName)}]: {(payload is null ? "void" : Map(payload, nullable: false))};\n");
            map.Append("}\n");

            var declarations = new StringBuilder();
            while (_pending.TryDequeue(out var type)) Declare(declarations, type);

            return new StringBuilder(Header)
                .Append("import { SocketRequests } from './socket-requests';\n\n")
                .Append(declarations)
                .Append(map)
                .ToString();
        }

        private static string EnumMember(string eventName) =>
            typeof(SocketRequests).GetFields(BindingFlags.Public | BindingFlags.Static)
                .First(f => f.IsLiteral && (string)f.GetRawConstantValue()! == eventName).Name;

        private string Map(Type type, bool nullable)
        {
            var ts = MapNonNull(Nullable.GetUnderlyingType(type) ?? type);
            return nullable || Nullable.GetUnderlyingType(type) is not null ? $"{ts} | null" : ts;
        }

        private string MapNonNull(Type type)
        {
            if (type == typeof(string) || type == typeof(Guid) || type == typeof(char) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
                return "string";
            if (type == typeof(bool)) return "boolean";
            if (type.IsPrimitive || type == typeof(decimal)) return "number";
            if (type == typeof(object) || type.FullName == "System.Text.Json.JsonElement") return "unknown";

            if (type.IsArray) return $"{Element(type.GetElementType()!)}[]";
            if (type.IsGenericType && DictionaryValue(type) is { } value) return $"Record<string, {Map(value, false)}>";
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
                return $"{Element(type.GetGenericArguments()[0])}[]";

            return NameOf(type);
        }

        private string Element(Type type)
        {
            var mapped = Map(type, false);
            return mapped.Contains(' ') ? $"({mapped})" : mapped;
        }

        private static Type? DictionaryValue(Type type)
        {
            var dictionary = type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() is var d
                                     && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)));
            return dictionary?.GetGenericArguments()[1];
        }

        private string NameOf(Type type)
        {
            if (_names.TryGetValue(type, out var name)) return name;
            name = type.Name;
            // Two types of the same name (other namespaces): the second gets its namespace's last part in front.
            if (_names.ContainsValue(name)) name = type.Namespace!.Split('.')[^1] + name;
            _names[type] = name;
            _pending.Enqueue(type);
            return name;
        }

        private void Declare(StringBuilder sb, Type type)
        {
            AppendDoc(sb, docs.Of(type), "");
            var name = _names[type];
            if (type.IsEnum)
            {
                // The socket's JSON has no global enum converter: enums with a converter attribute (JsonStringEnumConverter
                // or their own, all writing names) are names, the rest their numbers.
                if (type.GetCustomAttribute<JsonConverterAttribute>() is not null)
                {
                    var names = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => $"'{f.Name}'");
                    sb.Append($"export type {name} = {string.Join(" | ", names)};\n\n");
                    return;
                }
                sb.Append($"export enum {name} {{\n");
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    AppendDoc(sb, docs.Of(field), "  ");
                    sb.Append($"  {field.Name} = {Convert.ToInt64(field.GetRawConstantValue())},\n");
                }
                sb.Append("}\n\n");
                return;
            }

            sb.Append($"export interface {name} {{\n");
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         // Payloads are what the client sends: only properties the server can set.
                         .Where(p => p.GetMethod is not null && p.SetMethod is not null && p.GetIndexParameters().Length == 0
                                     && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
                         .OrderBy(p => p.MetadataToken))
            {
                var nullable = !property.PropertyType.IsValueType
                    && _nullability.Create(property).ReadState == NullabilityState.Nullable;
                var optional = nullable || Nullable.GetUnderlyingType(property.PropertyType) is not null
                               || (property.PropertyType.IsValueType && HasDefault(type, property));
                var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? CamelCase(property.Name);
                AppendDoc(sb, docs.Of(property), "  ");
                sb.Append($"  {jsonName}{(optional ? "?" : "")}: {Map(property.PropertyType, nullable)};\n");
            }
            sb.Append("}\n\n");
        }

        /// <summary>
        /// A value the client may leave out: no <c>required</c>, and an initializer (parameterless constructor, or an
        /// init/set property next to the constructor) or a default constructor argument. Reference types stay required:
        /// left out they arrive as null.
        /// </summary>
        private static bool HasDefault(Type type, PropertyInfo property)
        {
            if (property.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute")) return false;
            var constructors = type.GetConstructors();
            if (constructors.Any(c => c.GetParameters().Length == 0)) return true;
            var parameter = constructors.SelectMany(c => c.GetParameters())
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            return parameter is null || parameter.HasDefaultValue;
        }

        private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Summaries (and record <c>param</c>s) of the XML documentation files next to the assemblies.</summary>
    private sealed class XmlDocs
    {
        private readonly Dictionary<string, string> _members = new();

        public static XmlDocs Load(Assembly assembly)
        {
            var docs = new XmlDocs();
            var dir = Path.GetDirectoryName(assembly.Location)!;
            foreach (var file in Directory.EnumerateFiles(dir, "Popcorn.*.xml"))
            {
                try
                {
                    foreach (var member in XDocument.Load(file).Descendants("member"))
                    {
                        var key = member.Attribute("name")?.Value;
                        if (key is null) continue;
                        if (Text(member.Element("summary")) is { } summary) docs._members[key] = summary;
                        // Positional records: <param> of the type documents its properties.
                        if (key.StartsWith("T:"))
                        {
                            foreach (var param in member.Elements("param"))
                            {
                                if (param.Attribute("name")?.Value is { } p && Text(param) is { } text)
                                    docs._members.TryAdd($"P:{key[2..]}.{p}", text);
                            }
                        }
                    }
                }
                catch (Exception e) when (e is IOException or System.Xml.XmlException)
                {
                    // no docs from that file
                }
            }
            return docs;
        }

        private static string? Text(XElement? element)
        {
            if (element is null) return null;
            var text = string.Concat(element.Nodes().Select(n => n switch
            {
                XText t => t.Value,
                XElement { Name.LocalName: "see" or "paramref" or "typeparamref" } e =>
                    $"`{(e.Attribute("cref")?.Value ?? e.Attribute("name")?.Value ?? e.Attribute("langword")?.Value ?? "").Split(':')[^1].Split('.')[^1]}`",
                XElement e => e.Value,
                _ => "",
            }));
            var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lines.Count == 0 ? null : string.Join("\n", lines).Replace("*/", "*\\/");
        }

        public string? Of(MemberInfo member)
        {
            var key = member switch
            {
                Type t => $"T:{t.FullName}",
                FieldInfo f => $"F:{f.DeclaringType!.FullName}.{f.Name}",
                PropertyInfo p => $"P:{p.DeclaringType!.FullName}.{p.Name}",
                _ => "",
            };
            return _members.GetValueOrDefault(key.Replace('+', '.'));
        }
    }
}
