# Entwurfsentscheidungen

Kurze Begründungen zu den Festlegungen, die im Code nicht selbsterklärend
sind. Jede Entscheidung nennt auch, was sie kostet.

---

## 1. Die Anwendung erzeugt kein SQL für die Datenabfrage

`ISemanticLayer` hat bewusst keine Methode, die SQL entgegennimmt. Alles,
was die Oberfläche fragen kann, muss als `SemanticQuery` über
katalogisierte Felder ausdrückbar sein.

**Warum:** Damit ist die Menge der möglichen Abfragen endlich und
prüfbar. Ein Sprachmodell, das SQL schreibt, muss man gegen Injection,
Kreuzprodukte und falsch aggregierte Kennzahlen absichern; ein
Sprachmodell, das aus einem Katalog auswählt, kann nur Dinge auswählen,
die es gibt.

**Preis:** Was das semantische Modell nicht hergibt, ist nicht abfragbar.
Bei einer neuen Frageart wird das Modell erweitert, nicht die Abfrage.
Das ist der beabsichtigte Weg — es hält die Fachlichkeit an einer Stelle.

---

## 2. Metabase liest über Cubes SQL-API, nicht über das Warehouse

Die Metabase-Frage enthält SQL gegen Cubes SQL-API
(`SELECT machine_name, MEASURE(oee) FROM fertigung ...`), nicht gegen die
Warehouse-Tabellen.

**Warum:** Der Wunsch, „in Metabase weiterzufiltern“, ist sonst genau die
Stelle, an der die Kennzahlen auseinanderlaufen. Über `MEASURE()` bleibt
die Aggregation in der Verantwortung der semantischen Schicht — auch
dann, wenn jemand in Metabase Filter ergänzt oder eine Spalte
gruppiert.

**Preis:** Cubes SQL-API unterstützt nicht jedes SQL-Konstrukt, das
Metabase gegen eine echte Postgres-Datenbank erlauben würde. Für einen
Prototyp ist das der richtige Tausch.

---

## 3. Rollierende Zeiträume bleiben symbolisch

„Letzte 30 Tage“ wird als `RelativeDateRange.Last30Days` gespeichert und
erst beim Ausführen aufgelöst — in der Cube-Abfrage als `last 30 days`,
im erzeugten SQL als `NOW() - INTERVAL '30 days'`.

**Warum:** Eine Dashboard-Kachel, die beim Speichern auf feste Daten
festgeschrieben würde, zeigt drei Monate später stillschweigend
veraltete Zahlen. Die Übersetzung liegt in einer Klasse
(`RelativeRange`), damit derselbe Zeitraum in Tabelle und Metabase-Karte
nicht unterschiedlich ausfallen kann.

**Preis:** Die Menge der Zeiträume ist geschlossen. Freitext wie „seit
dem Werksurlaub“ ist nicht darstellbar.

---

## 4. Kennzahlen als Quotient additiver Summen

Jede Verhältniskennzahl im Modell ist `Summe(A) / Summe(B)` und nie ein
Mittelwert von Verhältnissen.

**Warum:** Zwei Gründe, die zusammenfallen. Fachlich ist der Mittelwert
von Schichtquoten falsch, sobald die Schichten unterschiedlich viel
produziert haben. Technisch kann Cube eine solche Kennzahl aus einer
Pre-Aggregation der additiven Bestandteile beantworten — ein
vorberechneter Mittelwert ließe sich nicht weiter aggregieren.

**Preis:** Die Zerlegung muss beim Modellieren durchgehalten werden. Für
den Leistungsgrad heißt das, die Sollzykluszeit über eine
`sql:`-Definition an die Faktentabelle zu holen, statt `sql_table:` zu
verwenden.

---

## 5. Gespeichert wird die Abfrage, nicht das Ergebnis

Eine Dashboard-Kachel führt ihre Abfrage bei jedem Öffnen erneut aus.

**Warum:** Die Anforderung war, Ergebnisse „jedes Mal wieder abzurufen“.
Ein eingefrorenes Ergebnis wäre die schlechtere Lesart: es veraltet, und
es überlebt eine Korrektur der Kennzahlendefinition, ohne sie
mitzubekommen.

**Preis:** Jedes Öffnen kostet eine Abfrage. Dagegen stehen Cubes
Pre-Aggregations und Ergebnis-Cache; für ein Dashboard mit vielen
Kacheln lohnt sich ein Blick auf beides.

---

## 6. Validierung als eine Stelle für drei Quellen

`SemanticQueryValidator` prüft Eingaben aus der Oberfläche, gespeicherte
Abfragen und Planner-Ausgaben mit demselben Code.

**Warum:** Eine gespeicherte Abfrage ist genauso wenig vertrauenswürdig
wie eine generierte — sie wurde nur zu einem Zeitpunkt geschrieben, an
dem das Modell anders aussah. Die Fehlermeldungen sind deutschsprachig
und nennen das betroffene Feld, weil sie zwei Adressaten haben: den
Benutzer in der Oberfläche und den Planner im Reparaturversuch.

**Preis:** Die Prüfung läuft auch im Ausführungspfad noch einmal. Das ist
gemessen an einer Warehouse-Abfrage nicht messbar.

---

## 7. Fluent-UI-DataGrid nur für typisierte Listen

Das Ergebnisraster ist eine einfache HTML-Tabelle; `FluentDataGrid` wird
für die Liste gespeicherter Abfragen verwendet.

**Warum:** Die Spalten eines Ergebnisses stehen erst zur Laufzeit fest
und ändern sich mit jeder Abfrage. Genau das ist der Fall, in dem ein
typisiertes Grid mehr Umweg als Nutzen ist.

**Preis:** Sortieren und Paginieren im Ergebnisraster müssten selbst
gebaut werden. Für einen Prototyp mit Zeilenlimit und CSV-Export ist das
nicht nötig.

---

## 8. Mandantenfilter in Cube statt in der Anwendung

Der Filter wird in `cube.js` in `queryRewrite` angehängt, nicht beim
Erzeugen der Abfrage.

**Warum:** `queryRewrite` läuft *nach* dem Parsen der eingehenden
Abfrage. Der Aufrufer kann den Filter also nicht weglassen — und das gilt
auch für Abfragen, die Metabase über die SQL-API stellt, die durch die
Anwendung gar nicht hindurchlaufen.

**Der Haken, den diese Entscheidung ursprünglich übersehen hat:**
`queryRewrite` bekommt seinen Security-Context auf dem REST-Pfad aus dem
JWT, das die Anwendung signiert. Auf dem SQL-Pfad gibt es kein JWT. Ohne
eigenes `checkSqlAuth` reicht Cube dort einen *leeren* Context durch, und
`queryRewrite` bricht folgerichtig mit „no tenant_id“ ab — also **jede**
Metabase-Abfrage. Die Absicherung, die dieser Abschnitt behauptete, gab es
auf diesem Pfad zunächst schlicht nicht. `checkSqlAuth` in `cube.js`
schließt die Lücke; `infra/cube/cube.test.js` hält sie geschlossen.

**Preis:** Ein Stück Zugriffslogik liegt in JavaScript neben dem Modell
statt im C#-Code. Das ist die richtige Ebene: es ist die einzige, durch
die alle Leser hindurchmüssen. Dazu kommt: Metabase authentifiziert sich
mit einem statischen SQL-Benutzer, der Metabase-Pfad kennt also genau
einen Mandanten. Mehr braucht einen SQL-Benutzer je Mandant plus
`canSwitchSqlUser` — die Filterlogik selbst bliebe unverändert.

---

## 9. Das Sprachmodell läuft lokal

Die Frageeingabe spricht mit Ollama auf `localhost`, nicht mit einer
Cloud-API.

**Warum:** Der Prompt besteht zum größten Teil aus dem generierten
Feldkatalog — Kennzahlennamen, Beschreibungen, Synonyme. In einer
fachspezifischen Domäne ist das nicht Beiwerk, sondern das
Geschäftsvokabular des Kunden. Es bei jeder Frage an einen fremden Dienst
zu schicken, ist eine Entscheidung, die man bewusst treffen sollte; hier
fällt sie zugunsten der Datenhoheit. Nebeneffekt: keine Kosten je Frage
und keine Abhängigkeit von einer Netzverbindung.

**Preis:** Ein 14B-Modell hält ein JSON-Schema schlechter ein als eine
serverseitig validierte Structured-Output-API. Zwei Stellen zahlen das ab:
`JsonPayload` schneidet Markdown-Zäune und Prosa weg, und das Schema
bleibt im konservativen Teilbereich, den llama.cpps Grammatik-Übersetzung
zuverlässig abbildet — keine Typ-Unions, keine `enum`-Listen mit `null`.
`PlanSchemaTests` hält beides fest. Dazu kommen längere Antwortzeiten und
ein Rechner, der das Modell tragen muss.

---

## 10. Weiterfiltern in Metabase über „Explore results“

Die erzeugte Metabase-Karte ist eine native SQL-Frage ohne Parameter. Wer
weiterfiltern will, geht in Metabase über „Explore results“.

**Warum:** Die Alternative wäre, Filter als Template-Tags an der Karte zu
modellieren. Das kann Metabase, es ist aber eine zweite Stelle, an der
Fachlichkeit entsteht — und damit genau das, was die semantische Schicht
verhindern soll. Über `MEASURE()` bleibt die Aggregation in Cubes
Verantwortung, egal was in Metabase darüber gelegt wird.

**Preis:** „Explore results“ filtert das bereits verdichtete Ergebnis, nicht
die Quelle. Eine andere Gruppierung erfordert eine geänderte Abfrage in der
Anwendung. Und im eingebetteten Frame ist bewusst kein Drill-Down möglich —
Static Embedding ist lesend. Beides steht in der Oberfläche neben dem Link,
statt Nutzer es herausfinden zu lassen.
