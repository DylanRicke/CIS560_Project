using Microsoft.Data.SqlClient;

namespace CSStatTracker.Data;

// Each repository method calls exactly one stored procedure from
// Database/Procedures.sql and maps its columns by name.

public sealed class PlayerRepository
{
    private readonly Db _db;
    public PlayerRepository(Db db) => _db = db;

    public Task<List<PlayerListItem>> SearchAsync(string? search = null, bool includeRemoved = false) =>
        _db.QueryAsync("Tracker.Players_Search",
            r => new PlayerListItem(r.Int("PlayerID"), r.Str("SteamID"), r.Str("DisplayName"),
                                    r.IntOrNull("PremierRank"), r.IntOrNull("CompetitiveRank"),
                                    r.DateTime("CreatedOn"), r.Bool("IsRemoved"), r.Int("GamesPlayed")),
            Param.NVarChar("@Search", search, 32),
            Param.Bit("@IncludeRemoved", includeRemoved));

    public Task<Player?> GetAsync(int playerId) =>
        _db.QuerySingleAsync("Tracker.Players_Get",
            r => new Player(r.Int("PlayerID"), r.Str("SteamID"), r.Str("DisplayName"),
                            r.IntOrNull("PremierRank"), r.IntOrNull("CompetitiveRank"),
                            r.DateTime("CreatedOn"), r.Bool("IsRemoved")),
            Param.Int("@PlayerID", playerId));

    public async Task<int> InsertAsync(string steamId, string displayName, int? premierRank, int? competitiveRank)
    {
        var id = Param.OutputInt("@PlayerID");
        await _db.ExecuteAsync("Tracker.Players_Insert",
            Param.VarChar("@SteamID", steamId, 17),
            Param.NVarChar("@DisplayName", displayName, 32),
            Param.Int("@PremierRank", premierRank),
            Param.TinyInt("@CompetitiveRank", competitiveRank),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateAsync(int playerId, string steamId, string displayName, int? premierRank, int? competitiveRank) =>
        _db.ExecuteAsync("Tracker.Players_Update",
            Param.Int("@PlayerID", playerId),
            Param.VarChar("@SteamID", steamId, 17),
            Param.NVarChar("@DisplayName", displayName, 32),
            Param.Int("@PremierRank", premierRank),
            Param.TinyInt("@CompetitiveRank", competitiveRank));

    public Task SetRemovedAsync(int playerId, bool isRemoved) =>
        _db.ExecuteAsync("Tracker.Players_SetRemoved",
            Param.Int("@PlayerID", playerId),
            Param.Bit("@IsRemoved", isRemoved));

    public Task<List<PremierPoint>> PremierHistoryAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("Tracker.RankHistory_PremierForPlayer",
            r => new PremierPoint(r.DateTime("RecordedOn"), r.Int("RankValue")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));

    public Task<List<CompetitiveMapRank>> CompetitiveByMapAsync(int playerId) =>
        _db.QueryAsync("Tracker.RankHistory_CompetitiveByMap",
            r => new CompetitiveMapRank(r.Str("MapName"), r.Int("RankValue"), r.DateTime("RecordedOn")),
            Param.Int("@PlayerID", playerId));
}

public sealed class LookupRepository
{
    private readonly Db _db;
    public LookupRepository(Db db) => _db = db;

    public Task<List<MapItem>> MapsAsync() =>
        _db.QueryAsync("Tracker.Maps_List",
            r => new MapItem(r.Int("MapID"), r.Str("MapName"), r.Str("MapType"), r.Int("MatchCount")));

    public async Task<int> InsertMapAsync(string mapName, string mapType)
    {
        var id = Param.OutputInt("@MapID");
        await _db.ExecuteAsync("Tracker.Maps_Insert",
            Param.NVarChar("@MapName", mapName, 32),
            Param.VarChar("@MapType", mapType, 16),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateMapAsync(int mapId, string mapName, string mapType) =>
        _db.ExecuteAsync("Tracker.Maps_Update",
            Param.Int("@MapID", mapId),
            Param.NVarChar("@MapName", mapName, 32),
            Param.VarChar("@MapType", mapType, 16));

    public Task<List<GameModeItem>> ModesAsync() =>
        _db.QueryAsync("Tracker.GameModes_List",
            r => new GameModeItem(r.Int("GameModeID"), r.Str("ModeName"), r.Int("MatchCount")));

    public async Task<int> InsertModeAsync(string modeName)
    {
        var id = Param.OutputInt("@GameModeID");
        await _db.ExecuteAsync("Tracker.GameModes_Insert",
            Param.NVarChar("@ModeName", modeName, 32),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateModeAsync(int gameModeId, string modeName) =>
        _db.ExecuteAsync("Tracker.GameModes_Update",
            Param.Int("@GameModeID", gameModeId),
            Param.NVarChar("@ModeName", modeName, 32));
}

public sealed class MatchRepository
{
    private readonly Db _db;
    public MatchRepository(Db db) => _db = db;

    public Task<List<MatchListItem>> SearchAsync(MatchSearch s) =>
        _db.QueryAsync("Tracker.Matches_Search",
            r => new MatchListItem(
                r.Int("MatchID"), r.DateTime("MatchDate"), r.Str("MapName"), r.Str("ModeName"),
                r.Int("Team1Score"), r.Int("Team2Score"), r.Int("PlayerCount"),
                r.StrOrNull("PlayerResult"), r.IntOrNull("PlayerKills"), r.IntOrNull("PlayerDeaths"),
                r.Int("TotalRows")),
            Param.Int("@PlayerID", s.PlayerId),
            Param.Int("@MapID", s.MapId),
            Param.Int("@GameModeID", s.GameModeId),
            Param.Date("@StartDate", s.From),
            Param.Date("@EndDate", s.To),
            Param.Int("@PageNumber", s.PageNumber),
            Param.Int("@PageSize", s.PageSize));

    public Task<MatchDetail?> GetAsync(int matchId) =>
        _db.QuerySingleAsync("Tracker.Matches_Get",
            r => new MatchDetail(
                r.Int("MatchID"), r.DateTime("MatchDate"), r.Int("MapID"), r.Str("MapName"), r.Str("MapType"),
                r.Int("GameModeID"), r.Str("ModeName"), r.Int("Team1Score"), r.Int("Team2Score"),
                r.Int("RoundsRecorded")),
            Param.Int("@MatchID", matchId));

    public Task<List<ScoreboardRow>> ScoreboardAsync(int matchId) =>
        _db.QueryAsync("Tracker.PlayerMatchStats_ListByMatch",
            r => new ScoreboardRow(
                r.Int("PlayerMatchStatID"), r.Int("PlayerID"), r.Str("DisplayName"), r.Int("TeamNumber"),
                r.Int("Kills"), r.Int("Deaths"), r.Int("Assists"), r.Decimal("HeadshotPercentage"),
                r.Int("Damage"), r.Str("Result"), r.DecimalOrNull("ADR")),
            Param.Int("@MatchID", matchId));

    public Task<List<RoundRow>> RoundsAsync(int matchId) =>
        _db.QueryAsync("Tracker.Rounds_ListByMatch",
            r => new RoundRow(r.Int("RoundNumber"), r.Int("WinningTeam"), r.Str("WinReason"), r.Int("DurationSeconds")),
            Param.Int("@MatchID", matchId));

    public async Task<int> ImportAsync(int mapId, int gameModeId, DateTime matchDate,
                                       int team1Score, int team2Score, IEnumerable<PlayerStatInput> stats)
    {
        var id = Param.OutputInt("@MatchID");
        await _db.ExecuteAsync("Tracker.Matches_Import",
            Param.Int("@MapID", mapId),
            Param.Int("@GameModeID", gameModeId),
            Param.DateTime2("@MatchDate", matchDate),
            Param.TinyInt("@Team1Score", team1Score),
            Param.TinyInt("@Team2Score", team2Score),
            Param.PlayerStats("@Stats", stats),
            id);
        return Convert.ToInt32(id.Value);
    }

    public Task UpdateAsync(int matchId, int mapId, DateTime matchDate, IEnumerable<PlayerStatInput> stats) =>
        _db.ExecuteAsync("Tracker.Matches_Update",
            Param.Int("@MatchID", matchId),
            Param.Int("@MapID", mapId),
            Param.DateTime2("@MatchDate", matchDate),
            Param.PlayerStats("@Stats", stats));

    public Task DeleteAsync(int matchId) =>
        _db.ExecuteAsync("Tracker.Matches_Delete",
            Param.Int("@MatchID", matchId));
}

public sealed class FriendRepository
{
    private readonly Db _db;
    public FriendRepository(Db db) => _db = db;

    public Task<List<FriendItem>> ListAsync(int playerId) =>
        _db.QueryAsync("Tracker.Friendships_ListForPlayer",
            r => new FriendItem(r.Int("OtherPlayerID"), r.Str("DisplayName"), r.IntOrNull("PremierRank"),
                                r.Str("Status"), r.Str("Direction"), r.DateTime("RequestedOn"),
                                r.DateTimeOrNull("FriendsSince")),
            Param.Int("@PlayerID", playerId));

    /// <summary>Sends a request (or accepts theirs). Returns the resulting Status.</summary>
    public async Task<string> AddAsync(int playerId, int friendId)
    {
        var status = Param.OutputVarChar("@Status", 10);
        await _db.ExecuteAsync("Tracker.Friendships_Add",
            Param.Int("@PlayerID", playerId),
            Param.Int("@FriendID", friendId),
            status);
        return Convert.ToString(status.Value) ?? "";
    }

    public Task RespondAsync(int playerId, int requesterId, bool accept) =>
        _db.ExecuteAsync("Tracker.Friendships_Respond",
            Param.Int("@PlayerID", playerId),
            Param.Int("@RequesterID", requesterId),
            Param.Bit("@Accept", accept));

    public Task RemoveAsync(int playerId, int friendId) =>
        _db.ExecuteAsync("Tracker.Friendships_Remove",
            Param.Int("@PlayerID", playerId),
            Param.Int("@FriendID", friendId));
}

/// <summary>The four aggregating queries.</summary>
public sealed class ReportRepository
{
    private readonly Db _db;
    public ReportRepository(Db db) => _db = db;

    public Task<PlayerTotals?> PlayerTotalsAsync(int playerId, DateOnly? startDate) =>
        _db.QuerySingleAsync("Tracker.Report_PlayerTotals",
            r => new PlayerTotals(r.Int("PlayerID"), r.Str("DisplayName"), r.Int("GamesPlayed"),
                                  r.Int("TotalKills"), r.Int("TotalDeaths"), r.Int("TotalAssists"),
                                  r.Int("TotalDamage"), r.Int("TotalWins"), r.DecimalOrNull("AvgHeadshotPct")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate));

    public Task<List<FriendComparisonRow>> FriendComparisonAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("Tracker.Report_FriendComparison",
            r => new FriendComparisonRow(
                r.Int("FriendID"), r.Str("FriendName"), r.Int("FriendGamesPlayed"),
                r.DecimalOrNull("PlayerKD"), r.DecimalOrNull("FriendKD"), r.DecimalOrNull("KDDifference"),
                r.DecimalOrNull("PlayerADR"), r.DecimalOrNull("FriendADR"), r.DecimalOrNull("ADRDifference"),
                r.DecimalOrNull("PlayerWinRate"), r.DecimalOrNull("FriendWinRate"), r.DecimalOrNull("WinRateDifference")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));

    public Task<List<MapStatRow>> MapStatsAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("Tracker.Report_PlayerMapStats",
            r => new MapStatRow(r.Str("MapName"), r.Int("GamesPlayed"), r.Int("Wins"),
                                r.DecimalOrNull("WinRate") ?? 0, r.DecimalOrNull("AvgKills") ?? 0,
                                r.DecimalOrNull("AvgDamage") ?? 0, r.Int("MapRank")),
            Param.Int("@PlayerID", playerId),
            Param.Date("@StartDate", startDate),
            Param.Date("@EndDate", endDate));

    public Task<List<MonthlyTrendRow>> MonthlyTrendAsync(int playerId, DateOnly startDate, DateOnly endDate) =>
        _db.QueryAsync("Tracker.Report_PlayerMonthlyTrend",
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
