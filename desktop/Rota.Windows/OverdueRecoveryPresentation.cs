using System.Globalization;
using System.Text;

namespace Rota.Desktop;

public static class OverdueRecoveryPresentation
{
    public const int MaximumDetailedItems = 20;
    public const int MaximumRequestLength = 4_000;

    public static string BuildAiRequest(OverdueStudySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasOverdue)
            throw new InvalidOperationException("Não há blocos atrasados para preparar a recuperação.");

        var ptBr = CultureInfo.GetCultureInfo("pt-BR");
        var builder = new StringBuilder();
        builder.AppendLine(
            $"Prepare uma proposta segura para recuperar {snapshot.TotalCount} " +
            $"{(snapshot.TotalCount == 1 ? "bloco atrasado" : "blocos atrasados")} " +
            $"({snapshot.TotalMinutes} minutos no total).");
        builder.AppendLine(
            "Use somente datas futuras e respeite meus dias, limite diário, prazo e revisões protegidas. " +
            "Não altere histórico concluído nem aplique nada sem prévia e confirmação.");
        builder.AppendLine("Conteúdos atrasados:");

        foreach (var item in snapshot.Items.Take(MaximumDetailedItems))
        {
            var protectedLabel = item.IsRuntimeProtected ? " · revisão automática protegida" : "";
            builder.AppendLine(
                $"- {item.PlannedDate.ToString("dd/MM/yyyy", ptBr)} · {item.Subject} · " +
                $"{item.Topic} · {item.Minutes} min{protectedLabel}");
        }

        if (snapshot.TotalCount > MaximumDetailedItems)
            builder.AppendLine($"- e mais {snapshot.TotalCount - MaximumDetailedItems} blocos não detalhados aqui.");

        var request = builder.ToString().Trim();
        return request.Length <= MaximumRequestLength
            ? request
            : request[..MaximumRequestLength].TrimEnd();
    }
}
