-- =====================================================================
-- Deterministic demo data.
--
-- Randomness is derived from md5(seed_text) instead of random() so that
-- every `docker compose up` produces byte-identical facts. That keeps
-- screenshots, tests and Metabase snapshots reproducible.
--
-- The calendar is anchored to CURRENT_DATE so relative questions
-- ("letzte 30 Tage") always return data.
-- =====================================================================

SET search_path TO ops, public;

-- Deterministic uniform value in [0,1). 7 hex chars = 28 bits, which
-- always fits a non-negative int4.
CREATE OR REPLACE FUNCTION det_rand(seed text)
RETURNS double precision
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT (('x' || substr(md5(seed), 1, 7))::bit(28)::int)::double precision / 268435456.0;
$$;

INSERT INTO dim_site (site_id, site_code, site_name, country) VALUES
    (1, 'DE-AUG', 'Werk Augsburg',  'DE'),
    (2, 'CZ-BRN', 'Werk Brno',      'CZ');

INSERT INTO dim_machine (machine_id, machine_code, machine_name, site_id, production_line, manufacturer, commissioned_on) VALUES
    (1, 'PRS-01', 'Presse 1',        1, 'Linie A', 'Schuler',   '2016-04-11'),
    (2, 'PRS-02', 'Presse 2',        1, 'Linie A', 'Schuler',   '2019-09-02'),
    (3, 'CNC-01', 'CNC-Zentrum 1',   1, 'Linie B', 'DMG Mori',  '2018-02-19'),
    (4, 'CNC-02', 'CNC-Zentrum 2',   1, 'Linie B', 'DMG Mori',  '2014-07-28'),
    (5, 'MNT-01', 'Montagezelle 1',  2, 'Linie C', 'Kuka',      '2021-01-15'),
    (6, 'MNT-02', 'Montagezelle 2',  2, 'Linie C', 'Kuka',      '2022-06-07');

INSERT INTO dim_product (product_id, sku, product_name, product_family, target_cycle_seconds) VALUES
    (1, 'GH-1001', 'Gehäuse Typ A',      'Gehäuse',    42.0),
    (2, 'GH-1002', 'Gehäuse Typ B',      'Gehäuse',    55.5),
    (3, 'WL-2001', 'Welle 20mm',         'Wellen',     18.0),
    (4, 'WL-2002', 'Welle 32mm',         'Wellen',     24.5),
    (5, 'FL-3001', 'Flansch DN50',       'Flansche',   12.5),
    (6, 'FL-3002', 'Flansch DN80',       'Flansche',   15.0),
    (7, 'BG-4001', 'Baugruppe Pumpe S',  'Baugruppen', 96.0),
    (8, 'BG-4002', 'Baugruppe Pumpe L',  'Baugruppen', 130.0);

INSERT INTO dim_shift (shift_id, shift_code, shift_name, start_hour) VALUES
    (1, 'F', 'Frühschicht', 6),
    (2, 'S', 'Spätschicht', 14),
    (3, 'N', 'Nachtschicht', 22);

INSERT INTO dim_downtime_reason (reason_id, reason_code, reason_name, reason_group, is_planned) VALUES
    (1,  'PM-100', 'Geplante Wartung',        'Instandhaltung', true),
    (2,  'PM-110', 'Werkzeugwechsel geplant', 'Rüsten',         true),
    (3,  'UM-200', 'Werkzeugbruch',           'Instandhaltung', false),
    (4,  'UM-210', 'Störung Steuerung',       'Instandhaltung', false),
    (5,  'UM-220', 'Hydraulikleckage',        'Instandhaltung', false),
    (6,  'OR-300', 'Materialmangel',          'Organisation',   false),
    (7,  'OR-310', 'Personalmangel',          'Organisation',   false),
    (8,  'OR-320', 'Qualitätsprüfung',        'Qualität',       false),
    (9,  'RU-400', 'Rüsten ungeplant',        'Rüsten',         false),
    (10, 'RU-410', 'Anfahrverluste',          'Rüsten',         false);

-- ------------------------------------------------------- production runs
--
-- 6 machines x 3 shifts x ~18 months, Sundays excluded.
INSERT INTO fact_production_run (
    run_id, machine_id, product_id, shift_id,
    started_at, ended_at,
    planned_runtime_minutes, actual_runtime_minutes,
    planned_qty, good_qty, scrap_qty
)
WITH calendar AS (
    SELECT
        day_offset,
        (CURRENT_DATE - day_offset)::date AS run_date
    FROM generate_series(0, 546) AS day_offset
),
slot AS (
    SELECT
        c.day_offset,
        c.run_date,
        m.machine_id,
        s.shift_id,
        s.start_hour,
        -- Stable seed per production slot.
        m.machine_id || ':' || c.day_offset || ':' || s.shift_id AS seed
    FROM calendar c
    CROSS JOIN dim_machine m
    CROSS JOIN dim_shift s
    WHERE EXTRACT(DOW FROM c.run_date) <> 0                       -- no Sunday shifts
      AND NOT (EXTRACT(DOW FROM c.run_date) = 6 AND s.shift_id = 3) -- no Saturday night shift
),
assigned AS (
    SELECT
        sl.*,
        -- Each machine is qualified for a fixed pair of products.
        CASE sl.machine_id
            WHEN 1 THEN 1 WHEN 2 THEN 2 WHEN 3 THEN 3
            WHEN 4 THEN 4 WHEN 5 THEN 7 ELSE 8
        END + (CASE WHEN det_rand(sl.seed || ':prod') < 0.35 THEN
                    CASE WHEN sl.machine_id IN (1, 2) THEN 3 ELSE -2 END
               ELSE 0 END) AS product_id
    FROM slot sl
),
shaped AS (
    SELECT
        a.*,
        480.0 AS planned_minutes,
        -- Stoppage minutes per shift: baseline plus machine-specific load.
        round((12 + det_rand(a.seed || ':stop') * 70
                 + CASE WHEN a.machine_id = 4 THEN 25 ELSE 0 END
                 + CASE WHEN a.shift_id = 3 THEN 10 ELSE 0 END)::numeric, 2) AS stop_minutes,
        -- Performance factor (speed losses).
        0.74 + det_rand(a.seed || ':perf') * 0.24 AS perf_factor,
        -- Scrap rate; machine 4 degrades noticeably in the last 120 days,
        -- which gives the demo a finding worth drilling into.
        0.005 + det_rand(a.seed || ':scrap') * 0.045
              + CASE WHEN a.machine_id = 4 AND a.day_offset < 120 THEN 0.055 ELSE 0 END AS scrap_rate
    FROM assigned a
),
computed AS (
    SELECT
        s.*,
        p.target_cycle_seconds,
        (s.planned_minutes - s.stop_minutes) AS actual_minutes,
        -- Theoretical output at ideal cycle time over the running minutes.
        floor((s.planned_minutes - s.stop_minutes) * 60.0 / p.target_cycle_seconds) AS theoretical_qty
    FROM shaped s
    JOIN dim_product p ON p.product_id = s.product_id
),
final AS (
    SELECT
        c.*,
        floor(c.theoretical_qty * c.perf_factor) AS total_qty,
        floor(c.planned_minutes * 60.0 / c.target_cycle_seconds * 0.85) AS planned_qty
    FROM computed c
)
SELECT
    row_number() OVER (ORDER BY f.run_date, f.machine_id, f.shift_id)      AS run_id,
    f.machine_id,
    f.product_id,
    f.shift_id,
    (f.run_date + make_interval(hours => f.start_hour::int))               AS started_at,
    (f.run_date + make_interval(hours => f.start_hour::int) + interval '8 hours') AS ended_at,
    f.planned_minutes,
    f.actual_minutes,
    f.planned_qty::int,
    (f.total_qty - floor(f.total_qty * f.scrap_rate))::int                 AS good_qty,
    floor(f.total_qty * f.scrap_rate)::int                                 AS scrap_qty
FROM final f;

-- ------------------------------------------------------ downtime events
--
-- Events are generated per run so that the sum of event durations lines
-- up with the stoppage minutes implied by the run facts.
INSERT INTO fact_downtime_event (event_id, machine_id, reason_id, started_at, ended_at, duration_minutes)
WITH stoppage AS (
    SELECT
        r.run_id,
        r.machine_id,
        r.started_at,
        (r.planned_runtime_minutes - r.actual_runtime_minutes) AS stop_minutes,
        -- 1 to 3 events per shift.
        1 + floor(det_rand(r.run_id || ':n') * 3)::int         AS event_count
    FROM fact_production_run r
),
expanded AS (
    SELECT
        s.run_id,
        s.machine_id,
        s.started_at,
        s.stop_minutes,
        s.event_count,
        g.idx,
        det_rand(s.run_id || ':w' || g.idx) + 0.2 AS weight
    FROM stoppage s
    CROSS JOIN LATERAL generate_series(1, s.event_count) AS g(idx)
),
weighted AS (
    SELECT
        e.*,
        sum(e.weight) OVER (PARTITION BY e.run_id) AS weight_total,
        sum(e.weight) OVER (PARTITION BY e.run_id ORDER BY e.idx
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS weight_before
    FROM expanded e
)
SELECT
    row_number() OVER (ORDER BY w.run_id, w.idx)                       AS event_id,
    w.machine_id,
    1 + floor(det_rand(w.run_id || ':r' || w.idx) * 10)::int           AS reason_id,
    w.started_at + make_interval(mins =>
        round(coalesce(w.weight_before, 0) / w.weight_total * w.stop_minutes)::int) AS started_at,
    w.started_at + make_interval(mins =>
        round((coalesce(w.weight_before, 0) + w.weight) / w.weight_total * w.stop_minutes)::int) AS ended_at,
    round((w.weight / w.weight_total * w.stop_minutes)::numeric, 2)    AS duration_minutes
FROM weighted w;

ANALYZE;
