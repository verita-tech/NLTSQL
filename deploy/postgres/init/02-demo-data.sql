-- Deterministic demo data. setseed makes every developer machine and CI run produce
-- byte-identical rows, so integration tests can assert on concrete aggregates.

SELECT setseed(0.42);

INSERT INTO vertrieb.kunde (mandant_id, kundennummer, name, land, branche, segment, angelegt_am)
SELECT
    1 + (i % 2),
    'K' || lpad(i::text, 6, '0'),
    'Kunde ' || i,
    (ARRAY['DE','AT','CH','NL','FR'])[1 + (i % 5)],
    (ARRAY['Maschinenbau','Handel','Logistik','Chemie','Automotive'])[1 + (i % 5)],
    (ARRAY['KMU','Mittelstand','Konzern'])[1 + (i % 3)],
    DATE '2019-01-01' + ((i * 7) % 2000)
FROM generate_series(1, 400) AS i;

INSERT INTO vertrieb.produkt (mandant_id, artikelnummer, bezeichnung, produktgruppe, listenpreis, aktiv)
SELECT
    1 + (i % 2),
    'A' || lpad(i::text, 6, '0'),
    'Artikel ' || i,
    (ARRAY['Antriebstechnik','Sensorik','Hydraulik','Elektronik','Zubehoer'])[1 + (i % 5)],
    round((25 + (i % 400) * 3.75)::numeric, 2),
    (i % 11) <> 0
FROM generate_series(1, 120) AS i;

INSERT INTO vertrieb.auftrag (
    mandant_id, auftragsnummer, kunde_id, bestelldatum, lieferdatum,
    status, vertriebskanal, nettobetrag, rabattbetrag)
SELECT
    k.mandant_id,
    'AU' || lpad(i::text, 8, '0'),
    k.id,
    d.bestelldatum,
    CASE WHEN s.status IN ('versendet') THEN d.bestelldatum + (3 + (i % 12)) END,
    s.status,
    (ARRAY['online','aussendienst','partner'])[1 + (i % 3)],
    round((320 + (i % 900) * 7.25)::numeric, 2),
    round(((i % 40) * 4.5)::numeric, 2)
FROM generate_series(1, 6000) AS i
CROSS JOIN LATERAL (SELECT DATE '2023-01-01' + ((i * 3) % 950) AS bestelldatum) AS d
CROSS JOIN LATERAL (
    SELECT (ARRAY['offen','bestaetigt','versendet','versendet','versendet','storniert'])[1 + (i % 6)] AS status
) AS s
CROSS JOIN LATERAL (
    SELECT * FROM vertrieb.kunde OFFSET (i % 400) LIMIT 1
) AS k;

INSERT INTO vertrieb.auftragsposition (
    mandant_id, auftrag_id, produkt_id, positionsnummer, menge, einzelpreis, positionsbetrag)
SELECT
    a.mandant_id,
    a.id,
    p.id,
    pos.n,
    m.menge,
    p.listenpreis,
    round(m.menge * p.listenpreis, 2)
FROM vertrieb.auftrag AS a
CROSS JOIN LATERAL generate_series(1, 1 + (a.id % 4)) AS pos(n)
CROSS JOIN LATERAL (
    SELECT * FROM vertrieb.produkt OFFSET ((a.id * 7 + pos.n) % 120) LIMIT 1
) AS p
CROSS JOIN LATERAL (SELECT (1 + ((a.id + pos.n) % 25))::numeric(12,3) AS menge) AS m;

ANALYZE;
