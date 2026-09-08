using System.Text;

namespace Rota.Desktop.LocalAI;

/// <summary>
/// Prepara uma cópia manual e limitada do histórico visível. O usuário precisa
/// acionar esse fluxo; nada aqui envia a conversa ao modelo ou para a rede.
/// </summary>
public sealed record AiTeacherLessonHistoryClipboardContent
{
    public string Text { get; init; } = "";
    public bool WasTruncated { get; init; }
}

public static class AiTeacherLessonHistoryClipboardText
{
    public const int MaximumClipboardCharacters = 16_000;
    private const string TruncationNotice = "\n\n[Histórico limitado para cópia.]";

    public static AiTeacherLessonHistoryClipboardContent Create(AiTeacherLessonHistorySnapshot history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var builder = new StringBuilder();
        foreach (var entry in history.Entries)
        {
            if (entry is null) continue;
            if (builder.Length > 0) builder.AppendLine().AppendLine();
            builder.AppendLine(entry.StatusLabel);
            builder.AppendLine(entry.TimestampLabel);
            builder.Append("Pergunta: ").AppendLine(entry.Question);
            if (!string.IsNullOrWhiteSpace(entry.AnswerTitle)) builder.AppendLine(entry.AnswerTitle);
            if (!string.IsNullOrWhiteSpace(entry.AnswerRecap)) builder.AppendLine(entry.AnswerRecap);
            if (!string.IsNullOrWhiteSpace(entry.EvidenceSummary)) builder.AppendLine(entry.EvidenceSummary);
        }

        var text = builder.ToString().TrimEnd();
        if (text.Length <= MaximumClipboardCharacters)
            return new AiTeacherLessonHistoryClipboardContent { Text = text };

        return new AiTeacherLessonHistoryClipboardContent
        {
            Text = text[..(MaximumClipboardCharacters - TruncationNotice.Length)] + TruncationNotice,
            WasTruncated = true
        };
    }
}
