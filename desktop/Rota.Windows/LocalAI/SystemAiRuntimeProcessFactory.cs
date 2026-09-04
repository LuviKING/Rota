using System.Diagnostics;
using System.Text;

namespace Rota.Desktop.LocalAI;

public sealed class SystemAiRuntimeProcessFactory : IAiRuntimeProcessFactory
{
    public IAiRuntimeProcess Start(AiRuntimeLaunchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.FileName) || !Path.IsPathFullyQualified(command.FileName) ||
            string.IsNullOrWhiteSpace(command.WorkingDirectory) || !Path.IsPathFullyQualified(command.WorkingDirectory) ||
            command.Arguments is null || command.Arguments.Any(argument => argument is null))
        {
            throw new AiRuntimeException("O comando do runtime local possui caminhos inválidos.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(command.FileName),
            WorkingDirectory = Path.GetFullPath(command.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);

        // Ambient llama.cpp variables must not be able to widen the local server configuration.
        foreach (var key in startInfo.Environment.Keys
                     .Where(key => key.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("LLAMA_API_KEY", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            startInfo.Environment.Remove(key);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var started = false;
        try
        {
            var owned = new SystemAiRuntimeProcess(process);
            if (!process.Start())
            {
                owned.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new AiRuntimeException("O Windows não iniciou o runtime local da IA.");
            }
            started = true;
            owned.BeginReadingOutput();
            return owned;
        }
        catch (AiRuntimeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            }
            process.Dispose();
            throw new AiRuntimeException("O Windows não conseguiu iniciar o runtime local da IA.", ex);
        }
    }

    private sealed class SystemAiRuntimeProcess : IAiRuntimeProcess
    {
        private const int MaximumCapturedCharacters = 16 * 1024;
        private readonly Process _process;
        private readonly object _outputSync = new();
        private readonly StringBuilder _output = new();

        public SystemAiRuntimeProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += CaptureOutput;
            _process.ErrorDataReceived += CaptureOutput;
        }

        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        public string RecentOutput
        {
            get
            {
                lock (_outputSync) return _output.ToString();
            }
        }

        public void BeginReadingOutput()
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        private void CaptureOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null) return;
            lock (_outputSync)
            {
                _output.AppendLine(eventArgs.Data);
                if (_output.Length > MaximumCapturedCharacters)
                    _output.Remove(0, _output.Length - MaximumCapturedCharacters);
            }
        }

        public ValueTask DisposeAsync()
        {
            _process.OutputDataReceived -= CaptureOutput;
            _process.ErrorDataReceived -= CaptureOutput;
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
