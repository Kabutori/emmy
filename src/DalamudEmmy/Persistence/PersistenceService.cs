using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Data.Sqlite;

namespace DalamudEmmy.Persistence;

public sealed class PersistenceService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly string dbPath;

    public PersistenceService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.dbPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "dalamud-emmy.db");
    }

    public void Initialize()
    {
        this.EnsureDatabaseExists();
        this.EnsureSchemaExists();
    }

    public SqliteConnection GetConnection()
    {
        var connection = new SqliteConnection($"Data Source={this.dbPath}");
        connection.Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL";
        pragmas.ExecuteNonQuery();
        return connection;
    }

    public void Dispose()
    {
    }

    private void EnsureDatabaseExists()
    {
        var directory = Path.GetDirectoryName(this.dbPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(this.dbPath))
        {
            this.log.Information("Creating new database at {DbPath}", this.dbPath);
            var connection = new SqliteConnection($"Data Source={this.dbPath}");
            connection.Open();
            connection.Close();
        }
    }

    private void EnsureSchemaExists()
    {
        using var connection = this.GetConnection();
        using var transaction = connection.BeginTransaction();

        this.CreatePeopleTable(connection, transaction);
        this.CreateChatMessagesTable(connection, transaction);
        this.CreateGroupsTable(connection, transaction);
        this.CreateGroupMembersTable(connection, transaction);
        this.CreateSessionsTable(connection, transaction);
        this.CreateSessionParticipantsTable(connection, transaction);
        this.CreateMemoryTable(connection, transaction);
        this.EnsurePeopleColumns(connection, transaction);
        using var columns = new SqliteCommand("PRAGMA table_info(chat_messages)",connection,transaction);
        bool hasKey = false;
        using (var reader=columns.ExecuteReader()) while(reader.Read()) if(reader.GetString(1)=="conversation_key") hasKey=true;
        if(!hasKey) { using var alter = new SqliteCommand("ALTER TABLE chat_messages ADD COLUMN conversation_key TEXT NOT NULL DEFAULT ''",connection,transaction); alter.ExecuteNonQuery(); }

        transaction.Commit();
        this.log.Information("Database schema verified and updated.");
    }

    private void CreatePeopleTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS people (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                display_name TEXT NOT NULL UNIQUE,
                trust_state INTEGER NOT NULL DEFAULT 0,
                message_count INTEGER NOT NULL DEFAULT 0,
                preferred_language TEXT NOT NULL DEFAULT '',
                trust_score INTEGER NOT NULL DEFAULT 0,
                affinity_score INTEGER NOT NULL DEFAULT 0,
                engagement_score INTEGER NOT NULL DEFAULT 0,
                boundary_risk_score INTEGER NOT NULL DEFAULT 0,
                interaction_style_summary TEXT NOT NULL DEFAULT '',
                relationship_summary TEXT NOT NULL DEFAULT '',
                memory_summary TEXT NOT NULL DEFAULT '',
                last_interaction_summary TEXT NOT NULL DEFAULT '',
                low_info_opinion_meta TEXT NOT NULL DEFAULT '',
                last_opinion_refresh_message_count INTEGER NOT NULL DEFAULT 0,
                first_seen_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void EnsurePeopleColumns(SqliteConnection connection, SqliteTransaction transaction)
    {
        EnsureColumnExists(connection, transaction, "people", "preferred_language", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "trust_score", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, transaction, "people", "affinity_score", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, transaction, "people", "engagement_score", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, transaction, "people", "boundary_risk_score", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, transaction, "people", "interaction_style_summary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "relationship_summary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "memory_summary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "last_interaction_summary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "low_info_opinion_meta", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, transaction, "people", "last_opinion_refresh_message_count", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumnExists(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName, string columnDefinition)
    {
        using var pragmaCommand = new SqliteCommand($"PRAGMA table_info({tableName})", connection, transaction);
        using var reader = pragmaCommand.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();

        using var alterCommand = new SqliteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}", connection, transaction);
        alterCommand.ExecuteNonQuery();
    }

    private void CreateChatMessagesTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_at TEXT NOT NULL,
                chat_type INTEGER NOT NULL,
                sender TEXT NOT NULL,
                message TEXT NOT NULL
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void CreateGroupsTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS groups (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                created_at TEXT NOT NULL,
                last_updated_at TEXT NOT NULL
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void CreateGroupMembersTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS group_members (
                group_id TEXT NOT NULL,
                person_name TEXT NOT NULL,
                PRIMARY KEY (group_id, person_name),
                FOREIGN KEY (group_id) REFERENCES groups(id)
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void CreateSessionsTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                persona_name TEXT NOT NULL,
                message_count INTEGER NOT NULL DEFAULT 0,
                reply_count INTEGER NOT NULL DEFAULT 0
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void CreateSessionParticipantsTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS session_participants (
                session_id TEXT NOT NULL,
                person_name TEXT NOT NULL,
                PRIMARY KEY (session_id, person_name),
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }

    private void CreateMemoryTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS memory (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                content TEXT NOT NULL,
                related_person_name TEXT,
                related_group_id TEXT,
                session_id TEXT,
                created_at TEXT NOT NULL,
                last_accessed_at TEXT NOT NULL,
                access_count INTEGER NOT NULL DEFAULT 0,
                relevance_score REAL NOT NULL DEFAULT 0.0
            )";

        using var command = new SqliteCommand(createTableSql, connection, transaction);
        command.ExecuteNonQuery();
    }
}
