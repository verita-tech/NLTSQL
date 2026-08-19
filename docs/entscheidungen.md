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

**Preis:** Ein Stück Zugriffslogik liegt in JavaScript neben dem Modell
statt im C#-Code. Das ist die richtige Ebene: es ist die einzige, durch
die alle Leser hindurchmüssen.
