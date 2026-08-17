-- Oracle mirror of the PostgreSQL demo domain.
--
-- Deliberately the *same* domain in a different dialect: it is what proves the SQL
-- compiler produces equivalent results on both engines rather than merely compiling.
-- Scripts in this directory run as SYSTEM, so every object is schema-qualified.

ALTER SESSION SET CURRENT_SCHEMA = nltsql_demo;

CREATE TABLE nltsql_demo.kunde (
    id              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mandant_id      NUMBER(9)     NOT NULL,
    kundennummer    VARCHAR2(20)  NOT NULL UNIQUE,
    name            VARCHAR2(200) NOT NULL,
    land            CHAR(2)       NOT NULL,
    branche         VARCHAR2(60)  NOT NULL,
    segment         VARCHAR2(20)  NOT NULL,
    angelegt_am     DATE          NOT NULL
);

COMMENT ON TABLE  nltsql_demo.kunde              IS 'Auftraggeber. Enthaelt auch inaktive Kunden.';
COMMENT ON COLUMN nltsql_demo.kunde.mandant_id   IS 'Mandant, dem der Kunde gehoert. Trennkriterium fuer die Mandantenfaehigkeit.';
COMMENT ON COLUMN nltsql_demo.kunde.kundennummer IS 'Fachliche Kundennummer aus dem ERP.';
COMMENT ON COLUMN nltsql_demo.kunde.land         IS 'ISO-3166-1-alpha-2 Laenderkennzeichen des Rechnungssitzes.';
COMMENT ON COLUMN nltsql_demo.kunde.branche      IS 'Branchenschluessel des Kunden.';
COMMENT ON COLUMN nltsql_demo.kunde.segment      IS 'Vertriebssegment: KMU, Mittelstand oder Konzern.';

CREATE TABLE nltsql_demo.produkt (
    id              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mandant_id      NUMBER(9)     NOT NULL,
    artikelnummer   VARCHAR2(30)  NOT NULL UNIQUE,
    bezeichnung     VARCHAR2(200) NOT NULL,
    produktgruppe   VARCHAR2(60)  NOT NULL,
    listenpreis     NUMBER(12,2)  NOT NULL,
    aktiv           NUMBER(1)     DEFAULT 1 NOT NULL
);

COMMENT ON TABLE  nltsql_demo.produkt               IS 'Verkaufbare Artikel des Produktkatalogs.';
COMMENT ON COLUMN nltsql_demo.produkt.listenpreis   IS 'Bruttolistenpreis in EUR, ohne kundenspezifische Rabatte.';
COMMENT ON COLUMN nltsql_demo.produkt.produktgruppe IS 'Oberste Ebene der Produkthierarchie.';
COMMENT ON COLUMN nltsql_demo.produkt.aktiv         IS 'Kennzeichen ob der Artikel verkauft werden darf. 1 = aktiv, 0 = ausgelaufen.';

CREATE TABLE nltsql_demo.auftrag (
    id              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mandant_id      NUMBER(9)     NOT NULL,
    auftragsnummer  VARCHAR2(20)  NOT NULL UNIQUE,
    kunde_id        NUMBER        NOT NULL,
    bestelldatum    DATE          NOT NULL,
    lieferdatum     DATE,
    status          VARCHAR2(20)  NOT NULL,
    vertriebskanal  VARCHAR2(20)  NOT NULL,
    nettobetrag     NUMBER(14,2)  NOT NULL,
    rabattbetrag    NUMBER(14,2)  DEFAULT 0 NOT NULL,
    CONSTRAINT fk_auftrag_kunde FOREIGN KEY (kunde_id) REFERENCES nltsql_demo.kunde (id)
);

COMMENT ON TABLE  nltsql_demo.auftrag                IS 'Auftragskopf. Ein vom Kunden erteilter Auftrag.';
COMMENT ON COLUMN nltsql_demo.auftrag.status         IS 'Auftragsstatus: offen, bestaetigt, versendet, storniert.';
COMMENT ON COLUMN nltsql_demo.auftrag.vertriebskanal IS 'Kanal der Auftragserfassung: online, aussendienst, partner.';
COMMENT ON COLUMN nltsql_demo.auftrag.nettobetrag    IS 'Nettoauftragswert in EUR ohne Umsatzsteuer, nach Rabatt.';
COMMENT ON COLUMN nltsql_demo.auftrag.rabattbetrag   IS 'Gewaehrter Rabatt in EUR, bereits im Nettobetrag beruecksichtigt.';
COMMENT ON COLUMN nltsql_demo.auftrag.lieferdatum    IS 'Tatsaechliches Lieferdatum. NULL solange nicht geliefert.';

CREATE TABLE nltsql_demo.auftragsposition (
    id              NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mandant_id      NUMBER(9)     NOT NULL,
    auftrag_id      NUMBER        NOT NULL,
    produkt_id      NUMBER        NOT NULL,
    positionsnummer NUMBER(9)     NOT NULL,
    menge           NUMBER(12,3)  NOT NULL,
    einzelpreis     NUMBER(12,2)  NOT NULL,
    positionsbetrag NUMBER(14,2)  NOT NULL,
    CONSTRAINT fk_position_auftrag FOREIGN KEY (auftrag_id) REFERENCES nltsql_demo.auftrag (id),
    CONSTRAINT fk_position_produkt FOREIGN KEY (produkt_id) REFERENCES nltsql_demo.produkt (id),
    CONSTRAINT uq_position UNIQUE (auftrag_id, positionsnummer)
);

COMMENT ON TABLE  nltsql_demo.auftragsposition                 IS 'Einzelposition eines Auftrags.';
COMMENT ON COLUMN nltsql_demo.auftragsposition.menge           IS 'Bestellte Menge in der Basismengeneinheit des Artikels.';
COMMENT ON COLUMN nltsql_demo.auftragsposition.positionsbetrag IS 'Menge mal Einzelpreis in EUR, netto.';

CREATE INDEX nltsql_demo.ix_auftrag_kunde        ON nltsql_demo.auftrag (kunde_id);
CREATE INDEX nltsql_demo.ix_auftrag_bestelldatum ON nltsql_demo.auftrag (bestelldatum);
CREATE INDEX nltsql_demo.ix_position_auftrag     ON nltsql_demo.auftragsposition (auftrag_id);
CREATE INDEX nltsql_demo.ix_position_produkt     ON nltsql_demo.auftragsposition (produkt_id);
