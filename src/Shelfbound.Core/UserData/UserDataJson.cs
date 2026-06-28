using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfbound.Core.UserData;

/// <summary>
/// Canonical serialization for <see cref="UserProfile"/> documents. Centralized so every store — the
/// local JSON file store and the hosted database (jsonb) store — writes and reads the same shape, and
/// a profile stays portable between them. Naming/enum/null handling are the semantic contract; only
/// indentation differs per use (readable on disk, compact in the database).
/// </summary>
public static class UserDataJson
{
    /// <summary>Compact options — used for database (jsonb) storage and transport.</summary>
    public static JsonSerializerOptions Default { get; } = Create(indented: false);

    /// <summary>Builds options with the canonical naming/enum/null policy; toggle indentation per use.</summary>
    public static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(UserProfile profile) => JsonSerializer.Serialize(profile, Default);

    public static UserProfile Deserialize(string json) =>
        JsonSerializer.Deserialize<UserProfile>(json, Default)
        ?? throw new JsonException("User profile JSON deserialized to null.");
}
