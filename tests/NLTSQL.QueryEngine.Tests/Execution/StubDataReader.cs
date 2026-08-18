using System.Collections;
using System.Data.Common;
using NLTSQL.QueryEngine.Execution;

namespace NLTSQL.QueryEngine.Tests.Execution;

/// <summary>
/// A data reader over an in-memory table.
/// </summary>
/// <remarks>
/// Lets the materialisation logic be tested without a database. Only the members
/// <see cref="ResultSetReader"/> actually calls are implemented; the rest throw, so if the reader
/// grows a dependency on something else the test fails loudly rather than silently passing on a
/// stub that quietly returned a default.
/// </remarks>
internal sealed class StubDataReader(int fieldCount, IReadOnlyList<object?[]> rows) : DbDataReader
{
    private int index = -1;

    public override int FieldCount => fieldCount;

    public override bool HasRows => rows.Count > 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => 0;

    public override int Depth => 0;

    public override object this[int ordinal] => this.GetValue(ordinal);

    public override object this[string name] => throw new NotSupportedException();

    public override bool Read() => ++this.index < rows.Count;

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.Read());
    }

    public override bool IsDBNull(int ordinal) => rows[this.index][ordinal] is null;

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.IsDBNull(ordinal));
    }

    public override object GetValue(int ordinal) => rows[this.index][ordinal] ?? DBNull.Value;

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

    public override byte GetByte(int ordinal) => throw new NotSupportedException();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();

    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

    public override double GetDouble(int ordinal) => throw new NotSupportedException();

    public override Type GetFieldType(int ordinal) => throw new NotSupportedException();

    public override float GetFloat(int ordinal) => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => throw new NotSupportedException();

    public override int GetInt32(int ordinal) => throw new NotSupportedException();

    public override long GetInt64(int ordinal) => throw new NotSupportedException();

    public override string GetName(int ordinal) => throw new NotSupportedException();

    public override int GetOrdinal(string name) => throw new NotSupportedException();

    public override string GetString(int ordinal) => throw new NotSupportedException();

    public override int GetValues(object[] values) => throw new NotSupportedException();
}
