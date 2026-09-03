using System.Windows;

namespace Rota.Desktop;

internal static class WindowSizing
{
    public static void FitToWorkArea(Window window, double margin = 24)
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(window.MinWidth, workArea.Width - margin);
        var availableHeight = Math.Max(window.MinHeight, workArea.Height - margin);
        window.Width = Math.Min(window.Width, availableWidth);
        window.Height = Math.Min(window.Height, availableHeight);
    }
}

