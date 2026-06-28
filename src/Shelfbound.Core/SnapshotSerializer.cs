using System.Text.Json;
using System.Text.Json.Serialization;
using Shelfbound.Core.Model;

namespace Shelfbound.Core;

/// <summary>
/// Serializes and deserializes <see cref="SnapshotDocument"/> using the canonical Shelfbound JSON
/// conventions: camelCase property names, camelCase string enums, ISO-8601 timestamps, nulls omitted.
/// </summary>
public static class SnapshotSerializer
{
    private static JsonSerializerOptions CreateOptions(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonSerializerOptions Compact = CreateOptions(indented: false);
    private static readonly JsonSerializerOptions Pretty = CreateOptions(indented: true);

    public static string Serialize(SnapshotDocument snapshot, bool indented = true) =>
        JsonSerializer.Serialize(snapshot, indented ? Pretty : Compact);

    public static SnapshotDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<SnapshotDocument>(json, Compact)
            ?? throw new InvalidDataException("Snapshot JSON deserialized to null.");
}
