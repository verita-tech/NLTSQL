using Microsoft.Data.Sqlite;

namespace NLTSQL.Data.Tests;

/// <summary>
/// Guards the SQLitePCLRaw version pin in Directory.Packages.props.
/// </summary>
public sealed class SqliteVersionTests
{
    /// <summary>
    /// CVE-2025-6965 (GHSA-2m69-gcr7-jv3q) is a memory-corruption defect in SQLite below
    /// 3.50.2, reachable when the number of aggregate terms exceeds the available columns.
    /// EF Core 10 resolves an affected SQLitePCLRaw by default, so we pin the graph forward.
    /// A pin is only worth as much as the check that it actually took effect: this asserts
    /// the version of the native library that is genuinely loaded at runtime, which would
    /// catch both an accidental un-pin and a package that ships an older engine than claimed.
    /// </summary>
    [Fact]
    public void NativeSqliteEngine_IsAtLeastThePatchedVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var reported = (string)command.ExecuteScalar()!;

        var actual = Version.Parse(reported);
        actual.ShouldBeGreaterThanOrEqualTo(
            new Version(3, 50, 2),
            $"SQLite {reported} is affected by CVE-2025-6965; check the SQLitePCLRaw pin.");
    }
}
