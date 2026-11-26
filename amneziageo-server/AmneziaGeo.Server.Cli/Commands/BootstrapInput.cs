using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Reads the initial configuration and identifies invalid fields.
/// </summary>
public static class BootstrapInput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>
    /// Reads a configuration object.
    /// </summary>
    public static BootstrapSettings Read(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new BootstrapFault("$", "expected a configuration object");
        }

        Shape(document.RootElement, typeof(BootstrapSettings), "$", false);
        Required(document.RootElement, "user", "$", JsonValueKind.String);
        Required(document.RootElement, "passwordFile", "$", JsonValueKind.String);

        return JsonSerializer.Deserialize<BootstrapSettings>(text, Json)
            ?? throw new BootstrapFault("$", "expected a configuration object");
    }

    private static void Required(JsonElement value, string name, string path, JsonValueKind kind)
    {
        var found = value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found.Value.ValueKind != kind || (kind == JsonValueKind.String && string.IsNullOrWhiteSpace(found.Value.GetString())))
        {
            throw new BootstrapFault(path + "." + name, "required value is missing or empty");
        }
    }

    private static void Shape(JsonElement value, Type type, string path, bool element)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (element)
            {
                throw new BootstrapFault(path, "null entries are not allowed");
            }

            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var itemType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                Shape(item, itemType, path + "[" + index++ + "]", true);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            foreach (var item in value.EnumerateObject())
            {
                Shape(item.Value, type.GetGenericArguments()[1], path + "." + Name(item.Name), true);
            }

            return;
        }

        var properties = type.GetProperties().Where(p => p.SetMethod is not null).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            var at = path + "." + Name(property.Name);
            if (!seen.Add(property.Name))
            {
                throw new BootstrapFault(at, "field is specified more than once");
            }

            if (!properties.TryGetValue(property.Name, out var info))
            {
                throw new BootstrapFault(at, "unknown or read-only field");
            }

            if (info.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute)))
            {
                Required(value, property.Name, path, info.PropertyType == typeof(string) ? JsonValueKind.String : JsonValueKind.Object);
            }

            Shape(property.Value, info.PropertyType, at, false);
        }

        foreach (var property in properties.Values.Where(p => p.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute))))
        {
            Required(value, property.Name, path, property.PropertyType == typeof(string) ? JsonValueKind.String : JsonValueKind.Object);
        }
    }

    private static string Name(string value) => value.Length <= 80 && Regex.IsMatch(value, "^[a-zA-Z_][a-zA-Z0-9_-]*$") ? value : "[field]";
}

/// <summary>
/// Identifies a rejected installation field.
/// </summary>
public sealed class BootstrapFault : Exception
{
    /// <summary>
    /// ctor
    /// </summary>
    public BootstrapFault(string field, string reason, Exception? inner = null)
        : base(reason, inner)
    {
        Field = field;
        Reason = reason;
    }

    public string Field { get; }

    public string Reason { get; }
}
