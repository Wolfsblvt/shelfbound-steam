using Shelfbound.Core.Model;
using Shelfbound.Steam.Vdf;

namespace Shelfbound.Steam.Steam;

/// <summary>Parses config/loginusers.vdf into the known Steam accounts.</summary>
public static class LoginUsersParser
{
    public static IReadOnlyList<SteamAccount> Parse(string vdfText)
    {
        return Parse(VdfParser.Parse(vdfText));
    }

    public static IReadOnlyList<SteamAccount> ParseFile(string path) => Parse(VdfParser.ParseFile(path));

    private static IReadOnlyList<SteamAccount> Parse(VdfObject root)
    {
        var users = root.GetObject("users");
        if (users is null)
            return [];

        var accounts = new List<SteamAccount>();
        foreach (var (steamId, user) in users.Objects)
        {
            accounts.Add(new SteamAccount
            {
                SteamId64 = steamId,
                AccountName = user.GetValue("AccountName"),
                PersonaName = user.GetValue("PersonaName"),
                MostRecent = user.GetValue("MostRecent") == "1",
            });
        }
        return accounts;
    }
}
