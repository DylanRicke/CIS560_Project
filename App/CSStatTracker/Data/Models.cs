namespace CSStatTracker.Data;

// Plain classes that hold rows returned by the stored procedures.

public static class Ranks
{
    public static readonly string[] All =
    {
        "Unranked", "Silver", "Gold Nova", "Master Guardian",
        "Legendary Eagle", "Supreme", "Global Elite"
    };
}

public static class MapTypes
{
    public static readonly string[] All = { "Defusal", "Hostage" };
}

public record PlayerListItem(int PlayerId, string Username, string Email, DateOnly JoinDate, string Rank, int GamesPlayed);

public record Player(int PlayerId, string Username, string Email, DateOnly JoinDate, string Rank);

public record MapItem(int MapId, string MapName, string MapType, int MatchCount);

public record GameModeItem(int GameModeId, string ModeName, int PlayersPerTeam, int MaxRounds, int MatchCount);

public record MatchListItem(
    int MatchId, DateTime MatchDate, string MapName, string ModeName,
    int Team1Score, int Team2Score, bool IsRemoved, int PlayerCount,
    string? PlayerResult, int? PlayerKills, int? PlayerDeaths, int TotalRows);

public record MatchDetail(
    int MatchId, DateTime MatchDate, int MapId, string MapName, int GameModeId, string ModeName,
    int PlayersPerTeam, int MaxRounds, int Team1Score, int Team2Score, bool IsRemoved)
{
    public bool IsTie => Team1Score == Team2Score;
    public int RoundsPlayed => Team1Score + Team2Score;
}

public record ScoreboardRow(
    int PlayerMatchStatId, int PlayerId, string Username, int Kills, int Deaths, int Assists,
    int HeadshotCount, int Damage, string Result, decimal? HeadshotPct, decimal? Adr, int? Team);

public record MatchSearch(
    int? PlayerId, int? MapId, int? GameModeId, DateOnly? From, DateOnly? To,
    bool IncludeRemoved, int PageNumber, int PageSize);

public class PlayerStatInput
{
    public int PlayerId { get; set; }
    public int? Team { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int HeadshotCount { get; set; }
    public int Damage { get; set; }
}

public record FriendItem(int FriendId, string Username, string Rank, DateOnly FriendsSince);

// ---- Aggregating query results ----

public record PlayerTotals(
    int PlayerId, string DisplayName, int GamesPlayed, int TotalKills, int TotalDeaths,
    int TotalAssists, int TotalDamage, int TotalWins)
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
