namespace DalamudEmmy.Conversation;

public sealed class ConversationAnalysisResult
{
    public ConversationType ConversationType { get; init; } = ConversationType.Unclear;

    public IReadOnlyList<string> LikelyAddressees { get; init; } = [];

    public IReadOnlyList<string> ActiveParticipants { get; init; } = [];

    public int ParticipantCount { get; init; }

    public int IsEmmyAddressedScore { get; init; }

    public int OtherTargetClarityScore { get; init; }

    public int GroupChatStrength { get; init; }

    public int ReplyExpectationScore { get; init; }

    public int ThreadParticipationScore { get; init; }

    public string EmmyInvolvementLevel { get; init; } = "low";

    public string ThreadSummary { get; init; } = string.Empty;
}
