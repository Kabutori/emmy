using Dalamud.Game.Text;
using DalamudEmmy.Conversation;
using DalamudEmmy.Reply;

namespace DalamudEmmy.Chat;

public sealed class CapturedChatMessage
{
    public CapturedChatMessage(DateTimeOffset capturedAt, XivChatType type, string sender, string message)
    {
        this.CapturedAt = capturedAt;
        this.Type = type;
        this.Sender = sender;
        this.Message = message;
        this.SenderWithWorld = sender; // Will be updated if world is available
    }

    public DateTimeOffset CapturedAt { get; }

    public XivChatType Type { get; }

    public string Sender { get; }

    public string Message { get; }

    public string SenderWithWorld { get; set; }

    public Emmy.Core.Identity? SenderIdentity { get; set; }
    public string ConversationKey { get; set; } = "";
    public bool IsOwn { get; set; }
    public bool ReplyConsumed { get; set; }
    public long Generation { get; set; }
    public ReplyDecision? ReplyDecision { get; set; }

    public int RelevanceScore { get; set; }

    public string RelevanceReason { get; set; } = string.Empty;

    public bool WasAiRelevanceChecked { get; set; }

    public ConversationType ConversationType { get; set; } = ConversationType.Unclear;

    public string DetectedAddressees { get; set; } = string.Empty;

    public string ActiveParticipants { get; set; } = string.Empty;

    public int ParticipantCount { get; set; }

    public int IsEmmyAddressedScore { get; set; }

    public int OtherTargetClarityScore { get; set; }

    public int GroupChatStrength { get; set; }

    public int ReplyExpectationScore { get; set; }

    public int ThreadParticipationScore { get; set; }

    public string EmmyInvolvementLevel { get; set; } = "low";

    public string ConversationThreadSummary { get; set; } = string.Empty;

    public bool IsSenderTargetingEmmy { get; set; }

    public string TargetingDetectionNote { get; set; } = string.Empty;

    public string? SuggestedReply { get; set; }

    public bool IsReplyPending { get; set; }
}
