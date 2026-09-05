using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Rota.Desktop;

public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";
    private const string PreferenceFileName = "theme.txt";

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BackgroundBrush"] = "#090F1A",
        ["SurfaceBrush"] = "#0F1724",
        ["SurfaceRaisedBrush"] = "#121C2A",
        ["SidebarBrush"] = "#08111E",
        ["SidebarCardBrush"] = "#101B2B",
        ["NavyBrush"] = "#08111E",
        ["NavySoftBrush"] = "#101B2B",
        ["PrimaryBrush"] = "#6258F5",
        ["PrimaryTextBrush"] = "#8F88FF",
        ["PrimarySoftBrush"] = "#1C2340",
        ["TextBrush"] = "#F4F7FB",
        ["MutedBrush"] = "#9AA7B8",
        ["BorderBrush"] = "#243246",
        ["SuccessBrush"] = "#63D19E",
        ["SuccessSoftBrush"] = "#123427",
        ["ReviewBrush"] = "#63C7F2",
        ["ReviewSoftBrush"] = "#102F42",
        ["AssessmentBrush"] = "#F0B35B",
        ["AssessmentSoftBrush"] = "#3A2910",
        ["ErrorBrush"] = "#FF8D8D",
        ["ErrorSoftBrush"] = "#3A171B",
        ["SidebarTextBrush"] = "#F4F7FB",
        ["SidebarMutedBrush"] = "#98A7BA",
        ["CalendarOutsideBrush"] = "#0B1320",
        ["SubtleBrush"] = "#162131",
        ["NavSelectedBrush"] = "#172344",
        ["ControlHoverBrush"] = "#1A2638",
        ["ControlPressedBrush"] = "#202E43"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BackgroundBrush"] = "#F7F8FC",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceRaisedBrush"] = "#FFFFFF",
        ["SidebarBrush"] = "#FFFFFF",
        ["SidebarCardBrush"] = "#F7F8FC",
        ["NavyBrush"] = "#111A33",
        ["NavySoftBrush"] = "#F1F3F8",
        ["PrimaryBrush"] = "#5B56F5",
        ["PrimaryTextBrush"] = "#5B56F5",
        ["PrimarySoftBrush"] = "#EEF0FF",
        ["TextBrush"] = "#172033",
        ["MutedBrush"] = "#626B7E",
        ["BorderBrush"] = "#E3E7EF",
        ["SuccessBrush"] = "#11734B",
        ["SuccessSoftBrush"] = "#E7F7F0",
        ["ReviewBrush"] = "#1976A3",
        ["ReviewSoftBrush"] = "#E8F5FC",
        ["AssessmentBrush"] = "#A33457",
        ["AssessmentSoftBrush"] = "#FFEAF0",
        ["ErrorBrush"] = "#B42318",
        ["ErrorSoftBrush"] = "#FFF0F0",
        ["SidebarTextBrush"] = "#1F2A3D",
        ["SidebarMutedBrush"] = "#778095",
        ["CalendarOutsideBrush"] = "#F1F3F7",
        ["SubtleBrush"] = "#F1F3F8",
        ["NavSelectedBrush"] = "#EEF0FF",
        ["ControlHoverBrush"] = "#F0F2F8",
        ["ControlPressedBrush"] = "#E5E8F0"
    };

    public static string Normalize(string? theme) =>
        string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;

    public static string LoadPreference(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, PreferenceFileName);
            if (!File.Exists(path)) return Dark;
            return Normalize(File.ReadAllText(path, Encoding.UTF8).Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Dark;
        }
    }

    public static void SavePreference(string dataDirectory, string? theme)
    {
        var normalized = Normalize(theme);
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, PreferenceFileName);
        var temp = Path.Combine(dataDirectory, $".{PreferenceFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, normalized + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static void Apply(string? theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var palette = Normalize(theme) == Light ? LightPalette : DarkPalette;
        foreach (var pair in palette)
            resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }

    public static Brush ResourceBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return Brushes.Transparent;
    }
}
