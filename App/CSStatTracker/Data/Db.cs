using System.Data;
using Microsoft.Data.SqlClient;

namespace CSStatTracker.Data;

/// <summary>
/// Thin ADO.NET helper. Every call runs a stored procedure with
/// SqlCommand and reads rows with SqlDataReader. Mapping from columns to
/// objects is written by hand in each repository, so there is no ORM.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string connectionString) => _connectionString = connectionString;

    /// <summary>Runs a procedure and maps every row it returns.</summary>
    public async Task<List<T>> QueryAsync<T>(string procedure, Func<SqlDataReader, T> map, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = CreateCommand(connection, procedure, parameters);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }
        return rows;
    }

    /// <summary>Runs a procedure and maps the first row, or returns null.</summary>
    public async Task<T?> QuerySingleAsync<T>(string procedure, Func<SqlDataReader, T> map, params SqlParameter[] parameters)
        where T : class
    {
        var rows = await QueryAsync(procedure, map, parameters);
        return rows.FirstOrDefault();
    }

    /// <summary>Runs a procedure that changes data. Output parameters are filled in afterwards.</summary>
    public async Task ExecuteAsync(string procedure, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = CreateCommand(connection, procedure, parameters);
        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string procedure, SqlParameter[] parameters)
    {
        var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 30
        };
        command.Parameters.AddRange(parameters);
        return command;
    }
}

/// <summary>Shortcuts for building SqlParameters.</summary>
public static class Param
{
    public static SqlParameter Int(string name, int? value) =>
        new(name, SqlDbType.Int) { Value = (object?)value ?? DBNull.Value };

    public static SqlParameter TinyInt(string name, int? value) =>
        new(name, SqlDbType.TinyInt) { Value = value is null ? DBNull.Value : (byte)value.Value };

    public static SqlParameter Bit(string name, bool value) =>
        new(name, SqlDbType.Bit) { Value = value };

    public static SqlParameter VarChar(string name, string? value, int size) =>
        new(name, SqlDbType.VarChar, size) { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim() };

    public static SqlParameter NVarChar(string name, string? value, int size) =>
        new(name, SqlDbType.NVarChar, size) { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim() };

    public static SqlParameter Date(string name, DateOnly? value) =>
        new(name, SqlDbType.Date) { Value = value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue) };

    public static SqlParameter DateTime2(string name, DateTime value) =>
        new(name, SqlDbType.DateTime2) { Value = value, Scale = 0 };

    public static SqlParameter OutputInt(string name) =>
        new(name, SqlDbType.Int) { Direction = ParameterDirection.Output };

    public static SqlParameter OutputVarChar(string name, int size) =>
        new(name, SqlDbType.VarChar, size) { Direction = ParameterDirection.Output };

    /// <summary>Builds the Tracker.PlayerStatInput table-valued parameter.</summary>
    public static SqlParameter PlayerStats(string name, IEnumerable<PlayerStatInput> rows)
    {
        // Column order must match CREATE TYPE Tracker.PlayerStatInput.
        var table = new DataTable();
        table.Columns.Add("PlayerID", typeof(int));
        table.Columns.Add("TeamNumber", typeof(byte));
        table.Columns.Add("Kills", typeof(short));
        table.Columns.Add("Deaths", typeof(short));
        table.Columns.Add("Assists", typeof(short));
        table.Columns.Add("HeadshotPercentage", typeof(decimal));
        table.Columns.Add("Damage", typeof(int));

        foreach (var r in rows)
        {
            table.Rows.Add(r.PlayerId, (byte)r.TeamNumber,
                           (short)r.Kills, (short)r.Deaths, (short)r.Assists,
                           Math.Round(r.HeadshotPercentage, 2), r.Damage);
        }

        return new SqlParameter(name, SqlDbType.Structured)
        {
            TypeName = "Tracker.PlayerStatInput",
            Value = table
        };
    }
}

/// <summary>Typed column readers for SqlDataReader.</summary>
public static class ReaderExtensions
{
    public static int Int(this SqlDataReader r, string column) => Convert.ToInt32(r[column]);

    public static int? IntOrNull(this SqlDataReader r, string column) =>
        r[column] is DBNull ? null : Convert.ToInt32(r[column]);

    public static decimal Decimal(this SqlDataReader r, string column) => Convert.ToDecimal(r[column]);

    public static decimal? DecimalOrNull(this SqlDataReader r, string column) =>
        r[column] is DBNull ? null : Convert.ToDecimal(r[column]);

    public static DateTime? DateTimeOrNull(this SqlDataReader r, string column) =>
        r[column] is DBNull ? null : (DateTime)r[column];

    public static string Str(this SqlDataReader r, string column) => (string)r[column];

    public static string? StrOrNull(this SqlDataReader r, string column) =>
        r[column] is DBNull ? null : (string)r[column];

    public static DateTime DateTime(this SqlDataReader r, string column) => (DateTime)r[column];

    public static DateOnly DateOnly(this SqlDataReader r, string column) =>
        System.DateOnly.FromDateTime((DateTime)r[column]);

    public static bool Bool(this SqlDataReader r, string column) => (bool)r[column];
}

/// <summary>Turns SQL Server errors into messages a user can act on.</summary>
public static class DbErrors
{
    public static string Friendly(SqlException ex)
    {
        // 50000-50999: raised on purpose with THROW in Procedures.sql.
        if (ex.Number is >= 50000 and < 51000)
        {
            return ex.Message;
        }

        var text = ex.Message;
        return ex.Number switch
        {
            2627 or 2601 when text.Contains("UQ_Players_SteamID") => "Another player already has that SteamID.",
            2627 or 2601 when text.Contains("UQ_Maps_MapName") => "A map with that name already exists.",
            2627 or 2601 when text.Contains("UQ_GameModes_ModeName") => "A game mode with that name already exists.",
            2627 or 2601 when text.Contains("UQ_PlayerMatchStats") => "A player can only be listed once per match.",
            2627 or 2601 when text.Contains("UQ_Friendships") => "These players already have a friendship or request.",
            2627 or 2601 => "That record already exists.",
            547 when text.Contains("CK_Players_SteamID") => "A SteamID is exactly 17 digits.",
            547 when text.Contains("CK_Players_DisplayName") => "Enter a display name.",
            547 when text.Contains("CK_Players_PremierRank") => "Premier rating must be between 0 and 40,000.",
            547 when text.Contains("CK_Players_CompetitiveRank") => "Choose a Competitive rank from the list.",
            547 when text.Contains("CK_Maps_MapType") => "Map type must be Defusal or Hostage.",
            547 when text.Contains("CK_Matches_Scores") => "A score can't be higher than 60.",
            547 when text.Contains("CK_PlayerMatchStats_HS") => "Headshot % must be between 0 and 100.",
            547 when text.Contains("CK_") => "One of the values isn't allowed: " + text,
            547 => "That refers to a record that doesn't exist, or other records still depend on it.",
            -2 => "The database took too long to answer. Try again.",
            _ => "The database returned an error: " + text
        };
    }
}
