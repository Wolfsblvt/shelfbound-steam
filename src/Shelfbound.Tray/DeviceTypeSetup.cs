using Shelfbound.Core.Model;
using Shelfbound.Steam.Steam;

namespace Shelfbound.Tray;

/// <summary>
/// Defines the explicit device-type choice required before this tray can use hosted features.
/// Keeping the selectable values, labels, and completion predicate together prevents the setup
/// UI and the non-UI sync boundary from drifting apart.
/// </summary>
public static class DeviceTypeSetup
{
    public static IReadOnlyList<DeviceTypeChoice> Choices { get; } =
    [
        new(DeviceType.Desktop, "Desktop"),
        new(DeviceType.Laptop, "Laptop"),
        new(DeviceType.SteamDeck, "Steam Deck"),
        new(DeviceType.Unknown, "Other / not sure"),
    ];

    public static bool IsComplete(DeviceType? type) => type is
        DeviceType.Desktop or DeviceType.Laptop or DeviceType.SteamDeck or DeviceType.Unknown;

    public static DeviceType? GetSuggestion() => DeviceIdentity.SuggestType();

    /// <summary>
    /// Chooses the initially highlighted option without turning a suggestion into completed setup.
    /// Only Steam Deck has a conservative automatic suggestion; other device types remain unselected.
    /// </summary>
    public static DeviceType? GetInitialSelection(DeviceType? savedType, DeviceType? suggestedType) =>
        IsComplete(savedType)
            ? savedType
            : suggestedType == DeviceType.SteamDeck ? DeviceType.SteamDeck : null;

    public static string LabelFor(DeviceType type) =>
        Choices.First(choice => choice.Type == type).Label;
}

/// <summary>A selectable device type and the user-facing label shown wherever it is edited.</summary>
public sealed record DeviceTypeChoice(DeviceType Type, string Label)
{
    public override string ToString() => Label;
}
