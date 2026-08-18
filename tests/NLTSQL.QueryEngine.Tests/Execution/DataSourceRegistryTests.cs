using Microsoft.Extensions.Options;
using NLTSQL.QueryEngine.Execution;

namespace NLTSQL.QueryEngine.Tests.Execution;

public sealed class DataSourceRegistryTests
{
    [Fact]
    public void PostgreSqlSource_GetsThePostgreSqlDialect()
    {
        // Pairing the dialect with the connection in one lookup is what makes it impossible to
        // compile Oracle SQL and run it somewhere that would accept it and mean something else.
        var resolved = Registry().Resolve("pg_main");

        resolved.Dialect.Name.ShouldBe("postgresql");
        resolved.Provider.ShouldBe(DataSourceProvider.PostgreSql);
    }

    [Fact]
    public void OracleSource_GetsTheOracleDialect()
    {
        var resolved = Registry().Resolve("erp");

        resolved.Dialect.Name.ShouldBe("oracle");
        resolved.Provider.ShouldBe(DataSourceProvider.Oracle);
    }

    [Fact]
    public void ConnectionFactoryProducesAClosedConnectionOfTheRightType()
    {
        var connection = Registry().Resolve("pg_main").CreateConnection();

        using (connection)
        {
            connection.GetType().Name.ShouldBe("NpgsqlConnection");
            connection.State.ShouldBe(System.Data.ConnectionState.Closed);
        }
    }

    [Fact]
    public void NamesAreMatchedCaseInsensitively()
    {
        Registry().Resolve("PG_MAIN").Name.ShouldBe("PG_MAIN");
    }

    [Fact]
    public void UnknownSource_NamesTheConfiguredOnes()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Registry().Resolve("nope"));

        exception.Message.ShouldContain("pg_main");
        exception.Message.ShouldContain("erp");
    }

    [Fact]
    public void NoSourcesConfigured_SaysSoRatherThanListingNothing()
    {
        var registry = new DataSourceRegistry(Options.Create(new DataSourceOptions()));

        var exception = Should.Throw<InvalidOperationException>(() => registry.Resolve("pg_main"));

        exception.Message.ShouldContain("(none)");
    }

    private static DataSourceRegistry Registry()
    {
        var options = new DataSourceOptions();
        options.Sources["pg_main"] = new DataSourceDefinition
        {
            Provider = DataSourceProvider.PostgreSql,
            ConnectionString = "Host=localhost;Database=demo;Username=reader;Password=devonly",
        };
        options.Sources["erp"] = new DataSourceDefinition
        {
            Provider = DataSourceProvider.Oracle,
            ConnectionString = "User Id=reader;Password=devonly;Data Source=localhost:1521/FREEPDB1",
        };

        return new DataSourceRegistry(Options.Create(options));
    }
}
