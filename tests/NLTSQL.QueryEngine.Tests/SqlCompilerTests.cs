using NLTSQL.Core.Query;
using NLTSQL.QueryEngine.Sql;

// Shouldly ships its own SortDirection; ours is the one this file means.
using SortDirection = NLTSQL.Core.Query.SortDirection;

namespace NLTSQL.QueryEngine.Tests;

/// <summary>
/// Asserts on the generated statement text for both engines.
/// </summary>
/// <remarks>
/// These run without a database on purpose: they pin down exactly what is emitted, so a change in
/// rendering shows up as a readable diff rather than as a subtly different number somewhere in an
/// integration test. Whether the statements are also <em>accepted and correct</em> is the job of
/// the integration tests that execute them against real engines.
/// </remarks>
public sealed class SqlCompilerTests
{
    public static TheoryData<ISqlDialect> Dialects =>
        [PostgreSqlDialect.Instance, OracleDialect.Instance];

    [Fact]
    public void SimpleAggregate_PostgreSql()
    {
        var sql = QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            PostgreSqlDialect.Instance).Sql;

        sql.ShouldBe(
            """
            SELECT
              SUM("t0"."nettobetrag") AS "umsatz"
            FROM "vertrieb"."auftrag" "t0"
            WHERE "t0"."mandant_id" = @p0
            LIMIT 1000
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SimpleAggregate_Oracle()
    {
        var sql = QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            OracleDialect.Instance).Sql;

        sql.ShouldBe(
            """
            SELECT
              SUM("t0"."nettobetrag") AS "umsatz"
            FROM "vertrieb"."auftrag" "t0"
            WHERE "t0"."mandant_id" = :p0
            FETCH FIRST 1000 ROWS ONLY
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void GroupedByMonth_PostgreSql()
    {
        var sql = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                GroupBy = [new GroupByItem("bestelldatum", TimeGrain.Month)],
                OrderBy = [new OrderByItem("bestelldatum", SortDirection.Ascending)],
            },
            PostgreSqlDialect.Instance).Sql;

        sql.ShouldBe(
            """
            SELECT
              date_trunc('month', "t0"."bestelldatum") AS "bestelldatum",
              SUM("t0"."nettobetrag") AS "umsatz"
            FROM "vertrieb"."auftrag" "t0"
            WHERE "t0"."mandant_id" = @p0
            GROUP BY date_trunc('month', "t0"."bestelldatum")
            ORDER BY "bestelldatum" ASC
            LIMIT 1000
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void GroupedByMonth_Oracle()
    {
        var sql = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                GroupBy = [new GroupByItem("bestelldatum", TimeGrain.Month)],
                OrderBy = [new OrderByItem("bestelldatum", SortDirection.Ascending)],
            },
            OracleDialect.Instance).Sql;

        sql.ShouldBe(
            """
            SELECT
              TRUNC("t0"."bestelldatum", 'MM') AS "bestelldatum",
              SUM("t0"."nettobetrag") AS "umsatz"
            FROM "vertrieb"."auftrag" "t0"
            WHERE "t0"."mandant_id" = :p0
            GROUP BY TRUNC("t0"."bestelldatum", 'MM')
            ORDER BY "bestelldatum" ASC
            FETCH FIRST 1000 ROWS ONLY
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WeekGrain_UsesIsoWeeksOnBothEngines()
    {
        // PostgreSQL's date_trunc('week', …) is ISO — Monday-based. Oracle's 'W' is not; only 'IW'
        // is. Using 'W' would shift every weekly figure on Oracle by up to six days relative to
        // PostgreSQL, and nothing else in the system would notice.
        QueryEngineFixture.Compile(WeeklySpec(), OracleDialect.Instance).Sql
            .ShouldContain("TRUNC(\"t0\".\"bestelldatum\", 'IW')");

        QueryEngineFixture.Compile(WeeklySpec(), PostgreSqlDialect.Instance).Sql
            .ShouldContain("date_trunc('week', \"t0\".\"bestelldatum\")");

        static QuerySpec WeeklySpec() => new()
        {
            Entity = "auftrag",
            Measures = ["anzahl"],
            GroupBy = [new GroupByItem("bestelldatum", TimeGrain.Week)],
        };
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void CountWithoutAnExpression_CountsRows(ISqlDialect dialect)
    {
        QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["anzahl"] },
            dialect).Sql.ShouldContain("COUNT(*) AS \"anzahl\"");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void CountDistinct_IsRendered(ISqlDialect dialect)
    {
        QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["kunden"] },
            dialect).Sql.ShouldContain("COUNT(DISTINCT \"t0\".\"kunde_id\") AS \"kunden\"");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void MeasureExpression_IsAggregatedAsAWhole(ISqlDialect dialect)
    {
        // SUM(a + b), not SUM(a) + SUM(b) — equal here, but not for AVG or COUNT, so the shape
        // has to follow the model rather than happen to be right for addition.
        QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["bruttoumsatz"] },
            dialect).Sql.ShouldContain("SUM((\"t0\".\"nettobetrag\" + \"t0\".\"rabattbetrag\"))");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Metric_ComputesArithmeticOverAggregates(ISqlDialect dialect)
    {
        // The division must wrap the aggregates. Dividing per row and then summing would produce
        // a plausible-looking, wrong number — the exact failure a semantic layer exists to prevent.
        QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["durchschnittswert"] },
            dialect).Sql.ShouldContain("(SUM(\"t0\".\"nettobetrag\") / NULLIF(COUNT(*), 0)) AS \"durchschnittswert\"");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void FilterValues_AreAlwaysBound_NeverInlined(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                Filters = [new FilterItem("status", FilterOperator.Equals, ["storniert'; DROP TABLE auftrag--"])],
            },
            dialect);

        compiled.Sql.ShouldNotContain("DROP TABLE");
        compiled.Parameters.ShouldContain(p => Equals(p.Value, "storniert'; DROP TABLE auftrag--"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void InFilter_BindsEveryValueSeparately(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                Filters = [new FilterItem("status", FilterOperator.In, ["offen", "versendet"])],
            },
            dialect);

        // p0 is the tenant filter, so the two values are p1 and p2.
        compiled.Parameters.Count.ShouldBe(3);
        compiled.Sql.ShouldContain(dialect.Name == "oracle"
            ? "\"t0\".\"status\" IN (:p1, :p2)"
            : "\"t0\".\"status\" IN (@p1, @p2)");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void NotEquals_IncludesRowsWithNoValue(ISqlDialect dialect)
    {
        // Asked for "status is not cancelled", a business user means every other row, including
        // those with no status. Plain <> would silently drop them and read as missing data.
        QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                Filters = [new FilterItem("status", FilterOperator.NotEquals, ["storniert"])],
            },
            dialect).Sql.ShouldContain("(\"t0\".\"status\" IS NULL OR \"t0\".\"status\" <>");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Contains_PutsWildcardsInTheParameter_AndEscapesTheUsersOwn(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["anzahl"],
                Filters = [new FilterItem("status", FilterOperator.Contains, ["50%_rabatt"])],
            },
            dialect);

        compiled.Sql.ShouldContain("LIKE");
        compiled.Sql.ShouldContain("ESCAPE '\\'");

        // The user's own % and _ are escaped so they match literally rather than as wildcards.
        compiled.Parameters[^1].Value.ShouldBe("%50\\%\\_rabatt%");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void RelativeTimeFilter_IsResolvedToBoundDates(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                Filters = [new FilterItem("bestelldatum", FilterOperator.InLastMonths, ["12"])],
            },
            dialect);

        // The boundary is computed in C# rather than with engine-specific interval arithmetic, so
        // both dialects get the identical instant and the audit record can state it exactly.
        compiled.Sql.ShouldNotContain("INTERVAL");
        compiled.Parameters[1].Value.ShouldBe(new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        compiled.Parameters[2].Value.ShouldBe(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void RowPolicy_IsAlwaysPresent_AndComesFirst(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz"],
                Filters = [new FilterItem("status", FilterOperator.Equals, ["offen"])],
            },
            dialect);

        compiled.Sql.ShouldContain("WHERE \"t0\".\"mandant_id\" =");
        compiled.Parameters[0].Value.ShouldBe(42L);
    }

    [Fact]
    public void MissingRowPolicyValue_RefusesToCompile()
    {
        // Failing closed is the whole point. A query that quietly loses its tenant filter would
        // return another customer's rows and look entirely normal doing it.
        var resolution = QueryEngineFixture.Resolver().Resolve(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            QueryEngineFixture.Model);

        var compiler = new SqlCompiler(PostgreSqlDialect.Instance);

        var exception = Should.Throw<InvalidOperationException>(
            () => compiler.Compile(resolution.Query!, QueryExecutionContext.Empty));

        exception.Message.ShouldContain("tenant_id");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ResultColumns_DescribeTheShapeOfTheAnswer(ISqlDialect dialect)
    {
        var compiled = QueryEngineFixture.Compile(
            new QuerySpec
            {
                Entity = "auftrag",
                Measures = ["umsatz", "durchschnittswert"],
                GroupBy = [new GroupByItem("bestelldatum", TimeGrain.Quarter)],
            },
            dialect);

        compiled.Columns.Select(c => c.Alias).ShouldBe(["bestelldatum", "umsatz", "durchschnittswert"]);
        compiled.Columns[0].Kind.ShouldBe(ResultColumnKind.Grouping);
        compiled.Columns[0].Grain.ShouldBe(TimeGrain.Quarter);
        compiled.Columns[0].Label.ShouldBe("Bestelldatum");
        compiled.Columns[1].Kind.ShouldBe(ResultColumnKind.Measure);
        compiled.Columns[1].Format!.Currency.ShouldBe("EUR");
        compiled.Columns[2].Kind.ShouldBe(ResultColumnKind.Metric);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void IdentifiersAreQuoted_SoCaseFoldingAndReservedWordsCannotBite(ISqlDialect dialect)
    {
        // Unquoted, Oracle folds to upper case and PostgreSQL to lower, so the same model would
        // resolve to different columns on the two engines.
        QueryEngineFixture.Compile(
            new QuerySpec { Entity = "auftrag", Measures = ["umsatz"] },
            dialect).Sql.ShouldContain("FROM \"vertrieb\".\"auftrag\"");
    }
}
