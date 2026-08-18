# NLTSQL

Self-Service-Analytics-Plattform: Kunden fragen ihre Datenbanken in natürlicher Sprache ab
und erhalten Tabelle, Diagramm und Fließtext. Ergebnisse lassen sich als Dashboard-Kachel
speichern und jederzeit erneut abrufen, Daten als CSV exportieren.

## Der zentrale Entwurfsgedanke

Ein Sprachmodell schreibt hier **kein SQL**. Es erzeugt ausschließlich eine typisierte,
schema-validierte `QuerySpec` — welche Entität, welche Kennzahlen, welche Dimensionen,
welche Filter. Ein deterministischer Compiler übersetzt diese Spezifikation anschließend in
Oracle- bzw. PostgreSQL-SQL.

Damit liegt der fehleranfällige Teil — Joins, Granularität, Aggregat-Mathematik,
Dialektunterschiede — im Code und nicht im Modell. Das ist die Voraussetzung dafür, dass die
Plattform mit einem lokal betriebenen Ollama-Modell verlässlich arbeitet, und es macht jede
erzeugte Abfrage nachvollziehbar und autorisierbar.

Grundlage ist ein **semantischer Layer**: ein versioniertes YAML-Fachmodell, das aus den
Datenbanken generiert und anschließend fachlich veredelt wird.

## Aufbau

| Projekt | Aufgabe |
|---|---|
| `NLTSQL.Core` | Domänenmodell: `QuerySpec`, Ergebnisse, Abstraktionen |
| `NLTSQL.Semantics` | YAML-Fachmodell laden, mergen, validieren, durchsuchen |
| `NLTSQL.Scaffolding` | Introspektion und Profiling der Zieldatenbanken → generiertes Modell |
| `NLTSQL.QueryEngine` | `QuerySpec` → SQL je Dialekt, Row-Policies, Ausführung |
| `NLTSQL.Ai` | LLM-Pipeline hinter `IChatClient`: NL → `QuerySpec`, Diagrammwahl, Narration |
| `NLTSQL.Data` | Anwendungspersistenz auf SQLite (Identity, Dashboards, Audit) |
| `NLTSQL.Web` | Blazor Web App mit Fluent UI |
| `NLTSQL.Cli` | `nltsql scaffold \| validate \| diff \| eval` |

## Voraussetzungen

- .NET SDK 10 (`global.json` pinnt die Bandbreite)
- Docker, für die lokalen Zieldatenbanken und Ollama

## Loslegen

```bash
docker compose up -d
docker compose exec ollama ollama pull qwen3:14b
docker compose exec ollama ollama pull nomic-embed-text

dotnet build
dotnet test
```

Die Zieldatenbanken werden mit einem Demo-Vertriebsschema (`vertrieb`) hochgefahren, das
Tabellenkommentare und Mandanten-Spalten mitbringt — beides braucht das Scaffolding.

## Tests

```bash
dotnet test
```

Die gesamte Suite läuft in-process, ohne Datenbank. Die Korrektheit des erzeugten SQL wird
festgehalten, indem der Statement-Text für beide Dialekte wörtlich geprüft wird — eine Änderung
am Rendering erscheint damit als lesbarer Diff statt als stille Zahlenabweichung.

## Sicherheitshinweise

- Die Plattform verbindet sich ausschließlich mit **lesenden** Datenbankkonten.
- Filterwerte werden immer als Parameter gebunden, nie in SQL interpoliert.
- Mandanten- und Rollenfilter werden serverseitig injiziert und sind für das Sprachmodell
  weder sicht- noch beeinflussbar.
