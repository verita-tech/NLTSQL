# NLTSQL — Self-Service-Datenabfrage mit semantischer Schicht

Prototyp einer Plattform, auf der Kunden ihre Datenbank selbst auswerten:
Frage stellen oder Abfrage zusammenklicken, Ergebnis als Tabelle und
Diagramm, Export als CSV, Ablage auf einem Dashboard und Übergabe an
Metabase zum Weiterfiltern.

Der Kern ist bewusst nicht die Oberfläche, sondern die **semantische
Schicht dazwischen**: Kennzahlen wie die OEE sind genau einmal definiert
— in Cube — und liefern in der Anwendung, im Diagramm, im CSV und in
Metabase denselben Wert.

---

## Architektur

```mermaid
flowchart TB
    User["Kunde im Browser"]

    subgraph App["Blazor Server · Fluent UI 4.10"]
        NL["Frage in natürlicher Sprache"]
        Builder["Abfrage-Editor"]
        Val["Validierung gegen das Modell"]
        Store[("Gespeicherte Abfragen<br/>und Dashboards")]
    end

    Planner["Ollama · lokales Modell<br/>(strukturierte Ausgabe)"]

    subgraph Semantic["Cube · semantische Schicht"]
        Views["Views: fertigung, stillstaende"]
        Metrics["Kennzahlendefinitionen<br/>OEE, Ausschussquote, MTTR"]
        Rewrite["queryRewrite:<br/>Mandantenfilter"]
    end

    DWH[("Data Warehouse<br/>PostgreSQL")]
    MB["Metabase 0.63"]

    User --> NL --> Planner --> Val
    User --> Builder --> Val
    Val -->|"Cube-Query (JSON)"| Semantic
    Val -->|"Cube-SQL"| MB
    Semantic --> DWH
    MB -->|"SQL-API, Postgres-Protokoll"| Semantic
    Semantic -->|Ergebnis| App
    MB -->|"signierter Embed"| User
    App --> Store
```

Zwei Wege führen in die semantische Schicht, und **beide** enden dort —
keiner davon spricht direkt mit dem Warehouse:

* Die Anwendung fragt Cube über die **REST-API** ab und rendert Tabelle
  und CSV selbst.
* Metabase ist als Datenbank an Cubes **SQL-API** (Postgres-Protokoll)
  angebunden. Eine Frage, die an Metabase übergeben wird, liest deshalb
  dieselben Kennzahlendefinitionen. Wer dort weiterfiltert, bekommt
  weiterhin die OEE aus dem Modell — und nicht eine zweite, abweichende
  Definition in einer Metabase-Frage.

---

## Warum eine semantische Schicht

In einer fachspezifischen Domäne ist die eigentliche Schwierigkeit nicht
SQL, sondern die Fachlichkeit. „OEE“ ist Verfügbarkeit × Leistungsgrad ×
Qualitätsrate, und jeder dieser Faktoren hat eine bestimmte Bezugsgröße.
Wird das in Abfragen, Dashboards und Exporten jeweils neu formuliert,
driften die Zahlen auseinander — meistens unbemerkt.

Deshalb liegt die Fachlichkeit in `infra/cube/model/` und nirgends sonst:

* **Kennzahlen als Quotient zweier additiver Summen.** `scrap_rate` ist
  `Summe(Ausschuss) / Summe(Gesamtmenge)`, nicht der Mittelwert der
  Schichtquoten. Nur so stimmt die Zahl auf jeder Aggregationsebene, und
  nur so lässt sie sich aus einer Pre-Aggregation beantworten.
* **Views als einziger Vertrag.** Die zugrundeliegenden Cubes sind
  `public: false`. Physische Joins und Spalten können sich ändern, ohne
  eine gespeicherte Abfrage oder eine Metabase-Frage zu brechen.
* **Fachvokabular am Modell.** Jedes Feld trägt Titel, Beschreibung und
  Synonyme (`Anlageneffektivität`, `Ausschussrate`, `MTTR`). Daraus
  entsteht der Katalog für die Frageeingabe — wächst das Modell, wächst
  das Sprachverständnis mit, ohne Codeänderung.

---

## Sicherheit

| Thema | Umsetzung |
|---|---|
| Kein SQL aus der Oberfläche | Es gibt keine Methode, die SQL entgegennimmt. Alles ist eine `SemanticQuery` über katalogisierte Felder. |
| KI läuft lokal | Der Planner spricht ausschließlich mit Ollama im eigenen Netz. Katalog, Fachbeschreibungen und Benutzerfragen gehen an keinen externen Dienst. |
| LLM-Ausgabe | Von Ollama auf ein JSON-Schema zwangsgeführt und anschließend gegen das *live* geladene Modell validiert. Unbekannte Felder werden abgelehnt, nicht ausgeführt. |
| Mandantentrennung | Der Server signiert pro Anfrage ein kurzlebiges JWT mit dem Security-Context. Cube hängt in `queryRewrite` den Filter an — *nach* dem Parsen, also nicht entfernbar. |
| Datenbankzugriff | Cube verbindet sich mit einer reinen Lese-Rolle (`cube_reader`). |
| Metabase-Vorschau | Static Embedding mit serverseitig signiertem Token. Der Browser hält keine Metabase-Session. |
| CSV-Download | Kurzlebiges, mandantengebundenes Ticket statt Abfrage in der URL. |

### Zertifikate bei internen Diensten

Laufen Cube, Metabase oder Ollama über HTTPS mit einem selbstsignierten
Zertifikat oder dem einer internen CA, lehnt .NET die Verbindung ab. Dafür
gibt es zwei Wege — sie sind nicht gleichwertig:

**Empfohlen: Zertifikat hinterlegen.** Die Verbindung bleibt geprüft, es
wird lediglich genau dieses eine Zertifikat zusätzlich akzeptiert.

```jsonc
"Planner": {
  "Tls": {
    "TrustedCertificateThumbprints": [ "A1:B2:C3:…" ]
  }
}
```

Fingerabdruck auslesen:

```bash
openssl s_client -connect ollama.intern:443 </dev/null 2>/dev/null   | openssl x509 -fingerprint -sha256 -noout
```

Doppelpunkte und Groß-/Kleinschreibung spielen keine Rolle. Wird das
Zertifikat erneuert, muss der Wert nachgezogen werden — das ist der Preis
dieser Variante.

**Notlösung: Prüfung abschalten.**

```jsonc
"Planner":  { "Tls": { "DangerousAcceptAnyServerCertificate": true } },
"Metabase": { "Tls": { "DangerousAcceptAnyServerCertificate": true } }
```

Über Compose: `PLANNER_TLS_INSECURE=true` bzw. `METABASE_TLS_INSECURE=true`.

Der Name ist bewusst unbequem. Der Datenverkehr bleibt verschlüsselt,
aber es wird nicht mehr geprüft, **mit wem** verschlüsselt wird — wer sich
im Netz dazwischenschalten kann, liest mit und kann Antworten verändern.
Beim Planner ginge dabei der komplette Fachkatalog samt Benutzerfragen
mit, bei Metabase der API-Schlüssel. Die Anwendung schreibt beim Start
für jeden betroffenen Client eine Warnung ins Log, damit die Einstellung
den Test nicht überlebt, für den sie gedacht war.

Ein Sonderfall: die **Diagrammvorschau** lädt der Browser direkt von
`Metabase:PublicUrl`. Ein Zertifikat, dem der Browser nicht traut, bleibt
dort ein Problem — unabhängig von dieser Einstellung.

Der Prototyp hat **keine Benutzeranmeldung**: `ITenantContext` kommt aus
der Konfiguration. Das ist die eine Stelle, die für einen echten Betrieb
zu ersetzen ist — alles dahinter benutzt bereits diese Abstraktion.

---

## Voraussetzungen

* Docker und Docker Compose
* .NET SDK 9.0 (nur, wenn die App außerhalb von Compose laufen soll)
* Für die Frageeingabe: Ollama ≥ 0.5 (im Stack enthalten) und ausreichend
  Arbeitsspeicher für das Modell — rund 6 GB für die 7B-Voreinstellung

---

## Einrichtung

### 1. Konfiguration anlegen

```bash
cp .env.example .env
```

Danach in `.env` echte Werte setzen. Für die Secrets:

```bash
openssl rand -hex 32
```

Mindestens nötig: `POSTGRES_PASSWORD`, `CUBEJS_API_SECRET` (≥ 32
Zeichen), `CUBEJS_SQL_PASSWORD`, `METABASE_EMBEDDING_SECRET_KEY`.

### 2. Infrastruktur starten

```bash
docker compose up -d
```

Das startet das Demo-Warehouse (Schema und Daten werden beim ersten Start
eingespielt), Cube und Metabase. Metabase braucht beim ersten Start etwa
eine Minute.

### 3. Metabase verbinden

```bash
set -a && source .env && set +a
python3 infra/metabase/bootstrap.py
```

Das Skript legt den Admin-Benutzer an, erzeugt einen API-Key, schaltet
Static Embedding frei und registriert Cubes SQL-API als Datenbank. Am
Ende gibt es drei Werte aus, die in `.env` gehören:

```
METABASE_API_KEY=...
METABASE_EMBEDDING_SECRET_KEY=...
METABASE_CUBE_DATABASE_ID=...
```

`METABASE_EMBEDDING_SECRET_KEY` muss mit dem Wert übereinstimmen, den
Compose als `MB_EMBEDDING_SECRET_KEY` gesetzt hat — sonst weist Metabase
die signierten Embed-Tokens ab und die Vorschau bleibt leer.

Dieselben Schritte gehen auch von Hand über die Metabase-Oberfläche:
*Admin → Einstellungen → Authentifizierung → API-Schlüssel*,
*Admin → Einstellungen → Einbetten → Statisches Einbetten* und
*Admin → Datenbanken → Datenbank hinzufügen* (PostgreSQL, Host `cube`,
Port `15432`, Datenbank `cube`).

### 4. Anwendung starten

Aus der IDE oder per CLI:

```bash
dotnet run --project src/Nltsql.Web
```

Oder mitsamt Container:

```bash
docker compose --profile app up -d
```

Die App liegt dann auf <http://localhost:8080>, Metabase auf
<http://localhost:3000>, der Cube-Playground auf <http://localhost:4000>.

### 5. Frageeingabe aktivieren (optional)

Die Frageeingabe läuft über ein **lokales Modell in Ollama**. Weder der
semantische Katalog noch die Frage des Benutzers verlässt das Netz.

Einmalig das Modell holen (mehrere GB):

```bash
docker compose --profile setup up ollama-pull
```

Dann in `.env` setzen:

```
PLANNER_ENABLED=true
PLANNER_MODEL=qwen2.5:7b-instruct
```

Läuft die App außerhalb von Compose, entsprechend `Planner:Enabled=true`
in `appsettings.Development.json` oder per User-Secrets.

**Ollama hinter einem Gateway.** Steht der Modellserver nicht auf der
Loopback-Schnittstelle, sondern hinter einer Absicherung, sendet die
Anwendung feste Header mit:

```jsonc
"Planner": {
  "BaseUrl": "https://ollama.intern",
  "ApiKey": "…",            // Header: X-Api-Key
  "UserToken": "…",         // Header: X-User-Token
  "ApiKeyHeader": "X-Api-Key",
  "UserTokenHeader": "X-User-Token",
  "DefaultHeaders": { "X-Request-Source": "nltsql" }
}
```

Die Headernamen sind konfigurierbar, weil Gateways sich hier nicht einig
sind. Für ein Bearer-Schema genügt `"ApiKeyHeader": "Authorization"` mit
`"ApiKey": "Bearer …"`. Ein leer gelassener Wert bedeutet: der Header
entfällt ganz — ein vorhandener, aber leerer Header wird von manchen
Gateways als fehlgeschlagener Anmeldeversuch gewertet. Über Compose:
`PLANNER_API_KEY` und `PLANNER_USER_TOKEN`.

**Modellwahl.** Nötig ist ein instruktionsgetuntes Modell, das sich an
ein JSON-Schema hält. Die 7B-Klasse ist die kleinste, die zuverlässig das
richtige Feld aus einem Fachkatalog wählt; darunter werden so viele Pläne
von der Validierung abgelehnt, dass es stört. Mit mehr Speicher ist
`qwen2.5:14b-instruct` spürbar treffsicherer. Ohne GPU dauert die erste
Antwort je nach Hardware bis zu einer Minute.

**Ein Wert, der wirklich passen muss:** `Planner:ContextTokens`. Ollama
begrenzt eine Anfrage sonst auf einen kleinen Standardkontext und
schneidet den Prompt **vorne** ab — genau dort steht der Katalog. Das
gibt keine Fehlermeldung, sondern ein Modell, das Feldnamen erfindet.
Der Wert muss deutlich über der Promptgröße liegen; bei einem großen
Modell mit vielen Kennzahlen sind 16384 angebracht.

Ohne aktivierte Frageeingabe bleibt die Plattform vollständig benutzbar —
nur das Frage-Feld ist ausgeblendet. Der Abfrage-Editor kann alles, was
die Frageeingabe auch erzeugen könnte.

---

## Was der Prototyp kann

* **Fragen stellen.** „OEE je Maschine in den letzten 30 Tagen“ wird in
  eine Abfrage übersetzt. Darüber steht immer, *wie* die Frage verstanden
  wurde, und darunter die tatsächlich erzeugte Abfrage — beides
  korrigierbar, bevor jemand der Zahl vertraut.
* **Abfragen bauen.** Datenbereich, Kennzahlen, Merkmale, Zeitraum,
  Filter, Sortierung, Zeilenlimit.
* **Ergebnis ansehen.** Tabelle mit fachlichen Titeln, Prozentwerten und
  deutscher Zahlenformatierung; Hinweis, wenn ein Limit gegriffen hat.
* **Diagramm.** Metabase erzeugt die Vorschau, eingebettet in der Seite.
  Der Diagrammtyp wird aus der Form der Abfrage vorgeschlagen.
* **In Metabase weiterarbeiten.** Frage öffnen oder direkt auf ein
  Metabase-Dashboard legen.
* **Speichern.** Gespeichert wird die *Abfrage*, nicht das Ergebnis. Beim
  Öffnen läuft sie erneut — ein Dashboard zeigt damit immer den aktuellen
  Stand unter den aktuellen Definitionen.
* **CSV-Export.** Voreingestellt so, wie Excel mit deutschen
  Regionaleinstellungen es ohne Importdialog liest: Semikolon,
  Dezimalkomma, UTF-8 mit BOM.

---

## Demo-Domäne

Serienfertigung und Instandhaltung. Das Warehouse ist ein kleines
Sternschema mit deterministisch erzeugten Daten über rund 18 Monate.

Zwei Datenbereiche:

* **`fertigung`** — Mengen, Zeiten und die OEE-Faktoren je Maschine,
  Produkt, Schicht und Werk.
* **`stillstaende`** — Stillstandsereignisse nach Störgrund, geplant und
  ungeplant, inklusive MTTR.

In den Daten steckt ein Befund: **CNC-Zentrum 2** hat in den letzten 120
Tagen eine deutlich erhöhte Ausschussquote (rund 8 % gegenüber sonst
2,7 %). Gute erste Frage: *„Ausschussquote je Maschine pro Monat im
letzten Jahr“*.

Zum Austausch gegen echte Kundendaten sind zwei Dinge zu ersetzen: das
Warehouse-Schema unter `infra/warehouse/` und das Modell unter
`infra/cube/model/`. Die Anwendung kennt keine Tabellennamen.

---

## Projektstruktur

```
infra/
  warehouse/       Schema und Demodaten des Warehouses
  cube/model/      Semantische Schicht: Cubes (privat) und Views (öffentlich)
  cube/cube.js     Mandantentrennung via queryRewrite
  metabase/        Einrichtungsskript
src/
  Nltsql.Core/             Domäne: Abfragevertrag, Validierung, SQL-Rendering
  Nltsql.Infrastructure/   Cube-, Metabase- und Planner-Anbindung, Persistenz, CSV
  Nltsql.Web/              Blazor Server mit Fluent UI
tests/
  Nltsql.Tests/            Tests für Validierung, SQL, Mapping und Export
```

`Nltsql.Core` hat bewusst **keine** Paketabhängigkeiten: Abfragevertrag,
Validierung und SQL-Erzeugung sind ohne Cube, Metabase, Datenbank oder
Sprachmodell testbar.

---

## Entwicklung

```bash
dotnet build          # Warnungen sind Fehler (Directory.Build.props)
dotnet test
```

Weitere Hinweise zu den Entwurfsentscheidungen: [`docs/entscheidungen.md`](docs/entscheidungen.md).

---

## Stand des Prototyps

Bewusst offen gelassen, weil es den Rahmen eines leichtgewichtigen
Prototyps sprengen würde:

* **Keine Authentifizierung.** Mandant und Datenbereich kommen aus der
  Konfiguration. Ersatzpunkt ist `ITenantContext`.
* **`EnsureCreated` statt Migrationen.** Für einen geteilten Betrieb auf
  `MigrateAsync` umstellen und eine erste Migration erzeugen.
* **SQLite als Anwendungsspeicher.** Trägt Prototyp-Last; für
  Mehrbenutzerbetrieb auf PostgreSQL wechseln (nur der Connection-String
  und das Provider-Paket).
* **Interne Dashboards zeigen Tabellen.** Die Diagrammansicht läuft über
  Metabase; interne Kacheln rendern das Ergebnis als Tabelle.
* **Pre-Aggregations sind definiert, aber nicht eingeplant.** Für große
  Datenmengen einen Refresh-Worker konfigurieren.
* **Qualität der Frageeingabe hängt am lokalen Modell.** Ein 7B-Modell
  trifft seltener als ein großes Cloud-Modell. Der Entwurf fängt das ab
  — jeder Plan wird validiert, ein abgelehnter geht mit den konkreten
  Fehlern zurück — aber manche Frage lässt sich schneller im Editor
  zusammenstellen als umformulieren.

Ehrlichkeitshinweis zur Verifikation: .NET-Build und Tests (71) laufen
nachweislich durch, die Anwendung startet und alle Seiten rendern, und
das Warehouse-SQL wurde gegen ein echtes PostgreSQL 16 eingespielt —
inklusive Prüfung, dass zwei Durchläufe byte-identische Daten erzeugen. Cube, Metabase und Ollama konnten in der
Entwicklungsumgebung nicht gestartet werden (kein Netzzugriff auf die
Images bzw. die Modellregistry). Diese drei Anbindungen sind anhand der
dokumentierten APIs implementiert und mit Tests gegen aufgezeichnete
Antworten abgesichert — beim Ollama-Client inklusive des gesendeten
Request-Formats und der Reparaturschleife. Erprobt gegen laufende
Instanzen sind sie nicht; beim ersten Start ist mit kleineren
Anpassungen zu rechnen.
