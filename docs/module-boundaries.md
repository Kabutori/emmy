> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Modulgrenzen – Dalamud Emmy

## Übersicht

Das Projekt ist in funktionale Module unterteilt, die klare Verantwortlichkeiten haben und lose gekoppelt sind.

## Module

### Chat (`src/DalamudEmmy/Chat/`)

**Verantwortlichkeit:**
- Erfassung von Chat-Nachrichten aus FFXIV
- Filterung relevanter Chat-Typen (Say, Tell, Party)
- Ignorieren eigener Nachrichten
- Session-lokaler Puffer für letzte Nachrichten
- Auslösen von LLM-Requests bei Reply-Decision

**Abhängigkeiten:**
- Dalamud `IChatGui`, `IObjectTable`
- `Configuration`
- `PeopleService`
- `PersistenceService`
- `ReplyDecisionService`
- `LLMRequestService`

**Exportiert:**
- `CapturedChatMessage` – Datenmodell für erfasste Nachrichten
- `ChatCaptureService` – Service für Chat-Erfassung

---

### People (`src/DalamudEmmy/People/`)

**Verantwortlichkeit:**
- Verwaltung bekannter Personen
- Trust-State-Management (Unknown, Whitelisted, Ignored)
- Registrierung von Chat-Sendern
- Persistenz über `PersistenceService`

**Abhängigkeiten:**
- `PersistenceService`

**Exportiert:**
- `KnownPerson` – Datenmodell für bekannte Personen
- `PersonTrustState` – Enum für Trust-States
- `PeopleService` – Service für Personen-Management

---

### Persistence (`src/DalamudEmmy/Persistence/`)

**Verantwortlichkeit:**
- SQLite-Datenbank-Management
- Schema-Initialisierung
- Connection-Management

**Abhängigkeiten:**
- Dalamud `IDalamudPluginInterface`, `IPluginLog`
- `Microsoft.Data.Sqlite`

**Exportiert:**
- `PersistenceService` – Service für Datenbank-Operationen

---

### Persona (`src/DalamudEmmy/Persona/`)

**Verantwortlichkeit:**
- Persona-Konfiguration
- Persona-spezifische Trust-Regeln
- Reply-Verhalten (ReplyToUnknown, ReplyToIgnored)

**Abhängigkeiten:**
- Keine (reine Datenmodelle)

**Exportiert:**
- `PersonaTrustMode` – Enum für Trust-Modi
- `PersonaConfig` – Datenmodell für Persona-Konfiguration

---

### Provider (`src/DalamudEmmy/Provider/`)

**Verantwortlichkeit:**
- Abstraktion für LLM-Provider
- Konkrete Implementierungen (OpenAI, DeepSeek)
- HTTP-Calls zu LLM-APIs
- Logging für API-Calls

**Abhängigkeiten:**
- Dalamud `IPluginLog`
- `System.Net.Http`
- `System.Text.Json`

**Exportiert:**
- `IProvider` – Interface für LLM-Provider
- `OpenAIProvider` – OpenAI-Implementierung
- `DeepSeekProvider` – DeepSeek-Implementierung
- `LLMRequestService` – Service für LLM-Requests

---

### Reply (`src/DalamudEmmy/Reply/`)

**Verantwortlichkeit:**
- Reply-Decision-Logik
- Berücksichtigung von Persona-Trust-Regeln
- Berücksichtigung von Person-Trust-State

**Abhängigkeiten:**
- `Configuration`
- `PeopleService`

**Exportiert:**
- `ReplyDecision` – Enum für Reply-Entscheidungen
- `ReplyDecisionService` – Service für Reply-Decision

---

### UI (`src/DalamudEmmy/UI/`)

**Verantwortlichkeit:**
- ImGui-basierte Benutzeroberfläche
- Tab-basierte Navigation
- Konfiguration-UI
- Chat-Anzeige
- Personen-Management-UI
- Persona-Trust-Regel-UI
- Provider-Konfigurations-UI

**Abhängigkeiten:**
- Dalamud `ImGui`
- `Configuration`
- `ChatCaptureService`
- `PeopleService`

**Exportiert:**
- `PluginWindow` – Hauptfenster des Plugins

---

## Architekturprinzipien

1. **Separation of Concerns:** Jedes Modul hat eine klare, einzelne Verantwortlichkeit.
2. **Dependency Injection:** Services werden über Konstruktoren injiziert.
3. **Loose Coupling:** Module kommunizieren über Interfaces und Services, nicht direkt.
4. **Single Source of Truth:** Konfiguration wird zentral in `Configuration` verwaltet.
5. **Persistence Layer:** Datenbankzugriffe sind isoliert in `PersistenceService`.

## Zirkuläre Abhängigkeiten

Keine zirkulären Abhängigkeiten vorhanden. Die Abhängigkeitskette ist linear:

`UI` → `Chat` → `People` → `Persistence`
`UI` → `Reply` → `People`
`Chat` → `Reply` → `Configuration`
`Chat` → `Provider` → `Configuration`
