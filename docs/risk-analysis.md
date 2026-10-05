> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Risiko- und Missbrauchskanten – Dalamud Emmy

## Übersicht

Dieses Dokument identifiziert potenzielle Risiken und Missbrauchsmöglichkeiten des Plugins und schlägt Gegenmaßnahmen vor.

## Risikokategorien

### 1. Datenschutz und Privatsphäre

**Risiko:**
- Chat-Nachrichten anderer Spieler werden erfasst und gespeichert
- Personen-Informationen (Name, World) werden persistiert
- Memory-Einträge können sensible Informationen enthalten

**Gegenmaßnahmen:**
- ✅ Nur ausgewählte Kanäle erfassen (Say, Tell, Party)
- ✅ Eigene Nachrichten ignorieren
- ✅ SQLite-Datenbank lokal auf Benutzer-System
- ⚠️ Datenlöschung/Export-Funktion hinzufügen
- ⚠️ Opt-in für Memory-Speicherung
- ⚠️ Anonymisierungsoption für Logs

---

### 2. Spam und Belästigung

**Risiko:**
- Automatische Antworten können als Spam wahrgenommen werden
- Häufige Antworten in öffentlichen Kanälen
- Unangemessene Antworten in sensiblen Kontexten

**Gegenmaßnahmen:**
- ✅ Rate-Limiting (10 Requests/Minute)
- ✅ Cooldowns nach Antworten (Say 15s, Party 10s, Tell 3s)
- ✅ Assistiert-Modus als Standard
- ✅ Whitelist-Prüfung vor Antworten
- ⚠️ Situationsregeln (Kampf, Cutscene, Duty)
- ⚠️ Community-Feedback-Mechanismus

---

### 3. Fehlinterpretation von Kontext

**Risiko:**
- Persona antwortet auf Nachrichten, die nicht an sie gerichtet sind
- Falsche Antworten in Gruppengesprächen
- Missverständnisse bei indirekter Ansprache

**Gegenmaßnahmen:**
- ✅ Should-I-Reply-Modul mit Regelprüfung
- ✅ Persona-Trust-Regeln (All, WhitelistedOnly, None)
- ✅ Reply-Decision-Logik basierend auf Trust-State
- ⚠️ LLM-basierte Should-I-Reply-Entscheidung
- ⚠️ Nearby Player Erkennung
- ⚠️ Session-Kontext-Berücksichtigung

---

### 4. API-Missbrauch

**Risiko:**
- Exzessive API-Calls zu OpenAI/DeepSeek
- API-Key-Exposure in Logs oder Konfiguration
- Kosten durch übermäßige Nutzung

**Gegenmaßnahmen:**
- ✅ Rate-Limiting (10 Requests/Minute)
- ✅ API-Keys werden verschlüsselt gespeichert
- ✅ Logging ohne API-Keys
- ⚠️ Kosten-Limitierung pro Monat
- ⚠️ API-Usage-Dashboard in UI
- ⚠️ Warnungen bei hoher Nutzung

---

### 5. Sicherheit und Targeting

**Risiko:**
- Automatisches Targeting kann Spielmechanik stören
- Targeting im Kampf kann zu Fehlern führen
- Unbeabsichtigtes Targeting falscher Personen

**Gegenmaßnahmen:**
- ⚠️ Targeting standardmäßig deaktiviert
- ⚠️ Targeting im Kampf deaktivieren
- ⚠️ Targeting in Duties deaktivieren
- ⚠️ TargetResolver mit Confidence-Prüfung
- ⚠️ Sicherheitsprüfung vor Targeting

---

### 6. Persona-Verhalten

**Risiko:**
- Persona antwortet unangemessen oder beleidigend
- Persona verletzt RP-Konventionen
- Persona gibt sich als menschlicher Spieler aus

**Gegenmaßnahmen:**
- ✅ System-Prompt mit Verhaltensregeln
- ✅ Persona-spezifische Trust-Regeln
- ⚠️ Content-Filter für Antworten
- ⚠️ Persona-Modi für verschiedene Kontexte
- ⚠️ "Nicht als AI ausgeben"-Regel

---

### 7. Fehlfunktionen und Bugs

**Risiko:**
- Plugin antwortet unerwartet
- Plugin stürzt ab und verursacht Probleme
- Datenbank-Korruption

**Gegenmaßnahmen:**
- ✅ Assistiert-Modus als Standard
- ✅ Ausnahmebehandlung in allen Services
- ✅ Logging für Fehlerdiagnose
- ⚠️ Graceful Degradation bei Fehlern
- ⚠️ Datenbank-Backup-Mechanismus
- ⚠️ Plugin-Status-Indikator in UI

---

### 8. Missbrauch durch Nutzer

**Risiko:**
- Nutzer nutzt Plugin für Belästigung anderer
- Nutzer konfiguriert Persona für unangemessenes Verhalten
- Nutzer umgeht Schutzregeln

**Gegenmaßnahmen:**
- ✅ Whitelist-Prüfung erzwingen
- ✅ Assistiert-Modus als Standard
- ⚠️ Persona-Validierung bei Erstellung
- ⚠️ Community-Richtlinien-Dokumentation
- ⚠️ Warnungen bei riskanter Konfiguration

---

### 9. Dalamud-API-Abhängigkeit

**Risiko:**
- Dalamud-API-Änderungen brechen Plugin
- Dalamud-Versionen inkompatibel
- API-Deprecations nicht beachtet

**Gegenmaßnahmen:**
- ✅ Dalamud API 15 Migration durchgeführt
- ✅ Verwendung von offiziellen Services
- ⚠️ Version-Prüfung beim Start
- ⚠️ Fallback für veraltete APIs
- ⚠️ Regelmäßige API-Updates

---

### 10. Performance und Ressourcen

**Risiko:**
- Exzessive CPU-Nutzung durch LLM-Requests
- Speicherleck durch Chat-Historie
- Datenbank-Wachstum ohne Bereinigung

**Gegenmaßnahmen:**
- ✅ Rate-Limiting
- ✅ Begrenzung auf 25 letzte Nachrichten
- ⚠️ Alte Chat-Nachrichten bereinigen
- ⚠️ Memory-Limit für Session-Historie
- ⚠️ Datenbank-Komprimierung

---

## Priorisierte Gegenmaßnahmen

### Hoch (sofort umsetzen)
- ✅ Assistiert-Modus als Standard
- ✅ Rate-Limiting
- ✅ Whitelist-Prüfung
- ✅ Logging ohne API-Keys

### Mittel (nächste Version)
- ⚠️ Situationsregeln (Kampf, Cutscene, Duty)
- ⚠️ Content-Filter für Antworten
- ⚠️ Datenlöschung/Export-Funktion
- ⚠️ API-Usage-Dashboard

### Niedrig (später)
- ⚠️ Nearby Player Erkennung
- ⚠️ Targeting-Implementierung
- ⚠️ Community-Feedback-Mechanismus
- ⚠️ Persona-Validierung

---

## Sicherheitsprinzipien

1. **Defense in Depth:** Mehrere Schutzschichten (Whitelist, Rate-Limit, Assistiert-Modus)
2. **Fail Safe:** Bei Fehlern lieber keine Antwort als falsche Antwort
3. **User Control:** Nutzer hat Kontrolle über alle wichtigen Entscheidungen
4. **Transparency:** Alle Entscheidungen werden geloggt und können einsehbar sein
5. **Minimal Data:** Nur notwendige Daten erfassen und speichern
