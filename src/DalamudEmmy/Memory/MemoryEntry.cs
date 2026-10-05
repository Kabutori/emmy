namespace DalamudEmmy.Memory;

public sealed class MemoryEntry
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RelatedPersonName { get; set; }
    public string? RelatedGroupId { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public float RelevanceScore { get; set; }
}
