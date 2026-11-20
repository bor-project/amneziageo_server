using System.Text.Json;
using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Converts stored traffic limits.
/// </summary>
internal static class TrafficLimitsData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TrafficLimits? Read(string? value) =>
        value is null ? null : JsonSerializer.Deserialize<TrafficLimits>(value, Json);

    public static string? Write(TrafficLimits? limits) =>
        limits is null ? null : JsonSerializer.Serialize(limits, Json);
}
