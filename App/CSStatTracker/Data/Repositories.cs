using Microsoft.Data.SqlClient;

namespace CSStatTracker.Data;

// Each repository method calls exactly one stored procedure from
// Database/Procedures.sql and maps its columns by name.

public sealed class PlayerRepository
{
    private readonly Db _db;
    public PlayerRepository(Db db) => _db = db;

    public Task<List<PlayerListItem>> SearchAsync(string? search = null, string? rank = null) =>
        _db.QueryAsync("dbo.Players_Search",
            r => new PlayerListItem(r.Int("PlayerID"), r.Str("Username"), r.Str("Email"),
                                    r.DateOnly("JoinDate"), r.Str("Rank"), r.Int("GamesPlayed")),
            Param.VarChar("@Search", search, 255),
            Param.VarChar("@Rank", rank, 20));

    public Task<Player?> GetAsync(int playerId) =>
        _db.QuerySingleAsync("dbo.Players_Get",
            r => new Player(r.Int("PlayerID"), r.Str("Username"), r.Str("Email"),
                            r.DateOnly("JoinDate"), r.Str("Rank")),
            Param.Int("@PlayerID", playerId));

    public async Task<int> InsertAsync(string username, string email, string rank)
    {
        var id = Param.OutputInt("@PlayerID");
        await _db.ExecuteAsync("dbo.Players_Insert",
            Param.VarChar("@Username", username, 32),
            Param.VarChar("@Email", email, 255),
            Param.VarChar("@Rank", rank, 20),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateAsync(int playerId, string username, string email, string rank) =>
        _db.ExecuteAsync("dbo.Players_Update",
            Param.Int("@PlayerID", playerId),
            Param.VarChar("@Username", username, 32),
            Param.VarChar("@Email", email, 255),
            Param.VarChar("@Rank", rank, 20));
}

public sealed class LookupRepository
{
    private readonly Db _db;
    public LookupRepository(Db db) => _db = db;

    public Task<List<MapItem>> MapsAsync() =>
        _db.QueryAsync("dbo.Maps_List",
            r => new MapItem(r.Int("MapID"), r.Str("MapName"), r.Str("MapType"), r.Int("MatchCount")));

    public async Task<int> InsertMapAsync(string mapName, string mapType)
    {
        var id = Param.OutputInt("@MapID");
        await _db.ExecuteAsync("dbo.Maps_Insert",
            Param.VarChar("@MapName", mapName, 32),
            Param.VarChar("@MapType", mapType, 16),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateMapAsync(int mapId, string mapName, string mapType) =>
        _db.ExecuteAsync("dbo.Maps_Update",
            Param.Int("@MapID", mapId),
            Param.VarChar("@MapName", mapName, 32),
            Param.VarChar("@MapType", mapType, 16));

    public Task<List<GameModeItem>> ModesAsync() =>
        _db.QueryAsync("dbo.GameModes_List",
            r => new GameModeItem(r.Int("GameModeID"), r.Str("ModeName"), r.Int("PlayersPerTeam"),
                                  r.Int("MaxRounds"), r.Int("MatchCount")));

    public async Task<int> InsertModeAsync(string modeName, int playersPerTeam, int maxRounds)
    {
        var id = Param.OutputInt("@GameModeID");
        await _db.ExecuteAsync("dbo.GameModes_Insert",
            Param.VarChar("@ModeName", modeName, 20),
            Param.TinyInt("@PlayersPerTeam", playersPerTeam),
            Param.TinyInt("@MaxRounds", maxRounds),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateModeAsync(int gameModeId, string modeName, int playersPerTeam, int maxRounds) =>
        _db.ExecuteAsync("dbo.GameModes_Update",
            Param.Int("@GameModeID", gameModeId),
            Param.VarChar("@ModeName", modeName, 20),
            Param.TinyInt("@PlayersPerTeam", playersPerTeam),
            Param.TinyInt("@MaxRounds", maxRounds));
}

public sealed class MatchRepository
{
    private readonly Db _db;
    public MatchRepository(Db db) => _db = db;

    public Task<List<MatchListItem>> SearchAsync(MatchSearch s) =>
        _db.QueryAsync("dbo.Matches_Search",
            r => new MatchListItem(
                r.Int("MatchID"), r.DateTime("MatchDate"), r.Str("MapName"), r.Str("ModeName"),
                r.Int("Team1Score"), r.Int("Team2Score"), r.Bool("IsRemoved"), r.Int("PlayerCount"),
                r.StrOrNull("PlayerResult"), r.IntOrNull("PlayerKills"), r.IntOrNull("PlayerDeaths"),
                r.Int("TotalRows")),
            Param.Int("@PlayerID", s.PlayerId),
            Param.Int("@MapID", s.MapId),
            Param.Int("@GameModeID", s.GameModeId),
            Param.Date("@StartDate", s.From),
            Param.Date("@EndDate", s.To),
            Param.Bit("@IncludeRemoved", s.IncludeRemoved),
            Param.Int("@PageNumber", s.PageNumber),
            Param.Int("@PageSize", s.PageSize));

    public Task<MatchDetail?> GetAsync(int matchId) =>
        _db.QuerySingleAsync("dbo.Matches_Get",
            r => new MatchDetail(
                r.Int("MatchID"), r.DateTime("MatchDate"), r.Int("MapID"), r.Str("MapName"),
                r.Int("GameModeID"), r.Str("ModeName"), r.Int("PlayersPerTeam"), r.Int("MaxRounds"),
                r.Int("Team1Score"), r.Int("Team2Score"), r.Bool("IsRemoved")),
            Param.Int("@MatchID", matchId));

    public Task<List<ScoreboardRow>> ScoreboardAsync(int matchId) =>
        _db.QueryAsync("dbo.PlayerMatchStats_ListByMatch",
            r => new ScoreboardRow(
                r.Int("PlayerMatchStatID"), r.Int("PlayerID"), r.Str("Username"),
                r.Int("Kills"), r.Int("Deaths"), r.Int("Assists"), r.Int("HeadshotCount"),
                r.Int("Damage"), r.Str("Result"), r.DecimalOrNull("HeadshotPct"),
                r.DecimalOrNull("ADR"), r.IntOrNull("Team")),
            Param.Int("@MatchID", matchId));

    public async Task<int> ImportAsync(int mapId, int gameModeId, DateTime matchDate,
                                       int team1Score, int team2Score, IEnumerable<PlayerStatInput> stats)
    {
        var id = Param.OutputInt("@MatchID");
        await _db.ExecuteAsync("dbo.Matches_Import",
            Param.Int("@MapID", mapId),
            Param.Int("@GameModeID", gameModeId),
            Param.DateTime("@MatchDate", matchDate),
            Param.TinyInt("@Team1Score", team1Score),
            Param.TinyInt("@Team2Score", team2Score),
            Param.PlayerStats("@Stats", stats),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateAsync(int matchId, int mapId, DateTime matchDate, IEnumerable<PlayerStatInput> stats) =>
        _db.ExecuteAsync("dbo.Matches_Update",
            Param.Int("@MatchID", matchId),
            Param.Int("@MapID", mapId),
            Param.DateTime("@MatchDate", matchDate),
            Param.PlayerStats("@Stats", stats));

    public Task SetRemovedAsync(int matchId, bool isRemoved) =>
        _db.ExecuteAsync("dbo.Matches_SetRemoved",
            Param.Int("@MatchID", matchId),
            Param.Bit("@IsRemoved", isRemoved));
}

public sealed class FriendRepository
{
    private readonly Db _db;
    public FriendRepository(Db db) => _db = db;

    public Task<List<FriendItem>> ListAsync(int playerId) =>
        _db.QueryAsync("dbo.Friendships_ListForPlayer",
            r => new FriendItem(r.Int("FriendID"), r.Str("Username"), r.Str("Rank"), r.DateOnly("FriendsSince")),
            Param.Int("@PlayerID", playerId));

    public Task AddAsync(int playerId, int friendId) =>
        _db.ExecuteAsync("dbo.Friendships_Add",
            Param.Int("@PlayerID", playerId),
            Param.Int("@FriendID", friendId));

    public Task RemoveAsync(int playerId, int friendId) =>
        _db.ExecuteAsync("dbo.Friendships_Remove",
            Param.Int("@PlayerID", playerId),
            Param.Int("@FriendID", friendId));
}

/// <summary>The four aggregating queries.</summary>
public sealed class ReportRepository
{
    private readonly Db _db;
    public ReportRepository(Db db) => _db = db;

    public Task<PlayerTotals?> PlayerTotalsAsync(int playerId, DateOnly? startDate) =>
        _db.QuerySingleAsync("dbo.Report_PlayerTotals",
            r => new PlayerTotals(r.Int("PlayerID"), r.Str("DisplayName"), r.Int("GamesPlayed"),
                                  r.Int("TotalKills"), r.Int("TotalDeaths"), r.Int("TotalAssists"),
                                  r.Int("TotalDamage"), r.Int("TotalWins")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate));

    public Task<List<FriendComparisonRow>> FriendComparisonAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("dbo.Report_FriendComparison",
            r => new FriendComparisonRow(
                r.Int("FriendID"), r.Str("FriendName"), r.Int("FriendGamesPlayed"),
                r.DecimalOrNull("PlayerKD"), r.DecimalOrNull("FriendKD"), r.DecimalOrNull("KDDifference"),
                r.DecimalOrNull("PlayerADR"), r.DecimalOrNull("FriendADR"), r.DecimalOrNull("ADRDifference"),
                r.DecimalOrNull("PlayerWinRate"), r.DecimalOrNull("FriendWinRate"), r.DecimalOrNull("WinRateDifference")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));

    public Task<List<MapStatRow>> MapStatsAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("dbo.Report_PlayerMapStats",
            r => new MapStatRow(r.Str("MapName"), r.Int("GamesPlayed"), r.Int("Wins"),
                                r.DecimalOrNull("WinRate") ?? 0, r.DecimalOrNull("AvgKills") ?? 0,
                                r.DecimalOrNull("AvgDamage") ?? 0, r.Int("MapRank")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));

    public Task<List<MonthlyTrendRow>> MonthlyTrendAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("dbo.Report_PlayerMonthlyTrend",
            r => new MonthlyTrendRow(r.DateOnly("Month"), r.Int("NumGames"), r.DecimalOrNull("KillDeathRatio"),
                                     r.DecimalOrNull("AverageDamagePerRound"), r.DecimalOrNull("WinRate") ?? 0),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));
}

/// <summary>Helper for page models: run a database call and capture a friendly error.</summary>
public static class DbCall
{
    public static async Task<string?> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (SqlException ex)
        {
            return DbErrors.Friendly(ex);
        }
    }
}
