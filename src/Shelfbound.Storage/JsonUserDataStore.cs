using System.Text.Json;
using Shelfbound.Core.UserData;

namespace Shelfbound.Storage;

/// <summary>
/// Local JSON-file user-data store: one file per owner under the Shelfbound config directory. Writes
/// are atomic (temp file + move) and guarded by an in-process lock. This is the local implementation
/// of <see cref="IUserDataStore"/>; the hosted layer provides a database-backed store over the same
/// <see cref="UserProfile"/> model and <see cref="UserDataJson"/> format. (A SQLite backend is the
/// planned local upgrade if concurrency/query needs grow.)
/// </summary>
public sealed class JsonUserDataStore : IUserDataStore
{
    // Indented for human-readable on-disk files; same canonical naming/enum/null policy as everywhere.
    private static readonly JsonSerializerOptions Options = UserDataJson.Create(indented: true);

    private readonly Lock _sync = new();
    private readonly string _profilesDirectory;

    public JsonUserDataStore(string profilesDirectory) => _profilesDirectory = profilesDirectory;

    public UserProfile Load(string ownerId)
    {
        lock (_sync)
        {
            return LoadUnlocked(ownerId);
        }
    }

    public void Save(UserProfile profile)
    {
        lock (_sync)
        {
            SaveUnlocked(profile);
        }
    }

    public T Update<T>(string ownerId, Func<UserProfile, T> mutate)
    {
        lock (_sync)
        {
            UserProfile profile = LoadUnlocked(ownerId);
            T result = mutate(profile);
            SaveUnlocked(profile);
            return result;
        }
    }

    private UserProfile LoadUnlocked(string ownerId)
    {
        string file = FileFor(ownerId);
        if (File.Exists(file) &&
            JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(file), Options) is { } loaded)
        {
            return loaded;
        }

        var now = DateTimeOffset.UtcNow;
        return new UserProfile { OwnerId = ownerId, CreatedAt = now, UpdatedAt = now };
    }

    private void SaveUnlocked(UserProfile profile)
    {
        UserProfile stamped = profile with { UpdatedAt = DateTimeOffset.UtcNow };
        string file = FileFor(stamped.OwnerId);
        PrivateFile.WriteAllTextAtomically(file, JsonSerializer.Serialize(stamped, Options));
    }

    private string FileFor(string ownerId)
    {
        string safe = string.Concat(ownerId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(_profilesDirectory, safe, "userdata.json");
    }
}
