namespace DalamudEmmy.Sessions;

public sealed class Session
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string PersonaName { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int ReplyCount { get; set; }
    public List<string> ParticipantNames { get; set; } = [];
}
