-- Demo sales domain for local development and integration tests.
--
-- Two things here exist specifically to exercise the scaffolding pipeline:
--   * COMMENT ON statements, which are the best fachliche descriptions available
--     from a database and are picked up verbatim by the introspector.
--   * mandant_id on every table, so row policies have something real to enforce.

CREATE SCHEMA IF NOT EXISTS vertrieb;

CREATE TABLE vertrieb.kunde (
    id              bigint PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    mandant_id      integer     NOT NULL,
    kundennummer    varchar(20) NOT NULL UNIQUE,
    name            varchar(200) NOT NULL,
    land            char(2)     NOT NULL,
    branche         varchar(60) NOT NULL,
    segment         varchar(20) NOT NULL,
    angelegt_am     date        NOT NULL
);

COMMENT ON TABLE  vertrieb.kunde              IS 'Auftraggeber. Enthaelt auch inaktive Kunden.';
COMMENT ON COLUMN vertrieb.kunde.mandant_id   IS 'Mandant, dem der Kunde gehoert. Trennkriterium fuer die Mandantenfaehigkeit.';
COMMENT ON COLUMN vertrieb.kunde.kundennummer IS 'Fachliche Kundennummer aus dem ERP.';
COMMENT ON COLUMN vertrieb.kunde.land         IS 'ISO-3166-1-alpha-2 Laenderkennzeichen des Rechnungssitzes.';
COMMENT ON COLUMN vertrieb.kunde.branche      IS 'Branchenschluessel des Kunden.';
COMMENT ON COLUMN vertrieb.kunde.segment      IS 'Vertriebssegment: KMU, Mittelstand oder Konzern.';

CREATE TABLE vertrieb.produkt (
    id              bigint PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    mandant_id      integer     NOT NULL,
    artikelnummer   varchar(30) NOT NULL UNIQUE,
    bezeichnung     varchar(200) NOT NULL,
    produktgruppe   varchar(60) NOT NULL,
    listenpreis     numeric(12,2) NOT NULL,
    aktiv           boolean     NOT NULL DEFAULT true
);

COMMENT ON TABLE  vertrieb.produkt               IS 'Verkaufbare Artikel des Produktkatalogs.';
COMMENT ON COLUMN vertrieb.produkt.listenpreis   IS 'Bruttolistenpreis in EUR, ohne kundenspezifische Rabatte.';
COMMENT ON COLUMN vertrieb.produkt.produktgruppe IS 'Oberste Ebene der Produkthierarchie.';

CREATE TABLE vertrieb.auftrag (
    id              bigint PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    mandant_id      integer     NOT NULL,
    auftragsnummer  varchar(20) NOT NULL UNIQUE,
    kunde_id        bigint      NOT NULL REFERENCES vertrieb.kunde (id),
    bestelldatum    date        NOT NULL,
    lieferdatum     date,
    status          varchar(20) NOT NULL,
    vertriebskanal  varchar(20) NOT NULL,
    nettobetrag     numeric(14,2) NOT NULL,
    rabattbetrag    numeric(14,2) NOT NULL DEFAULT 0
);

COMMENT ON TABLE  vertrieb.auftrag                IS 'Auftragskopf. Ein vom Kunden erteilter Auftrag.';
COMMENT ON COLUMN vertrieb.auftrag.status         IS 'Auftragsstatus: offen, bestaetigt, versendet, storniert.';
COMMENT ON COLUMN vertrieb.auftrag.vertriebskanal IS 'Kanal der Auftragserfassung: online, aussendienst, partner.';
COMMENT ON COLUMN vertrieb.auftrag.nettobetrag    IS 'Nettoauftragswert in EUR ohne Umsatzsteuer, nach Rabatt.';
COMMENT ON COLUMN vertrieb.auftrag.rabattbetrag   IS 'Gewaehrter Rabatt in EUR, bereits im Nettobetrag beruecksichtigt.';
COMMENT ON COLUMN vertrieb.auftrag.lieferdatum    IS 'Tatsaechliches Lieferdatum. NULL solange nicht geliefert.';

CREATE TABLE vertrieb.auftragsposition (
    id              bigint PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    mandant_id      integer     NOT NULL,
    auftrag_id      bigint      NOT NULL REFERENCES vertrieb.auftrag (id),
    produkt_id      bigint      NOT NULL REFERENCES vertrieb.produkt (id),
    positionsnummer integer     NOT NULL,
    menge           numeric(12,3) NOT NULL,
    einzelpreis     numeric(12,2) NOT NULL,
    positionsbetrag numeric(14,2) NOT NULL,
    UNIQUE (auftrag_id, positionsnummer)
);

COMMENT ON TABLE  vertrieb.auftragsposition                 IS 'Einzelposition eines Auftrags.';
COMMENT ON COLUMN vertrieb.auftragsposition.menge           IS 'Bestellte Menge in der Basismengeneinheit des Artikels.';
COMMENT ON COLUMN vertrieb.auftragsposition.positionsbetrag IS 'Menge mal Einzelpreis in EUR, netto.';

CREATE INDEX ix_auftrag_kunde        ON vertrieb.auftrag (kunde_id);
CREATE INDEX ix_auftrag_bestelldatum ON vertrieb.auftrag (bestelldatum);
CREATE INDEX ix_position_auftrag     ON vertrieb.auftragsposition (auftrag_id);
CREATE INDEX ix_position_produkt     ON vertrieb.auftragsposition (produkt_id);

-- A dedicated read-only role. The application never connects with an account that
-- can write to a target database; this is the account the query engine uses.
CREATE ROLE nltsql_reader LOGIN PASSWORD 'devonly';
GRANT USAGE ON SCHEMA vertrieb TO nltsql_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA vertrieb TO nltsql_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA vertrieb GRANT SELECT ON TABLES TO nltsql_reader;
