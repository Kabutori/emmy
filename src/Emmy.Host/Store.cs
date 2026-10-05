using Emmy.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Emmy.Host;

public sealed class Store
{
    private readonly string connectionString;
    private readonly object gate = new();
    public Store(string directory)
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder{DataSource=Path.Combine(directory,"emmy.db"),DefaultTimeout=5}.ToString();
        using var db = Open();
        using var cmd=db.CreateCommand(); cmd.CommandText="""
            PRAGMA journal_mode=WAL; PRAGMA user_version=1;
            CREATE TABLE IF NOT EXISTS config (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS messages (id TEXT PRIMARY KEY, conversation TEXT NOT NULL, person TEXT NOT NULL, json TEXT NOT NULL, at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_messages_conversation ON messages(conversation,at);
            CREATE TABLE IF NOT EXISTS memories (id TEXT PRIMARY KEY, person TEXT NOT NULL, scope TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS results (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS places (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            """; cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db=new SqliteConnection(connectionString); db.Open(); return db; }
    private int Execute(string sql, params (string,object)[] args)
    {
        lock(gate) { using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText=sql; foreach(var (key,value) in args) cmd.Parameters.AddWithValue(key,value); return cmd.ExecuteNonQuery(); }
    }
    private T[] Query<T>(string sql, params (string,object)[] args)
    {
        lock(gate) { using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText=sql; foreach(var (key,value) in args) cmd.Parameters.AddWithValue(key,value);
            using var reader=cmd.ExecuteReader(); var list=new List<T>(); while(reader.Read()) list.Add(JsonSerializer.Deserialize<T>(reader.GetString(0),Wire.Json)!); return list.ToArray(); }
    }
    public Settings Load() => Query<Settings>("SELECT json FROM config WHERE id=1").FirstOrDefault() ?? Settings.Default;
    public void Save(Settings settings) => Execute("INSERT INTO config VALUES(1,@json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",("@json",JsonSerializer.Serialize(settings,Wire.Json)));
    public bool Message(ChatEvent e) => Execute("INSERT OR IGNORE INTO messages VALUES(@id,@conversation,@person,@json,@at)",("@id",e.Id.ToString()),("@conversation",e.Conversation),
        ("@person",e.Own?e.Recipient?.Key??e.Speaker.Key:e.Speaker.Key),("@json",JsonSerializer.Serialize(e,Wire.Json)),("@at",e.At.ToString("o"))) == 1;
    public ChatEvent[] History(string conversation) => Query<ChatEvent>("SELECT json FROM messages WHERE conversation=@key ORDER BY at DESC LIMIT 12",("@key",conversation)).Reverse().ToArray();
    public void Memory(MemoryRecord memory) => Execute("INSERT INTO memories VALUES(@id,@person,@scope,@json) ON CONFLICT(id) DO UPDATE SET person=excluded.person,scope=excluded.scope,json=excluded.json",
        ("@id",memory.Id.ToString()),("@person",memory.PersonKey),("@scope",memory.Scope),("@json",JsonSerializer.Serialize(memory,Wire.Json)));
    public MemoryRecord[] Memories(string? scope=null) => scope is null ? Query<MemoryRecord>("SELECT json FROM memories ORDER BY rowid DESC LIMIT 200") :
        Query<MemoryRecord>("SELECT json FROM memories WHERE scope=@scope OR scope='public' ORDER BY rowid DESC LIMIT 20",("@scope",scope));
    public void DeleteMemory(Guid id) => Execute("DELETE FROM memories WHERE id=@id",("@id",id.ToString()));
    public void Forget(string key, bool history, bool memory)
    {
        lock(gate) { using var db=Open(); using var transaction=db.BeginTransaction(); using var cmd=db.CreateCommand(); cmd.Transaction=transaction;
            cmd.CommandText=(history?"DELETE FROM messages WHERE person=@key OR conversation IN (SELECT conversation FROM messages WHERE person=@key);":"")+
                (memory?"DELETE FROM memories WHERE person=@key;":""); cmd.Parameters.AddWithValue("@key",key); if(cmd.CommandText.Length>0)cmd.ExecuteNonQuery(); transaction.Commit(); }
    }
    public void Result(ActionResult result) => Execute("INSERT INTO results VALUES(@id,@json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",("@id",result.Id.ToString()),("@json",JsonSerializer.Serialize(result,Wire.Json)));
    public Place[] Places()=>Query<Place>("SELECT json FROM places ORDER BY rowid DESC LIMIT 100");
    public void Place(Place p)=>Execute("INSERT INTO places VALUES(@id,@json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",("@id",p.Id.ToString()),("@json",JsonSerializer.Serialize(p,Wire.Json)));
    public void DeletePlace(Guid id)=>Execute("DELETE FROM places WHERE id=@id",("@id",id.ToString()));
    public ActionResult[] Results() => Query<ActionResult>("SELECT json FROM results ORDER BY rowid DESC LIMIT 50");
}
