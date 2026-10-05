> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Datenmodell – Dalamud Emmy

## Übersicht

Das Datenmodell definiert die Kernentitäten für Persona, Personen, Gruppen, Sessions und Memory.

## Persona

**Aktuell:** `PersonaConfig` in `src/DalamudEmmy/Persona/PersonaConfig.cs`

**Felder:**
- `Name` – Name der Persona
- `TrustMode` – PersonaTrustMode (All, WhitelistedOnly, None)
- `ReplyToUnknown` – Ob auf unbekannte Personen geantwortet wird
- `ReplyToIgnored` – Ob auf ignorierte Personen geantwortet wird

**Zukünftige Erweiterungen:**
- `Tone` – Tonalität der Persona (freundlich, formell, humorvoll, etc.)
- `RpMode` – RP-Modus (in-character, out-of-character, hybrid)
- `AllowedChannels` – Erlaubte Chat-Kanäle
- `SystemPrompt` – System-Prompt für LLM
- `MemoryLinks` – Verknüpfungen zu Memory-Einträgen

---

## Personen

**Aktuell:** `KnownPerson` in `src/DalamudEmmy/People/KnownPerson.cs`

**Felder:**
- `DisplayName` – Name der Person
- `TrustState` – PersonTrustState (Unknown, Whitelisted, Ignored)
- `MessageCount` – Anzahl der Nachrichten von dieser Person
- `FirstSeenAt` – Erstes Auftreten
- `LastSeenAt` – Letztes Auftreten

**Zukünftige Erweiterungen:**
- `World` – Server/World der Person
- `GroupId` – Verknüpfung zu Gruppe (falls zutreffend)
- `RelationshipScore` – Beziehungsscore (basierend auf Interaktionen)
- `Notes` – Manuelle Notizen zur Person

---

## Gruppen (neu)

**Vorgeschlagenes Modell:**

```csharp
public sealed class Group
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> MemberNames { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}
```

**Verwendung:**
- Organisation von Personen in Gruppen (z.B. FC, Party, Freunde)
- Gruppenspezifische Trust-Regeln
- Gruppenspezifische Memory

---

## Sessions (neu)

**Vorgeschlagenes Modell:**

```csharp
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
```

**Verwendung:**
- Logging von Konversationssessions
- Trennung zwischen verschiedenen Spiel-Sessions
- Session-spezifische Memory

---

## Memory (neu)

**Vorgeschlagenes Modell:**

```csharp
public sealed class MemoryEntry
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "fact", "preference", "event", "relationship"
    public string Content { get; set; } = string.Empty;
    public string? RelatedPersonName { get; set; }
    public string? RelatedGroupId { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public float RelevanceScore { get; set; }
}
```

**Verwendung:**
- Langzeitgedächtnis für wichtige Informationen
- Fakten über Personen (Beruf, Hobbys, etc.)
- Präferenzen und Abneigungen
- Wichtige Ereignisse
- Beziehungen zwischen Personen

---

## Persistenz-Mapping

### SQLite-Schema-Erweiterungen

**Gruppen-Tabelle:**
```sql
CREATE TABLE groups (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    created_at TEXT NOT NULL,
    last_updated_at TEXT NOT NULL
);
```

**Gruppen-Mitglieder-Tabelle:**
```sql
CREATE TABLE group_members (
    group_id TEXT NOT NULL,
    person_name TEXT NOT NULL,
    FOREIGN KEY (group_id) REFERENCES groups(id),
    PRIMARY KEY (group_id, person_name)
);
```

**Sessions-Tabelle:**
```sql
CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    persona_name TEXT NOT NULL,
    message_count INTEGER NOT NULL DEFAULT 0,
    reply_count INTEGER NOT NULL DEFAULT 0
);
```

**Session-Teilnehmer-Tabelle:**
```sql
CREATE TABLE session_participants (
    session_id TEXT NOT NULL,
    person_name TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES sessions(id),
    PRIMARY KEY (session_id, person_name)
);
```

**Memory-Tabelle:**
```sql
CREATE TABLE memory (
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
);
```

---

## Beziehungen zwischen Entitäten

```
Persona ──┬──> Configuration (aktive Persona)
          └──> Session (verwendete Persona)

Person ──┬──> PeopleService (Management)
         ├──> Group (Mitgliedschaft)
         ├──> Session (Teilnahme)
         └──> Memory (bezogene Einträge)

Group ──┬──> People (Mitglieder)
        └──> Memory (bezogene Einträge)

Session ──┬──> Persona (verwendete Persona)
          ├──> People (Teilnehmer)
          └──> Memory (bezogene Einträge)

Memory ──┬──> Person (bezogene Person)
         ├──> Group (bezogene Gruppe)
         └──> Session (bezogene Session)
```

---

## Implementierungsreihenfolge

1. **Gruppen** – Einfachste Erweiterung, keine Abhängigkeiten zu neuen Modulen
2. **Sessions** – Benötigt Logging-Infrastruktur
3. **Memory** – Komplexeste Erweiterung, benötigt Relevanz-Score-Logik
