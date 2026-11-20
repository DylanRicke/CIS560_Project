namespace CSStatTracker.Data;

// Plain classes that hold rows returned by the stored procedures.

/// <summary>CS2 Competitive (per-map) ranks. CompetitiveRank 1 = Silver I ... 18 = Global Elite.</summary>
public static class CompetitiveRanks
{
    public static readonly string[] Names =
    {
        "Silver I", "Silver II", "Silver III", "Silver IV", "Silver Elite", "Silver Elite Master",
        "Gold Nova I", "Gold Nova II", "Gold Nova III", "Gold Nova Master",
        "Master Guardian I", "Master Guardian II", "Master Guardian Elite",
        "Distinguished Master Guardian", "Legendary Eagle", "Legendary Eagle Master",
        "Supreme Master First Class", "Global Elite"
    };

    public static string Name(int? rank) =>
        rank is >= 1 and <= 18 ? Names[rank.Value - 1] : "Unranked";
}

public static class MapTypes
{
    public static readonly string[] All = { "Defusal", "Hostage" };
}

/// <summary>
/// The schema's GameModes table only stores a name, so the team size the
/// import form shows comes from here. Unknown modes default to 5v5.
/// </summary>
public static class ModeRules
{
    private static readonly Dictionary<string, int> TeamSize = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Premier"] = 5,
        ["Competitive"] = 5,
        ["Casual"] = 10,
        ["Wingman"] = 2,
    };

    public static int PlayersPerTeam(string modeName) =>
        TeamSize.TryGetValue(modeName.Trim(), out var size) ? size : 5;
}

public record PlayerListItem(
    int PlayerId, string SteamId, string DisplayName, int? PremierRank, int? CompetitiveRank,
    DateTime CreatedOn, bool IsRemoved, int GamesPlayed);

public record Player(
    int PlayerId, string SteamId, string DisplayName, int? PremierRank, int? CompetitiveRank,
    DateTime CreatedOn, bool IsRemoved);

public record PremierPoint(DateTime RecordedOn, int RankValue);

public record CompetitiveMapRank(string MapName, int RankValue, DateTime RecordedOn);

public record MapItem(int MapId, string MapName, string MapType, int MatchCount);

public record GameModeItem(int GameModeId, string ModeName, int MatchCount)
{
    public int PlayersPerTeam => ModeRules.PlayersPerTeam(ModeName);
}

public record MatchListItem(
    int MatchId, DateTime MatchDate, string MapName, string ModeName,
    int Team1Score, int Team2Score, int PlayerCount,
    string? PlayerResult, int? PlayerKills, int? PlayerDeaths, int TotalRows);

public record MatchDetail(
    int MatchId, DateTime MatchDate, int MapId, string MapName, string MapType,
    int GameModeId, string ModeName, int Team1Score, int Team2Score, int RoundsRecorded)
{
    public bool IsTie => Team1Score == Team2Score;
    public int RoundsPlayed => Team1Score + Team2Score;
}

public record ScoreboardRow(
    int PlayerMatchStatId, int PlayerId, string DisplayName, int TeamNumber,
    int Kills, int Deaths, int Assists, decimal HeadshotPercentage, int Damage,
    string Result, decimal? Adr);

public record RoundRow(int RoundNumber, int WinningTeam, string WinReason, int DurationSeconds);

public record MatchSearch(
    int? PlayerId, int? MapId, int? GameModeId, DateOnly? From, DateOnly? To,
    int PageNumber, int PageSize);

public class PlayerStatInput
{
    public int PlayerId { get; set; }
    public int TeamNumber { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public decimal HeadshotPercentage { get; set; }
    public int Damage { get; set; }
}

/// <summary>One Friendships row seen from one player's side.</summary>
public record FriendItem(
    int OtherPlayerId, string DisplayName, int? PremierRank, string Status, string Direction,
    DateTime RequestedOn, DateTime? FriendsSince)
{
    public bool IsAccepted => Status == "Accepted";
    public bool IsIncomingRequest => Status == "Pending" && Direction == "Incoming";
    public bool IsOutgoingRequest => Status == "Pending" && Direction == "Outgoing";
}

// ---- Aggregating query results ----

public record PlayerTotals(
    int PlayerId, string DisplayName, int GamesPlayed, int TotalKills, int TotalDeaths,
    int TotalAssists, int TotalDamage, int TotalWins, decimal? AvgHeadshotPct)
{
    public decimal? KillDeathRatio => TotalDeaths == 0 ? null : Math.Round((decimal)TotalKills / TotalDeaths, 2);
    public decimal? WinRate => GamesPlayed == 0 ? null : Math.Round(TotalWins * 100m / GamesPlayed, 1);
}

public record FriendComparisonRow(
    int FriendId, string FriendName, int FriendGamesPlayed,
    decimal? PlayerKd, decimal? FriendKd, decimal? KdDifference,
    decimal? PlayerAdr, decimal? FriendAdr, decimal? AdrDifference,
    decimal? PlayerWinRate, decimal? FriendWinRate, decimal? WinRateDifference);

public record MapStatRow(
    string MapName, int GamesPlayed, int Wins, decimal WinRate,
    decimal AvgKills, decimal AvgDamage, int MapRank);

public record MonthlyTrendRow(
    DateOnly Month, int NumGames, decimal? KillDeathRatio,
    decimal? AverageDamagePerRound, decimal WinRate);
