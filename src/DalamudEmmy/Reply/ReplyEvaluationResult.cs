using DalamudEmmy.Conversation;

namespace DalamudEmmy.Reply;

public sealed class ReplyEvaluationResult
{
    public ReplyDecision Decision { get; init; }

    public int Score { get; init; }

    public bool WasAiEvaluated { get; init; }

    public string Reason { get; init; } = string.Empty;

    public ConversationAnalysisResult ConversationAnalysis { get; init; } = new();
}
