> Historical design reference from the original chat-only project. For current implementation and verified behavior, see [STATUS.md](STATUS.md) and [ACCEPTANCE.md](ACCEPTANCE.md).

# Build Strategy – Emmy soll über die Zeit fertig werden

## Zielbild
Dieses Repo soll nicht nur gepflegt, sondern **schrittweise fertig gebaut** werden.

Nicht in einem chaotischen Sprung, sondern über viele kleine, kontrollierte Iterationen.

## Arbeitsprinzip
Jeder autonome Lauf soll möglichst genau eines tun:

1. den wichtigsten Engpass erkennen
2. einen kleinen, klaren Schritt auswählen
3. diesen Schritt sauber umsetzen oder vorbereiten
4. den neuen Stand im Repo sichtbar machen

## Reihenfolge
1. Konzept in umsetzbare Bausteine zerlegen
2. Architektur und Datenmodell festziehen
3. Plugin-Grundgerüst anlegen
4. erste schmale End-to-End-Vertikale bauen
5. danach gezielt erweitern

## Was "fertig bauen" hier bedeutet
- nicht nur reden, sondern Fortschritt im Repo erzeugen
- offene Entscheidungen sichtbar reduzieren
- Dokumentation schrittweise in implementierbare Struktur überführen
- später auch Code anlegen, aber erst wenn das Fundament dafür klar genug ist

## Steward-Regel
`Emmy Steward` soll nicht bloß polieren. Er soll das Projekt **vorsichtig voranbauen**.

Wenn ein kleiner, sicherer Bauschritt möglich ist, soll er ihn machen.
Wenn ein Schritt noch zu groß oder zu unklar ist, soll er zuerst die Voraussetzung dafür schaffen.

## Brief-Regel
`Emmy Brief` soll nicht nur beschreiben, sondern den nächsten echten Bau-Schritt benennen, damit die Folge-Läufe nicht im Kreis laufen.

## Guardrails
- kleine Schritte statt Scope-Explosion
- lieber ein belastbarer Zwischenstand als halbfertige Magie
- keine hektischen Großumbauten
- bei Unsicherheit erst Struktur schaffen, dann implementieren
