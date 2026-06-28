namespace Shelfbound.Steam.Vdf;

/// <summary>
/// A parsed VDF/KeyValues object. Children are split into scalar values and nested objects.
/// Key lookups are case-insensitive, matching Steam's behaviour. Last value wins on duplicate keys.
/// </summary>
public sealed class VdfObject
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VdfObject> _objects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => _values;
    public IReadOnlyDictionary<string, VdfObject> Objects => _objects;

    public string? GetValue(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public VdfObject? GetObject(string key) => _objects.TryGetValue(key, out var o) ? o : null;

    internal void SetValue(string key, string value) => _values[key] = value;
    internal void SetObject(string key, VdfObject value) => _objects[key] = value;
}
