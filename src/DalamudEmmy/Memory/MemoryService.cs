using DalamudEmmy.Persistence;
using Microsoft.Data.Sqlite;

namespace DalamudEmmy.Memory;

public sealed class MemoryService
{
    private readonly PersistenceService persistence;
    private readonly List<MemoryEntry> memories = [];

    public MemoryService(PersistenceService persistence)
    {
        this.persistence = persistence;
    }

    public IReadOnlyList<MemoryEntry> Memories => this.memories;

    public void Initialize()
    {
        this.LoadMemories();
    }

    public void CreateMemory(string type, string content, string? relatedPersonName = null, string? relatedGroupId = null, string? sessionId = null)
    {
        var memory = new MemoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Content = content,
            RelatedPersonName = relatedPersonName,
            RelatedGroupId = relatedGroupId,
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.Now,
            LastAccessedAt = DateTimeOffset.Now,
            AccessCount = 0,
            RelevanceScore = 1.0f,
        };

        this.memories.Add(memory);
        this.SaveMemory(memory);
    }

    public void AccessMemory(string memoryId)
    {
        var memory = this.memories.FirstOrDefault(m => m.Id == memoryId);
        if (memory == null)
        {
            return;
        }

        memory.LastAccessedAt = DateTimeOffset.Now;
        memory.AccessCount++;
        this.SaveMemory(memory);
    }

    public void UpdateRelevanceScore(string memoryId, float score)
    {
        var memory = this.memories.FirstOrDefault(m => m.Id == memoryId);
        if (memory == null)
        {
            return;
        }

        memory.RelevanceScore = score;
        this.SaveMemory(memory);
    }

    public void DeleteMemory(string memoryId)
    {
        var memory = this.memories.FirstOrDefault(m => m.Id == memoryId);
        if (memory == null)
        {
            return;
        }

        this.memories.Remove(memory);
        this.DeleteMemoryFromDatabase(memoryId);
    }

    public List<MemoryEntry> GetRelevantMemories(string? personName = null, string? groupId = null, string? sessionId = null, int limit = 10)
    {
        var query = this.memories.AsQueryable();

        if (personName != null)
        {
            query = query.Where(m => m.RelatedPersonName == personName);
        }

        if (groupId != null)
        {
            query = query.Where(m => m.RelatedGroupId == groupId);
        }

        if (sessionId != null)
        {
            query = query.Where(m => m.SessionId == sessionId);
        }

        return query.OrderByDescending(m => m.RelevanceScore).ThenByDescending(m => m.LastAccessedAt).Take(limit).ToList();
    }

    private void LoadMemories()
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = "SELECT id, type, content, related_person_name, related_group_id, session_id, created_at, last_accessed_at, access_count, relevance_score FROM memory";

        using var command = new SqliteCommand(selectSql, connection);
        using var reader = command.ExecuteReader();

        this.memories.Clear();

        while (reader.Read())
        {
            var memory = new MemoryEntry
            {
                Id = reader.GetString(0),
                Type = reader.GetString(1),
                Content = reader.GetString(2),
                RelatedPersonName = reader.IsDBNull(3) ? null : reader.GetString(3),
                RelatedGroupId = reader.IsDBNull(4) ? null : reader.GetString(4),
                SessionId = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                LastAccessedAt = DateTimeOffset.Parse(reader.GetString(7)),
                AccessCount = reader.GetInt32(8),
                RelevanceScore = reader.GetFloat(9),
            };

            this.memories.Add(memory);
        }
    }

    private void SaveMemory(MemoryEntry memory)
    {
        using var connection = this.persistence.GetConnection();
        const string upsertSql = @"
            INSERT INTO memory (id, type, content, related_person_name, related_group_id, session_id, created_at, last_accessed_at, access_count, relevance_score)
            VALUES (@id, @type, @content, @related_person_name, @related_group_id, @session_id, @created_at, @last_accessed_at, @access_count, @relevance_score)
            ON CONFLICT(id) DO UPDATE SET
                content = @content,
                last_accessed_at = @last_accessed_at,
                access_count = @access_count,
                relevance_score = @relevance_score";

        using var command = new SqliteCommand(upsertSql, connection);
        command.Parameters.AddWithValue("@id", memory.Id);
        command.Parameters.AddWithValue("@type", memory.Type);
        command.Parameters.AddWithValue("@content", memory.Content);
        command.Parameters.AddWithValue("@related_person_name", memory.RelatedPersonName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@related_group_id", memory.RelatedGroupId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@session_id", memory.SessionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@created_at", memory.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@last_accessed_at", memory.LastAccessedAt.ToString("o"));
        command.Parameters.AddWithValue("@access_count", memory.AccessCount);
        command.Parameters.AddWithValue("@relevance_score", memory.RelevanceScore);

        command.ExecuteNonQuery();
    }

    private void DeleteMemoryFromDatabase(string memoryId)
    {
        using var connection = this.persistence.GetConnection();
        const string deleteSql = "DELETE FROM memory WHERE id = @id";

        using var command = new SqliteCommand(deleteSql, connection);
        command.Parameters.AddWithValue("@id", memoryId);

        command.ExecuteNonQuery();
    }
}
