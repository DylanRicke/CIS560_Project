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

    public static SqlParameter TinyInt(string name, int value) =>
        new(name, SqlDbType.TinyInt) { Value = (byte)value };

    public static SqlParameter Bit(string name, bool value) =>
        new(name, SqlDbType.Bit) { Value = value };

    public static SqlParameter VarChar(string name, string? value, int size) =>
        new(name, SqlDbType.VarChar, size) { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim() };

    public static SqlParameter Date(string name, DateOnly? value) =>
        new(name, SqlDbType.Date) { Value = value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue) };

    public static SqlParameter DateTime(string name, DateTime value) =>
        new(name, SqlDbType.DateTime) { Value = value };

    public static SqlParameter OutputInt(string name) =>
        new(name, SqlDbType.Int) { Direction = ParameterDirection.Output };

    /// <summary>Builds the dbo.PlayerStatInput table-valued parameter.</summary>
    public static SqlParameter PlayerStats(string name, IEnumerable<PlayerStatInput> rows)
    {
        // Column order must match CREATE TYPE dbo.PlayerStatInput.
        var table = new DataTable();
        table.Columns.Add("PlayerID", typeof(int));
        table.Columns.Add("Team", typeof(byte)).AllowDBNull = true;
        table.Columns.Add("Kills", typeof(short));
        table.Columns.Add("Deaths", typeof(short));
        table.Columns.Add("Assists", typeof(short));
        table.Columns.Add("HeadshotCount", typeof(short));
        table.Columns.Add("Damage", typeof(int));

        foreach (var r in rows)
        {
            table.Rows.Add(r.PlayerId,
                           r.Team is null ? DBNull.Value : (byte)r.Team.Value,
                           (short)r.Kills, (short)r.Deaths, (short)r.Assists,
                           (short)r.HeadshotCount, r.Damage);
        }

        return new SqlParameter(name, SqlDbType.Structured)
        {
            TypeName = "dbo.PlayerStatInput",
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

    public static decimal? DecimalOrNull(this SqlDataReader r, string column) =>
        r[column] is DBNull ? null : Convert.ToDecimal(r[column]);

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
            2627 or 2601 when text.Contains("UQ_Players_Username") => "That username is already taken.",
            2627 or 2601 when text.Contains("UQ_Players_Email") => "That email address is already in use.",
            2627 or 2601 when text.Contains("UQ_Maps_MapName") => "A map with that name already exists.",
            2627 or 2601 when text.Contains("UQ_GameModes_ModeName") => "A game mode with that name already exists.",
            2627 or 2601 when text.Contains("UQ_PlayerMatchStats") => "A player can only be listed once per match.",
            2627 or 2601 when text.Contains("UQ_Friendships") => "These players are already friends.",
            2627 or 2601 => "That record already exists.",
            547 when text.Contains("CK_Players_Email") => "Enter a valid email address.",
            547 when text.Contains("CK_Players_Username") => "Usernames need at least 3 characters.",
            547 when text.Contains("CK_Players_Rank") => "Choose a rank from the list.",
            547 when text.Contains("CK_Maps_MapType") => "Map type must be Defusal or Hostage.",
            547 when text.Contains("CK_GameModes_PlayersPerTeam") => "Players per team must be between 1 and 10.",
            547 when text.Contains("CK_GameModes_MaxRounds") => "Max rounds must be between 1 and 60.",
            547 when text.Contains("CK_PlayerMatchStats_Headshots") => "A player can't have more headshots than kills.",
            547 when text.Contains("CK_") => "One of the values isn't allowed: " + text,
            547 => "That refers to a record that doesn't exist.",
            -2 => "The database took too long to answer. Try again.",
            _ => "The database returned an error: " + text
        };
    }
}
