using Shelfbound.Storage.UserData;

namespace Shelfbound.Storage;

/// <summary>
/// Persistence for user/derived data, keyed by owner id. The owner id comes from the identity seam
/// (local: the machine's profile; hosted: the authenticated account), so swapping in real auth later
/// does not touch this contract or the data model.
/// </summary>
public interface IUserDataStore
{
    /// <summary>Loads the owner's profile, creating an empty one if none exists.</summary>
    UserProfile Load(string ownerId);

    /// <summary>Persists the profile atomically.</summary>
    void Save(UserProfile profile);

    /// <summary>Atomically load → mutate → save, returning a result. Use this for all writes.</summary>
    T Update<T>(string ownerId, Func<UserProfile, T> mutate);
}
