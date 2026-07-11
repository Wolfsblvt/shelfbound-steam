using Avalonia.Controls;

namespace Shelfbound.Tray;

/// <summary>Confirms one prepared upload without rebuilding or re-serializing it.</summary>
public partial class UploadPreviewWindow : Window
{
    // Avalonia runtime loader/designer only; the app uses the prepared-upload constructor below.
    public UploadPreviewWindow()
    {
        InitializeComponent();
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    public UploadPreviewWindow(PreparedSync prepared) : this()
    {
        ArgumentNullException.ThrowIfNull(prepared);

        var snapshot = prepared.Upload.Snapshot;
        SummaryText.Text = $"{snapshot.Device.Name} · {snapshot.Games.Count} games " +
            $"({snapshot.Stats.InstalledGameCount} installed) · {snapshot.Libraries.Count} libraries · " +
            $"{snapshot.Categories.Count} categories";
        UploadJson.Text = prepared.Upload.Json;

        if (prepared.Warnings.Count > 0)
        {
            WarningsBorder.IsVisible = true;
            IEnumerable<string> shown = prepared.Warnings.Take(5).Select(value => $"• {value}");
            if (prepared.Warnings.Count > 5)
                shown = shown.Append($"• … and {prepared.Warnings.Count - 5} more scan warnings");
            WarningsText.Text = string.Join(Environment.NewLine, shown);
        }
    }
}
