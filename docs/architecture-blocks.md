> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Architekturbausteine – Dalamud Emmy

## Übersicht

Dieses Dokument zerlegt die zentralen Anforderungen aus `konzept.md` in konkrete Architekturbausteine für die Implementierung.

## Kernbausteine

### 1. Chat-Kanäle

**Anforderung:**
- Unterstützte Kanäle in v1: Say, Tell, Party
- Antworten im selben Kanal wie Eingang
- Nicht in v1: Free Company, Linkshell, Shout, Yell, etc.

**Architekturbaustein:**
- `ChatCaptureService` – Erfassung und Filterung nach Kanal
- `ChannelRuleEngine` – Kanal-spezifische Regeln
- `ChannelMapper` – Mapping Eingang → Antwortkanal

**Status:** ✅ Implementiert (Say, Tell, Party)

---

### 2. LLM-Anbieter

**Anforderung:**
- Unterstützte Anbieter in v1: OpenAI, DeepSeek
- Provider-Abstraktion für Erweiterbarkeit
- Kein automatischer Fallback
- Provider-spezifische Einstellungen (API-Key, Model, Temperature, MaxTokens)

**Architekturbaustein:**
- `IProvider` – Interface für LLM-Provider
- `OpenAIProvider` – OpenAI-Implementierung
- `DeepSeekProvider` – DeepSeek-Implementierung
- `LLMRequestService` – Provider-Management und Request-Orchestrierung

**Status:** ✅ Implementiert

---

### 3. Persona-System

**Anforderung:**
- Persona-Eigenschaften: Name, Beschreibung, Sprachstil, Persönlichkeit, Humor-Level, RP-Level
- Persona-Modi: Neutral, Frech, RP-Modus, Support-Modus, Still-Modus
- Persona-spezifische Trust-Regeln
- System-Prompt pro Persona/Modus

**Architekturbaustein:**
- `PersonaConfig` – Persona-Konfiguration
- `PersonaTrustMode` – Trust-Modi (All, WhitelistedOnly, None)
- `PersonaMode` – Persona-Modi (vorgeschlagen)
- `PersonaManager` – Persona-Management (vorgeschlagen)

**Status:** ⚠️ Teilweise implementiert (Trust-Regeln, keine Modi)

---

### 4. Personen/Whitelist

**Anforderung:**
- Identität: `CharacterName@World`
- Trust-State: Unknown, Whitelisted, Ignored
- Registrierung von Chat-Sendern
- Persistenz in SQLite

**Architekturbaustein:**
- `KnownPerson` – Datenmodell für bekannte Personen
- `PersonTrustState` – Enum für Trust-States
- `PeopleService` – Personen-Management
- `PersistenceService` – SQLite-Persistenz

**Status:** ✅ Implementiert

---

### 5. Gruppen

**Anforderung:**
- Organisation von Personen in Gruppen (Freunde, RP-Kontakte, Party, Vorsichtig, Ignorieren)
- Gruppenregeln: darf Companion ansprechen, automatische Antworten, Assistiert-Modus, bevorzugte Persona
- Gruppengedächtnis für wiederkehrende Gruppen-Sessions

**Architekturbaustein:**
- `Group` – Datenmodell für Gruppen
- `GroupManager` – Gruppen-Management (vorgeschlagen)
- `GroupRules` – Gruppenregeln (vorgeschlagen)

**Status:** ❌ Nicht implementiert (vorgeschlagen in data-model.md)

---

### 6. Sessions

**Anforderung:**
- Session als zeitlich/räumlich begrenzter Gesprächskontext
- Session-Daten: ID, Kanal, ZoneId, StartedAt, LastActivityAt, Participants, NearbyPlayers
- Session-Start: Whitelist-Person schreibt, Persona erwähnt, Tell-Nachricht
- Session-Ende: Idle Timeout, Zone-Wechsel, Kanal-Inaktivität
- Session-Historie für Prompt-Kontext

**Architekturbaustein:**
- `Session` – Datenmodell für Sessions
- `SessionManager` – Session-Management (vorgeschlagen)
- `SessionTracker` – Session-Tracking (vorgeschlagen)

**Status:** ❌ Nicht implementiert (vorgeschlagen in data-model.md)

---

### 7. Memory

**Anforderung:**
- Memory-Typen: Kurzzeitgedächtnis, Langzeitgedächtnis, Faktenmemory, Stilmemory, Konfliktmemory
- Memory-Einträge: Typ, Content, RelatedPerson, RelatedGroup, SessionId, RelevanceScore
- Memory-Links zu Personen/Gruppen/Sessions
- Relevanz-Score für Memory-Auswahl

**Architekturbaustein:**
- `MemoryEntry` – Datenmodell für Memory
- `MemoryManager` – Memory-Management (vorgeschlagen)
- `MemorySelector` – Relevanz-basierte Memory-Auswahl (vorgeschlagen)

**Status:** ❌ Nicht implementiert (vorgeschlagen in data-model.md)

---

### 8. Should-I-Reply

**Anforderung:**
- Vorgeschaltete Entscheidungsschicht vor Antwortgenerierung
- Regelbasierter Vorfilter + optionaler LLM-Entscheidungsprompt
- Entscheidungskriterien: Whitelist, Nähe, Persona erwähnt, Spieler erwähnt, Frage, Session-Kontext, Cooldown
- JSON-Ergebnis: shouldReply, confidence, reason, addressedEntity, conversationType

**Architekturbaustein:**
- `ReplyDecision` – Enum für Reply-Entscheidungen
- `ReplyDecisionService` – Reply-Decision-Logik
- `ShouldReplyEngine` – Erweiterte Engine mit LLM-Entscheidung (vorgeschlagen)

**Status:** ⚠️ Teilweise implementiert (regelbasiert, kein LLM-Entscheidungsprompt)

---

### 9. Assistiert/Aktiv-Modus

**Anforderung:**
- Assistiert-Modus: Antwortvorschlag im UI, Nutzer entscheidet
- Aktiv-Modus: Selbstständiges Senden nach Regeln
- Unterschiedliche Confidence-Schwellen: Assistiert (0.55), Aktiv (0.82)

**Architekturbaustein:**
- `Configuration.AssistedModeOnly` – Konfiguration für Modus
- `ChatCaptureService.SendReply()` – Versand nur außerhalb Assisted-Modus
- `UI.SendButton` – Nur außerhalb Assisted-Modus

**Status:** ✅ Implementiert

---

### 10. Regel-Engine

**Anforderung:**
- Harte Regeln: Nicht überschreibbar (Whitelist, Kanal, Targeting, Assistiert-Modus, Cooldown)
- Situationsregeln: Kampf, Cutscene, Duty, Say-Nähe, Unklarer Adressat
- Cooldowns: Say (15s), Party (10s), Tell (3s), Max/Minute (3)

**Architekturbaustein:**
- `RuleEngine` – Regel-Prüfung (vorgeschlagen)
- `CooldownManager` – Cooldown-Management (vorgeschlagen)
- `SituationDetector` – Kampferkennung, Cutscene, Duty (vorgeschlagen)

**Status:** ⚠️ Teilweise implementiert (Rate-Limiting, keine Situationsregeln)

---

### 11. Nearby Player

**Anforderung:**
- Nur Spieler in Reichweite berücksichtigen
- Einstellbarer Radius (Standard: 30 Yalms)
- Nearby-Snapshot für Kontextbewertung
- Join-/Leave-Erkennung

**Architekturbaustein:**
- `NearbyPlayerTracker` – Nearby-Tracking (vorgeschlagen)
- `NearbySnapshot` – Snapshot-Datenmodell (vorgeschlagen)
- `DistanceChecker` – Distanz-Prüfung (vorgeschlagen)

**Status:** ❌ Nicht implementiert

---

### 12. Targeting

**Anforderung:**
- Optionales Targeting der Zielperson
- Regeln: Targeting aktiv, eindeutige Identifikation, Nähe, Confidence, keine gefährliche Situation
- Deaktivierbar: Kampf, Duty, Cutscene
- TargetResolver für Confidence-Berechnung

**Architekturbaustein:**
- `TargetResolver` – Targeting-Entscheidung (vorgeschlagen)
- `TargetManager` – Target-Management (vorgeschlagen)
- `SafetyChecker` – Sicherheitsprüfung (vorgeschlagen)

**Status:** ❌ Nicht implementiert

---

## Implementierungsreihenfolge

### Phase 1 (Erledigt)
- Chat-Kanäle
- LLM-Anbieter
- Personen/Whitelist
- Assistiert/Aktiv-Modus
- Teilweise Should-I-Reply
- Teilweise Regel-Engine (Rate-Limiting)

### Phase 2 (Nächste Schritte)
- Persona-Modi erweitern
- Should-I-Reply mit LLM-Entscheidungsprompt
- Gruppen implementieren
- Sessions implementieren

### Phase 3 (Später)
- Memory implementieren
- Nearby Player implementieren
- Targeting implementieren
- Situationsregeln erweitern (Kampf, Cutscene, Duty)

---

## Offene Entscheidungen (v1 vs. später)

### v1
- ✅ Chat-Kanäle: Say, Tell, Party
- ✅ LLM-Anbieter: OpenAI, DeepSeek
- ✅ Assistiert/Aktiv-Modus
- ✅ Persona-Trust-Regeln
- ✅ Personen/Whitelist
- ✅ Rate-Limiting

### Später
- ❌ Persona-Modi (Neutral, Frech, RP-Modus, etc.)
- ❌ Gruppen
- ❌ Sessions
- ❌ Memory
- ❌ Nearby Player
- ❌ Targeting
- ❌ Situationsregeln (Kampf, Cutscene, Duty)
- ❌ Weitere Kanäle (Free Company, Linkshell, etc.)
- ❌ Weitere Provider (OpenRouter, Anthropic, Gemini, Ollama)
