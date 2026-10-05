using DalamudEmmy.Persistence;
using Microsoft.Data.Sqlite;

namespace DalamudEmmy.Sessions;

public sealed class SessionService
{
    private readonly PersistenceService persistence;
    private readonly List<Session> sessions = [];

    public SessionService(PersistenceService persistence)
    {
        this.persistence = persistence;
    }

    public IReadOnlyList<Session> Sessions => this.sessions;

    public void Initialize()
    {
        this.LoadSessions();
    }

    public Session CreateSession(string personaName)
    {
        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            StartedAt = DateTimeOffset.Now,
            PersonaName = personaName,
        };

        this.sessions.Add(session);
        this.SaveSession(session);
        return session;
    }

    public void EndSession(string sessionId)
    {
        var session = this.sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            return;
        }

        session.EndedAt = DateTimeOffset.Now;
        this.SaveSession(session);
    }

    public void AddParticipant(string sessionId, string personName)
    {
        var session = this.sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            return;
        }

        if (!session.ParticipantNames.Contains(personName))
        {
            session.ParticipantNames.Add(personName);
            this.SaveSession(session);
            this.SaveSessionParticipant(sessionId, personName);
        }
    }

    public void IncrementMessageCount(string sessionId)
    {
        var session = this.sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            return;
        }

        session.MessageCount++;
        this.SaveSession(session);
    }

    public void IncrementReplyCount(string sessionId)
    {
        var session = this.sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            return;
        }

        session.ReplyCount++;
        this.SaveSession(session);
    }

    private void LoadSessions()
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = "SELECT id, started_at, ended_at, persona_name, message_count, reply_count FROM sessions";

        using var command = new SqliteCommand(selectSql, connection);
        using var reader = command.ExecuteReader();

        this.sessions.Clear();

        while (reader.Read())
        {
            var session = new Session
            {
                Id = reader.GetString(0),
                StartedAt = DateTimeOffset.Parse(reader.GetString(1)),
                EndedAt = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                PersonaName = reader.GetString(3),
                MessageCount = reader.GetInt32(4),
                ReplyCount = reader.GetInt32(5),
            };

            this.LoadSessionParticipants(session);
            this.sessions.Add(session);
        }
    }

    private void LoadSessionParticipants(Session session)
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = "SELECT person_name FROM session_participants WHERE session_id = @session_id";

        using var command = new SqliteCommand(selectSql, connection);
        command.Parameters.AddWithValue("@session_id", session.Id);

        using var reader = command.ExecuteReader();

        session.ParticipantNames.Clear();

        while (reader.Read())
        {
            session.ParticipantNames.Add(reader.GetString(0));
        }
    }

    private void SaveSession(Session session)
    {
        using var connection = this.persistence.GetConnection();
        const string upsertSql = @"
            INSERT INTO sessions (id, started_at, ended_at, persona_name, message_count, reply_count)
            VALUES (@id, @started_at, @ended_at, @persona_name, @message_count, @reply_count)
            ON CONFLICT(id) DO UPDATE SET
                ended_at = @ended_at,
                message_count = @message_count,
                reply_count = @reply_count";

        using var command = new SqliteCommand(upsertSql, connection);
        command.Parameters.AddWithValue("@id", session.Id);
        command.Parameters.AddWithValue("@started_at", session.StartedAt.ToString("o"));
        command.Parameters.AddWithValue("@ended_at", session.EndedAt?.ToString("o") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@persona_name", session.PersonaName);
        command.Parameters.AddWithValue("@message_count", session.MessageCount);
        command.Parameters.AddWithValue("@reply_count", session.ReplyCount);

        command.ExecuteNonQuery();
    }

    private void SaveSessionParticipant(string sessionId, string personName)
    {
        using var connection = this.persistence.GetConnection();
        const string insertSql = @"
            INSERT OR IGNORE INTO session_participants (session_id, person_name)
            VALUES (@session_id, @person_name)";

        using var command = new SqliteCommand(insertSql, connection);
        command.Parameters.AddWithValue("@session_id", sessionId);
        command.Parameters.AddWithValue("@person_name", personName);

        command.ExecuteNonQuery();
    }
}
