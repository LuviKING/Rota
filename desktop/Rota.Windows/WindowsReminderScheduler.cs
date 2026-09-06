using System.Diagnostics;
using System.Globalization;

namespace Rota.Desktop;

public sealed record StudyReminderConfiguration(bool Enabled, TimeOnly Time)
{
    public const string DefaultTime = "19:00";

    public static StudyReminderConfiguration Parse(bool enabled, string? time)
    {
        var clean = (time ?? "").Trim();
        if (!TimeOnly.TryParseExact(clean, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Use um horário válido no formato HH:mm.", nameof(time));
        return new StudyReminderConfiguration(enabled, parsed);
    }

    public string TimeText => Time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public interface IStudyReminderScheduler
{
    void Apply(StudyReminderConfiguration configuration, string executablePath);
}

public interface IReminderTaskRunner
{
    ReminderTaskResult Run(IReadOnlyList<string> arguments);
}

public sealed record ReminderTaskResult(int ExitCode, string Output);

public sealed class WindowsStudyReminderScheduler : IStudyReminderScheduler
{
    internal const string TaskName = "Rota - Lembrete de estudos";
    private readonly IReminderTaskRunner _runner;

    public WindowsStudyReminderScheduler(IReminderTaskRunner? runner = null) =>
        _runner = runner ?? new SchtasksReminderTaskRunner();

    public void Apply(StudyReminderConfiguration configuration, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("O executável do Rota é inválido.", nameof(executablePath));
        var fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("O executável do Rota é inválido.", nameof(executablePath));

        if (!configuration.Enabled)
        {
            var query = _runner.Run(new[] { "/Query", "/TN", TaskName });
            if (query.ExitCode != 0) return;
            EnsureSuccess(_runner.Run(new[] { "/Delete", "/TN", TaskName, "/F" }), "remover");
            return;
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("O executável do Rota não foi encontrado para criar o lembrete.", fullPath);

        var action = $"\"{fullPath}\" --reminder";
        var result = _runner.Run(new[]
        {
            "/Create", "/TN", TaskName, "/TR", action,
            "/SC", "DAILY", "/ST", configuration.TimeText, "/F"
        });
        EnsureSuccess(result, "criar");
    }

    private static void EnsureSuccess(ReminderTaskResult result, string action)
    {
        if (result.ExitCode == 0) return;
        var detail = string.IsNullOrWhiteSpace(result.Output) ? "O Agendador de Tarefas não informou detalhes." : result.Output.Trim();
        if (detail.Length > 500) detail = detail[..500];
        throw new InvalidOperationException($"Não foi possível {action} o lembrete do Windows. {detail}");
    }
}

public sealed class SchtasksReminderTaskRunner : IReminderTaskRunner
{
    public ReminderTaskResult Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var taskScheduler = Path.Combine(systemDirectory, "schtasks.exe");
        var startInfo = new ProcessStartInfo(taskScheduler)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("O Agendador de Tarefas do Windows não pôde ser iniciado.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("O Agendador de Tarefas do Windows demorou demais para responder.");
        }
        Task.WaitAll(outputTask, errorTask);
        var output = outputTask.Result;
        var error = errorTask.Result;
        return new ReminderTaskResult(process.ExitCode, string.Join(Environment.NewLine, output, error).Trim());
    }
}
