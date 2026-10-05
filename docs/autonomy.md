> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Autonomie-Notizen für Dalamud Emmy

Dieses Repo darf von Echo kontrolliert weiterentwickelt werden, aber in einem engen Rahmen.
Ziel ist dabei ausdrücklich: **über die Zeit Stück für Stück zum fertigen Plugin kommen**.

## Brief
`Emmy Brief` soll kurz sagen:
- was klar ist
- was unklar ist
- was der nächste kleine echte Bauschritt wäre
- was noch nicht eskaliert werden sollte

## Steward
`Emmy Steward` darf kleine, lokale, reversible Verbesserungen machen, die das Projekt Richtung Umsetzung schieben.

Typische sinnvolle Arbeiten:
- Doku schärfen
- Roadmap ordnen
- Modulgrenzen beschreiben
- offene Architekturentscheidungen sichtbar machen
- kleine Fundament-Codebasis anlegen, wenn die Voraussetzungen dafür klar genug sind
- Vorbedingungen für spätere Implementierung schaffen

Nicht sinnvoll ohne explizite Freigabe:
- große Codeblöcke ohne Fundament
- wilde API-Implementierung
- direkte Pushes auf `main`
- hektische Scope-Ausweitung

## Leitidee
Erst ein gutes Companion-Design und eine saubere Plugin-Architektur.
Dann kontrolliert in ernsthafte Implementierung übergehen.

## Fortschrittsregel
Jeder Lauf soll idealerweise genau einen kleinen Fortschritt erzeugen:
- eine offene Entscheidung klären
- eine Struktur ergänzen
- eine Roadmap-Stufe konkretisieren
- ein kleines Implementierungsfundament legen

Stillstand ist okay, wenn ein Schritt noch zu riskant oder zu unklar wäre.
Aber das Standardziel ist **Vorankommen**, nicht bloß Beobachten.
