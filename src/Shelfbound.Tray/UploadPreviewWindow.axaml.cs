using Avalonia.Controls;
using Shelfbound.Client;

namespace Shelfbound.Tray;

/// <summary>Confirms one prepared upload without rebuilding or re-serializing it.</summary>
public partial class UploadPreviewWindow : Window
{
    private PreparedSync? _prepared;
    private Func<PreparedSync, int, PreparedSync>? _unskipPrivateGame;

    // Avalonia runtime loader/designer only; the app uses the prepared-upload constructor below.
    public UploadPreviewWindow()
    {
        InitializeComponent();
        ConfirmButton.Click += (_, _) => Close(_prepared);
        CancelButton.Click += (_, _) => Close(null);
    }

    internal UploadPreviewWindow(
        PreparedSync prepared,
        Func<PreparedSync, int, PreparedSync>? unskipPrivateGame = null) : this()
    {
        ArgumentNullException.ThrowIfNull(prepared);
        _prepared = prepared;
        _unskipPrivateGame = unskipPrivateGame;
        RenderPreparedSync();
    }

    private void RenderPreparedSync()
    {
        PreparedSync prepared = _prepared
            ?? throw new InvalidOperationException("No prepared upload is available.");

        var snapshot = prepared.Upload.Snapshot;
        SummaryText.Text = $"{snapshot.Device.Name} · {snapshot.Games.Count} games " +
            $"({snapshot.Stats.InstalledGameCount} installed) · {snapshot.Libraries.Count} libraries · " +
            $"{snapshot.Categories.Count} categories";
        UploadJson.Text = prepared.Upload.Json;

        PrivateExclusionBorder.IsVisible = prepared.PrivateGameStatus.Enabled;
        PrivateExclusionStatusText.Text = prepared.PrivateGameStatus.Message;
        SkippedGamesPanel.Children.Clear();
        foreach (SkippedPrivateGame game in prepared.SkippedGames)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = new TextBlock
            {
                Text = game.Name,
                Foreground = Avalonia.Media.Brushes.White,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var unskip = new Button
            {
                Content = "Sync this game",
                IsEnabled = _unskipPrivateGame is not null,
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(unskip, 1);
            unskip.Click += (_, _) => Unskip(game.AppId);
            row.Children.Add(title);
            row.Children.Add(unskip);
            SkippedGamesPanel.Children.Add(row);
        }

        WarningsBorder.IsVisible = prepared.Warnings.Count > 0;
        if (prepared.Warnings.Count > 0)
        {
            WarningsBorder.IsVisible = true;
            IEnumerable<string> shown = prepared.Warnings.Take(5).Select(value => $"• {value}");
            if (prepared.Warnings.Count > 5)
                shown = shown.Append($"• … and {prepared.Warnings.Count - 5} more scan warnings");
            WarningsText.Text = string.Join(Environment.NewLine, shown);
        }
    }

    private void Unskip(int appId)
    {
        if (_prepared is null || _unskipPrivateGame is null)
            return;

        try
        {
            _prepared = _unskipPrivateGame(_prepared, appId);
            RenderPreparedSync();
        }
        catch (Exception ex)
        {
            WarningsBorder.IsVisible = true;
            WarningsText.Text = $"Could not save the device-local override: {ex.Message}";
        }
    }
}
