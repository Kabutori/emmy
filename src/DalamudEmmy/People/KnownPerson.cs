namespace DalamudEmmy.People;

public sealed class KnownPerson
{
    public KnownPerson(string displayName)
    {
        this.DisplayName = displayName;
        this.FirstSeenAt = DateTimeOffset.Now;
        this.LastSeenAt = this.FirstSeenAt;
    }

    public string DisplayName { get; }

    public PersonTrustState TrustState { get; set; }

    public int MessageCount { get; internal set; }

    public string PreferredLanguage { get; set; } = string.Empty;

    public int TrustScore { get; set; }

    public int AffinityScore { get; set; }

    public int EngagementScore { get; set; }

    public int BoundaryRiskScore { get; set; }

    public string InteractionStyleSummary { get; set; } = string.Empty;

    public string RelationshipSummary { get; set; } = string.Empty;

    public string MemorySummary { get; set; } = string.Empty;

    public string LastInteractionSummary { get; set; } = string.Empty;

    public string LowInfoOpinionMeta { get; set; } = string.Empty;

    public int LastOpinionRefreshMessageCount { get; set; }

    public DateTimeOffset FirstSeenAt { get; internal set; }

    public DateTimeOffset LastSeenAt { get; internal set; }

    public void RegisterSeen()
    {
        this.MessageCount++;
        this.LastSeenAt = DateTimeOffset.Now;
    }

    public void ResetKnowledge()
    {
        this.MessageCount = 0;
        this.PreferredLanguage = string.Empty;
        this.TrustScore = 0;
        this.AffinityScore = 0;
        this.EngagementScore = 0;
        this.BoundaryRiskScore = 0;
        this.InteractionStyleSummary = string.Empty;
        this.RelationshipSummary = string.Empty;
        this.MemorySummary = string.Empty;
        this.LastInteractionSummary = string.Empty;
        this.LowInfoOpinionMeta = string.Empty;
        this.LastOpinionRefreshMessageCount = 0;
        this.LastSeenAt = DateTimeOffset.Now;
    }
}
