using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests.Fakes;

internal sealed class FakeWindowsHardwareProbe : IWindowsHardwareProbe
{
    public FakeWindowsHardwareProbe(WindowsHardwareSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public WindowsHardwareSnapshot Snapshot { get; set; }
    public int CallCount { get; private set; }

    public Task<WindowsHardwareSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(Snapshot);
    }
}
