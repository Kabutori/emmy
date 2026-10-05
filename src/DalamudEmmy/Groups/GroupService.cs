using DalamudEmmy.Persistence;
using Microsoft.Data.Sqlite;

namespace DalamudEmmy.Groups;

public sealed class GroupService
{
    private readonly PersistenceService persistence;
    private readonly List<Group> groups = [];

    public GroupService(PersistenceService persistence)
    {
        this.persistence = persistence;
    }

    public IReadOnlyList<Group> Groups => this.groups;

    public void Initialize()
    {
        this.LoadGroups();
    }

    public void CreateGroup(string name, string description)
    {
        var group = new Group
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            CreatedAt = DateTimeOffset.Now,
            LastUpdatedAt = DateTimeOffset.Now,
        };

        this.groups.Add(group);
        this.SaveGroup(group);
    }

    public void AddMemberToGroup(string groupId, string personName)
    {
        var group = this.groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null)
        {
            return;
        }

        if (!group.MemberNames.Contains(personName))
        {
            group.MemberNames.Add(personName);
            group.LastUpdatedAt = DateTimeOffset.Now;
            this.SaveGroup(group);
            this.SaveGroupMember(groupId, personName);
        }
    }

    public void RemoveMemberFromGroup(string groupId, string personName)
    {
        var group = this.groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null)
        {
            return;
        }

        group.MemberNames.Remove(personName);
        group.LastUpdatedAt = DateTimeOffset.Now;
        this.SaveGroup(group);
        this.DeleteGroupMember(groupId, personName);
    }

    public void DeleteGroup(string groupId)
    {
        var group = this.groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null)
        {
            return;
        }

        this.groups.Remove(group);
        this.DeleteGroupFromDatabase(groupId);
    }

    private void LoadGroups()
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = "SELECT id, name, description, created_at, last_updated_at FROM groups";

        using var command = new SqliteCommand(selectSql, connection);
        using var reader = command.ExecuteReader();

        this.groups.Clear();

        while (reader.Read())
        {
            var group = new Group
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(3)),
                LastUpdatedAt = DateTimeOffset.Parse(reader.GetString(4)),
            };

            this.LoadGroupMembers(group);
            this.groups.Add(group);
        }
    }

    private void LoadGroupMembers(Group group)
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = "SELECT person_name FROM group_members WHERE group_id = @group_id";

        using var command = new SqliteCommand(selectSql, connection);
        command.Parameters.AddWithValue("@group_id", group.Id);

        using var reader = command.ExecuteReader();

        group.MemberNames.Clear();

        while (reader.Read())
        {
            group.MemberNames.Add(reader.GetString(0));
        }
    }

    private void SaveGroup(Group group)
    {
        using var connection = this.persistence.GetConnection();
        const string upsertSql = @"
            INSERT INTO groups (id, name, description, created_at, last_updated_at)
            VALUES (@id, @name, @description, @created_at, @last_updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = @name,
                description = @description,
                last_updated_at = @last_updated_at";

        using var command = new SqliteCommand(upsertSql, connection);
        command.Parameters.AddWithValue("@id", group.Id);
        command.Parameters.AddWithValue("@name", group.Name);
        command.Parameters.AddWithValue("@description", group.Description);
        command.Parameters.AddWithValue("@created_at", group.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@last_updated_at", group.LastUpdatedAt.ToString("o"));

        command.ExecuteNonQuery();
    }

    private void SaveGroupMember(string groupId, string personName)
    {
        using var connection = this.persistence.GetConnection();
        const string insertSql = @"
            INSERT OR IGNORE INTO group_members (group_id, person_name)
            VALUES (@group_id, @person_name)";

        using var command = new SqliteCommand(insertSql, connection);
        command.Parameters.AddWithValue("@group_id", groupId);
        command.Parameters.AddWithValue("@person_name", personName);

        command.ExecuteNonQuery();
    }

    private void DeleteGroupMember(string groupId, string personName)
    {
        using var connection = this.persistence.GetConnection();
        const string deleteSql = "DELETE FROM group_members WHERE group_id = @group_id AND person_name = @person_name";

        using var command = new SqliteCommand(deleteSql, connection);
        command.Parameters.AddWithValue("@group_id", groupId);
        command.Parameters.AddWithValue("@person_name", personName);

        command.ExecuteNonQuery();
    }

    private void DeleteGroupFromDatabase(string groupId)
    {
        using var connection = this.persistence.GetConnection();
        using var transaction = connection.BeginTransaction();

        // Delete group members first
        const string deleteMembersSql = "DELETE FROM group_members WHERE group_id = @group_id";
        using var deleteMembersCommand = new SqliteCommand(deleteMembersSql, connection, transaction);
        deleteMembersCommand.Parameters.AddWithValue("@group_id", groupId);
        deleteMembersCommand.ExecuteNonQuery();

        // Delete group
        const string deleteGroupSql = "DELETE FROM groups WHERE id = @group_id";
        using var deleteGroupCommand = new SqliteCommand(deleteGroupSql, connection, transaction);
        deleteGroupCommand.Parameters.AddWithValue("@group_id", groupId);
        deleteGroupCommand.ExecuteNonQuery();

        transaction.Commit();
    }
}
