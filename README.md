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

## Prototyp ausprobieren

Der aktuelle Stand ist ein Prototyp: eine Frage stellen, die erzeugte Abfrage und das SQL sehen,
Ergebnis als Tabelle und Diagramm. Ein Modell wird von Hand gepflegt statt generiert, es läuft
gegen PostgreSQL, und Abfragen bleiben auf eine Entität beschränkt.

```bash
# 1. Zieldatenbank und Modellruntime starten
docker compose up -d postgres ollama
docker compose exec ollama ollama pull qwen3:14b

# 2. Anwendung starten
dotnet run --project src/NLTSQL.Web
```

Dann `https://localhost:7xxx` öffnen (der Port steht in der Konsolenausgabe) und zum Beispiel
fragen:

- „Umsatz pro Monat"
- „Umsatz nach Vertriebskanal"
- „Wie viele Auftraege sind offen?"
- „Durchschnittlicher Auftragswert je Quartal"

Unter **Datenmodell** steht, was überhaupt gefragt werden kann — dieselben Namen, die auch das
Sprachmodell sieht.

### Ergebnisse behalten

Unter jeder Antwort stehen zwei Aktionen:

- **Auf Dashboard speichern** — legt die Abfrage als Kachel ab. *Bei jedem Öffnen neu berechnen*
  ist die Voreinstellung; *einfrieren* hält das Ergebnis fest und zeigt immer sein Datum dazu.
- **Als CSV herunterladen** — führt die Abfrage erneut aus und liefert die Datei gestreamt.
  Semikolon und UTF-8-BOM, damit Excel in deutscher Einstellung sie ohne Nacharbeit öffnet.

Gespeichert wird stets die **Abfrage**, nicht die Antwort. Ein Dashboard mit Zahlen vom letzten
Monat ist schlimmer als keins, weil es aktuell aussieht.

### Was der Prototyp noch nicht kann

- **Keine Joins.** Fragen bleiben innerhalb einer Entität. „Umsatz pro Monat" geht, „Umsatz nach
  Land" nicht, weil das Auftrag ⋈ Kunde bräuchte.
- **Kein Scaffolding.** Das Fachmodell unter `semantics/` ist von Hand gepflegt. Es hat bereits
  exakt die Form, die der Generator später erzeugen wird.
- **Oracle läuft nicht mit.** Dialekt und Treiber sind vorhanden und getestet, im Prototyp wird
  aber nur PostgreSQL hochgefahren.
- **Ein fester Mandant.** Der Wert für die Row-Policy kommt aus `appsettings.json` statt aus dem
  angemeldeten Nutzer. Der Mechanismus ist derselbe, nur die Quelle des Werts ist vorläufig.

### Konfiguration

Alles unter `Nltsql` in `appsettings.json`: `Semantics:Directory` (wo die Modelle liegen),
`Ai:Endpoint` und `Ai:ChatModel`, `DataSources:Sources` (Zieldatenbanken), `QueryContext:PolicyValues`
(Mandantenwerte) und `QueryLimits`. Die Optionen werden beim Start geprüft — eine falsche
Konfiguration verhindert den Start, statt bei der ersten Frage aufzufallen.

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
