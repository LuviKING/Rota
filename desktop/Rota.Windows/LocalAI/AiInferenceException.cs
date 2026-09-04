namespace Rota.Desktop.LocalAI;

public sealed class AiInferenceException : Exception
{
    public AiInferenceException(string message) : base(message)
    {
    }

    public AiInferenceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
