-- =====================================================================
-- Demo warehouse for the NLTSQL prototype.
--
-- Domain: discrete manufacturing / plant maintenance analytics.
-- The schema is a small star schema on purpose: the business semantics
-- (OEE, Ausschussquote, MTTR, ...) deliberately do NOT live here, they
-- live in the Cube semantic layer under infra/cube/model.
--
-- Replace this schema with the customer warehouse; nothing in the
-- application binds to these tables directly.
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS ops;

SET search_path TO ops, public;

-- --------------------------------------------------------------- dims

CREATE TABLE dim_site (
    site_id      integer PRIMARY KEY,
    site_code    text    NOT NULL UNIQUE,
    site_name    text    NOT NULL,
    country      text    NOT NULL
);

CREATE TABLE dim_machine (
    machine_id       integer PRIMARY KEY,
    machine_code     text    NOT NULL UNIQUE,
    machine_name     text    NOT NULL,
    site_id          integer NOT NULL REFERENCES dim_site (site_id),
    production_line  text    NOT NULL,
    manufacturer     text    NOT NULL,
    commissioned_on  date    NOT NULL
);

CREATE TABLE dim_product (
    product_id            integer PRIMARY KEY,
    sku                   text    NOT NULL UNIQUE,
    product_name          text    NOT NULL,
    product_family        text    NOT NULL,
    -- Ideal cycle time per unit; the basis of the OEE performance factor.
    target_cycle_seconds  numeric(10, 3) NOT NULL CHECK (target_cycle_seconds > 0)
);

CREATE TABLE dim_shift (
    shift_id    integer PRIMARY KEY,
    shift_code  text    NOT NULL UNIQUE,
    shift_name  text    NOT NULL,
    start_hour  smallint NOT NULL CHECK (start_hour BETWEEN 0 AND 23)
);

CREATE TABLE dim_downtime_reason (
    reason_id     integer PRIMARY KEY,
    reason_code   text    NOT NULL UNIQUE,
    reason_name   text    NOT NULL,
    reason_group  text    NOT NULL,
    is_planned    boolean NOT NULL
);

-- -------------------------------------------------------------- facts

CREATE TABLE fact_production_run (
    run_id                   bigint PRIMARY KEY,
    machine_id               integer   NOT NULL REFERENCES dim_machine (machine_id),
    product_id               integer   NOT NULL REFERENCES dim_product (product_id),
    shift_id                 integer   NOT NULL REFERENCES dim_shift (shift_id),
    started_at               timestamptz NOT NULL,
    ended_at                 timestamptz NOT NULL,
    -- Scheduled production time in minutes (OEE denominator for availability).
    planned_runtime_minutes  numeric(10, 2) NOT NULL CHECK (planned_runtime_minutes > 0),
    -- Time the machine actually produced, i.e. planned minus stoppages.
    actual_runtime_minutes   numeric(10, 2) NOT NULL CHECK (actual_runtime_minutes >= 0),
    planned_qty              integer NOT NULL CHECK (planned_qty >= 0),
    -- Units that passed inspection.
    good_qty                 integer NOT NULL CHECK (good_qty >= 0),
    -- Units rejected (Ausschuss).
    scrap_qty                integer NOT NULL CHECK (scrap_qty >= 0),
    CONSTRAINT run_period_valid CHECK (ended_at > started_at)
);

CREATE TABLE fact_downtime_event (
    event_id          bigint PRIMARY KEY,
    machine_id        integer NOT NULL REFERENCES dim_machine (machine_id),
    reason_id         integer NOT NULL REFERENCES dim_downtime_reason (reason_id),
    started_at        timestamptz NOT NULL,
    ended_at          timestamptz NOT NULL,
    duration_minutes  numeric(10, 2) NOT NULL CHECK (duration_minutes >= 0),
    CONSTRAINT downtime_period_valid CHECK (ended_at >= started_at)
);

-- Indexes matching the access paths the semantic layer generates:
-- time-sliced aggregation grouped by machine / product.
CREATE INDEX ix_run_started_at ON fact_production_run (started_at);
CREATE INDEX ix_run_machine    ON fact_production_run (machine_id, started_at);
CREATE INDEX ix_run_product    ON fact_production_run (product_id, started_at);
CREATE INDEX ix_downtime_started_at ON fact_downtime_event (started_at);
CREATE INDEX ix_downtime_machine    ON fact_downtime_event (machine_id, started_at);

-- Read-only role used by Cube. The semantic layer never needs DML.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cube_reader') THEN
        CREATE ROLE cube_reader LOGIN PASSWORD 'cube_reader';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA ops TO cube_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA ops TO cube_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA ops GRANT SELECT ON TABLES TO cube_reader;
