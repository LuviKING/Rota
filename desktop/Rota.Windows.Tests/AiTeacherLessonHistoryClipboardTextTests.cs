using Rota.Desktop.LocalAI;

namespace Rota.Desktop.Tests;

public static class AiTeacherLessonHistoryClipboardTextTests
{
    public static IEnumerable<(string Name, Action Body)> Cases => new (string, Action)[]
    {
        ("Teacher history copy text keeps the visible local fields", FormatsLocalHistory),
        ("Teacher history copy text has a strict size bound", BoundsCopiedHistory)
    };

    private static void FormatsLocalHistory()
    {
        var content = AiTeacherLessonHistoryClipboardText.Create(History("Pergunta local.", "Resposta local."));
        Require(!content.WasTruncated);
        Require(content.Text.Contains("Respondida", StringComparison.Ordinal));
        Require(content.Text.Contains("Pergunta: Pergunta local.", StringComparison.Ordinal));
        Require(content.Text.Contains("Resposta local.", StringComparison.Ordinal));
        Require(content.Text.Contains("Base: Material interno suficiente · Cobertura: Alta", StringComparison.Ordinal));
    }

    private static void BoundsCopiedHistory()
    {
        var content = AiTeacherLessonHistoryClipboardText.Create(History(new string('x', AiTeacherLessonHistoryClipboardText.MaximumClipboardCharacters + 100), "recap"));
        Require(content.WasTruncated);
        Require(content.Text.Length == AiTeacherLessonHistoryClipboardText.MaximumClipboardCharacters);
        Require(content.Text.EndsWith("[Histórico limitado para cópia.]", StringComparison.Ordinal));
    }

    private static AiTeacherLessonHistorySnapshot History(string question, string recap) => new()
    {
        ConversationId = Guid.NewGuid(),
        TotalExchangeCount = 1,
        Entries = new[]
        {
            new AiTeacherLessonHistoryEntry
            {
                ExchangeId = Guid.NewGuid(),
                StatusLabel = "Respondida",
                TimestampLabel = "Atualizada em 01/01/1970 00:00 UTC",
                Question = question,
                AnswerTitle = "Explicação local",
                AnswerRecap = recap,
                EvidenceSummary = "Base: Material interno suficiente · Cobertura: Alta"
            }
        }
    };

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Teacher history clipboard text assertion failed.");
    }
}
