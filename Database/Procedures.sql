/* =====================================================================
   CS2 Stat Tracker - CIS 560 Team 7
   Procedures.sql : every SQL statement the web app runs is a stored
   procedure in this file. The C# code only calls these procedures by name.

   Run order:  Tables.sql -> Procedures.sql -> Data/01..08_*.sql

   Contents
     1. Table type used to pass a match's player rows to a procedure
     2. Aggregating queries (the four report procedures)
     3. Players and rank history
     4. Maps
     5. Game modes
     6. Matches, player match stats and rounds
     7. Friendships

   Conventions
     - Date ranges are inclusive: @EndDate covers the whole day.
     - Result is stored as W / L / T. Win rate is a percentage of games
       played; ties count as games but not as wins.
     - K/D = kills / deaths. ADR = damage / rounds played, where rounds
       played = Team1Score + Team2Score.
     - Only Accepted friendships count as friends in the reports.
     - Players with IsRemoved = 1 are hidden from player lists and pickers,
       but their past match stats stay on the scoreboards.
     - Errors meant for the user are raised with THROW and numbers
       50001-50099; the app shows their message text.
   ===================================================================== */

SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'Tracker') IS NULL
    EXEC(N'CREATE SCHEMA Tracker;');
GO

/* =====================================================================
   1. TABLE TYPE
   ===================================================================== */

-- Procedures that take the type have to go before the type can be dropped.
DROP PROCEDURE IF EXISTS Tracker.Matches_Import;
DROP PROCEDURE IF EXISTS Tracker.Matches_Update;
DROP TYPE IF EXISTS Tracker.PlayerStatInput;
GO

/* One row per player in a match, sent from the app as a table-valued
   parameter. TeamNumber is stored on PlayerMatchStats and decides each
   player's Result. Matches_Update ignores TeamNumber. */
CREATE TYPE Tracker.PlayerStatInput AS TABLE
(
    PlayerID           INT          NOT NULL PRIMARY KEY,
    TeamNumber         TINYINT      NOT NULL,
    Kills              SMALLINT     NOT NULL,
    Deaths             SMALLINT     NOT NULL,
    Assists            SMALLINT     NOT NULL,
    HeadshotPercentage DECIMAL(5,2) NOT NULL,
    Damage             INT          NOT NULL
);
GO

/* =====================================================================
   2. AGGREGATING QUERIES
   ===================================================================== */

/* ---------------------------------------------------------------------
   Report_PlayerTotals
   Sums a player's PlayerMatchStats rows from @StartDate up to today.
   Pass NULL for @StartDate to cover all time. A player with no games in
   the range still gets one row of zeros.
   Used by: Player Profile page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Report_PlayerTotals
    @PlayerID  INT,
    @StartDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID,
           p.DisplayName,
           COUNT(s.PlayerMatchStatID)                               AS GamesPlayed,
           ISNULL(SUM(s.Kills), 0)                                  AS TotalKills,
           ISNULL(SUM(s.Deaths), 0)                                 AS TotalDeaths,
           ISNULL(SUM(s.Assists), 0)                                AS TotalAssists,
           ISNULL(SUM(s.Damage), 0)                                 AS TotalDamage,
           ISNULL(SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END), 0) AS TotalWins,
           CAST(AVG(s.HeadshotPercentage) AS DECIMAL(5, 1))         AS AvgHeadshotPct
    FROM Tracker.Players p
        LEFT JOIN (Tracker.PlayerMatchStats s
            INNER JOIN Tracker.Matches m
                ON m.MatchID = s.MatchID
               AND (@StartDate IS NULL OR m.MatchDate >= @StartDate))
            ON s.PlayerID = p.PlayerID
    WHERE p.PlayerID = @PlayerID
    GROUP BY p.PlayerID, p.DisplayName;
END;
GO

/* ---------------------------------------------------------------------
   Report_FriendComparison
   For a given player, compares their overall K/D, ADR and win rate
   against each of their accepted friends over a date range. Each
   Difference column is player minus friend, so a positive number means
   the player is better on that stat. A friend with no games in the range
   shows FriendGamesPlayed = 0 and NULL stats.
   A friendship can have been sent by either player, so friends are
   looked up from both columns.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Report_FriendComparison
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    WITH Friends AS
    (
        SELECT CASE WHEN f.PlayerID = @PlayerID THEN f.FriendID ELSE f.PlayerID END AS FriendID
        FROM Tracker.Friendships f
        WHERE f.Status = 'Accepted'
          AND (f.PlayerID = @PlayerID OR f.FriendID = @PlayerID)
    ),
    Totals AS
    (
        SELECT s.PlayerID,
               COUNT(*)                                             AS GamesPlayed,
               SUM(s.Kills)                                         AS Kills,
               SUM(s.Deaths)                                        AS Deaths,
               SUM(s.Damage)                                        AS Damage,
               SUM(m.Team1Score + m.Team2Score)                     AS RoundsPlayed,
               SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END)      AS Wins
        FROM Tracker.PlayerMatchStats s
            INNER JOIN Tracker.Matches m ON m.MatchID = s.MatchID
        WHERE m.MatchDate >= @StartDate
          AND m.MatchDate < DATEADD(DAY, 1, @EndDate)
          AND (s.PlayerID = @PlayerID
               OR s.PlayerID IN (SELECT fr.FriendID FROM Friends fr))
        GROUP BY s.PlayerID
    ),
    Rates AS
    (
        SELECT t.PlayerID,
               t.GamesPlayed,
               CAST(t.Kills * 1.0 / NULLIF(t.Deaths, 0) AS DECIMAL(6, 2))        AS KD,
               CAST(t.Damage * 1.0 / NULLIF(t.RoundsPlayed, 0) AS DECIMAL(6, 1)) AS ADR,
               CAST(t.Wins * 100.0 / t.GamesPlayed AS DECIMAL(5, 1))            AS WinRate
        FROM Totals t
    )
    SELECT fr.FriendID,
           p.DisplayName                    AS FriendName,
           ISNULL(f.GamesPlayed, 0)         AS FriendGamesPlayed,
           me.KD                            AS PlayerKD,
           f.KD                             AS FriendKD,
           me.KD - f.KD                     AS KDDifference,
           me.ADR                           AS PlayerADR,
           f.ADR                            AS FriendADR,
           me.ADR - f.ADR                   AS ADRDifference,
           me.WinRate                       AS PlayerWinRate,
           f.WinRate                        AS FriendWinRate,
           me.WinRate - f.WinRate           AS WinRateDifference
    FROM Friends fr
        INNER JOIN Tracker.Players p ON p.PlayerID = fr.FriendID
        LEFT JOIN Rates f  ON f.PlayerID = fr.FriendID
        LEFT JOIN Rates me ON me.PlayerID = @PlayerID
    WHERE p.IsRemoved = 0
    ORDER BY FriendGamesPlayed DESC, FriendName;
END;
GO

/* ---------------------------------------------------------------------
   Report_PlayerMapStats
   For a given player, shows win rate, average kills and average damage
   per match on each map over a date range, ranked from best map to
   worst. Only maps with at least 5 games played are counted.
   Best = highest win rate; average damage breaks ties.
   Used by: Player Profile page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Report_PlayerMapStats
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT mp.MapName,
           COUNT(*)                                                         AS GamesPlayed,
           SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END)                  AS Wins,
           CAST(SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END) * 100.0
                / COUNT(*) AS DECIMAL(5, 1))                                AS WinRate,
           CAST(AVG(s.Kills * 1.0) AS DECIMAL(5, 1))                        AS AvgKills,
           CAST(AVG(s.Damage * 1.0) AS DECIMAL(7, 1))                       AS AvgDamage,
           RANK() OVER (ORDER BY SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END) * 1.0
                                 / COUNT(*) DESC,
                                 AVG(s.Damage * 1.0) DESC)                  AS MapRank
    FROM Tracker.PlayerMatchStats s
        INNER JOIN Tracker.Matches m ON m.MatchID = s.MatchID
        INNER JOIN Tracker.Maps mp ON mp.MapID = m.MapID
    WHERE s.PlayerID = @PlayerID
      AND m.MatchDate >= @StartDate
      AND m.MatchDate < DATEADD(DAY, 1, @EndDate)
    GROUP BY mp.MapID, mp.MapName
    HAVING COUNT(*) >= 5
    ORDER BY MapRank, mp.MapName;
END;
GO

/* ---------------------------------------------------------------------
   Report_PlayerMonthlyTrend
   For a given player, shows games played, K/D, ADR and win rate for
   each month in a date range, so they can see whether they're
   improving. Months with no games are left out.
   Used by: Player Profile page (trend charts and table).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Report_PlayerMonthlyTrend
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DATEFROMPARTS(YEAR(m.MatchDate), MONTH(m.MatchDate), 1)          AS [Month],
           COUNT(*)                                                         AS NumGames,
           CAST(SUM(s.Kills) * 1.0 / NULLIF(SUM(s.Deaths), 0)
                AS DECIMAL(6, 2))                                           AS KillDeathRatio,
           CAST(SUM(s.Damage) * 1.0 / NULLIF(SUM(m.Team1Score + m.Team2Score), 0)
                AS DECIMAL(6, 1))                                           AS AverageDamagePerRound,
           CAST(SUM(CASE WHEN s.Result = 'W' THEN 1 ELSE 0 END) * 100.0
                / COUNT(*) AS DECIMAL(5, 1))                                AS WinRate
    FROM Tracker.PlayerMatchStats s
        INNER JOIN Tracker.Matches m ON m.MatchID = s.MatchID
    WHERE s.PlayerID = @PlayerID
      AND m.MatchDate >= @StartDate
      AND m.MatchDate < DATEADD(DAY, 1, @EndDate)
    GROUP BY DATEFROMPARTS(YEAR(m.MatchDate), MONTH(m.MatchDate), 1)
    ORDER BY [Month];
END;
GO

/* =====================================================================
   3. PLAYERS AND RANK HISTORY
   ===================================================================== */

/* ---------------------------------------------------------------------
   Players_Search
   Lists players, optionally filtered by part of a display name or
   SteamID, with the number of matches each has played. Removed players
   are left out unless @IncludeRemoved = 1.
   Used by: Players page, and the player pickers on other pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Players_Search
    @Search         NVARCHAR(32) = NULL,
    @IncludeRemoved BIT          = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID,
           p.SteamID,
           p.DisplayName,
           p.PremierRank,
           p.CompetitiveRank,
           p.CreatedOn,
           p.IsRemoved,
           COUNT(s.PlayerMatchStatID) AS GamesPlayed
    FROM Tracker.Players p
        LEFT JOIN Tracker.PlayerMatchStats s ON s.PlayerID = p.PlayerID
    WHERE (@IncludeRemoved = 1 OR p.IsRemoved = 0)
      AND (@Search IS NULL
           OR p.DisplayName LIKE N'%' + @Search + N'%'
           OR p.SteamID LIKE '%' + CAST(@Search AS VARCHAR(32)) + '%')
    GROUP BY p.PlayerID, p.SteamID, p.DisplayName, p.PremierRank,
             p.CompetitiveRank, p.CreatedOn, p.IsRemoved
    ORDER BY p.DisplayName, p.PlayerID;
END;
GO

/* ---------------------------------------------------------------------
   Players_Get
   Returns one player, removed or not.
   Used by: Player Profile and Edit Player pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Players_Get
    @PlayerID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID, p.SteamID, p.DisplayName, p.PremierRank,
           p.CompetitiveRank, p.CreatedOn, p.IsRemoved
    FROM Tracker.Players p
    WHERE p.PlayerID = @PlayerID;
END;
GO

/* ---------------------------------------------------------------------
   Players_Insert
   Adds a player. CreatedOn defaults to now. If a Premier rating is given
   it is also written to RankHistory as the player's first data point.
   Returns the new PlayerID. A duplicate SteamID is rejected by
   UQ_Players_SteamID.
   Used by: Add Player page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Players_Insert
    @SteamID         VARCHAR(17),
    @DisplayName     NVARCHAR(32),
    @PremierRank     INT     = NULL,
    @CompetitiveRank TINYINT = NULL,
    @PlayerID        INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT Tracker.Players (SteamID, DisplayName, PremierRank, CompetitiveRank)
        VALUES (LTRIM(RTRIM(@SteamID)), LTRIM(RTRIM(@DisplayName)), @PremierRank, @CompetitiveRank);

        SET @PlayerID = CAST(SCOPE_IDENTITY() AS INT);

        IF @PremierRank IS NOT NULL
            INSERT Tracker.RankHistory (PlayerID, RankType, MapID, RankValue, RecordedOn)
            VALUES (@PlayerID, 'Premier', NULL, @PremierRank, SYSUTCDATETIME());

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   Players_Update
   Changes a player's SteamID, display name and ranks. When the Premier
   rating changes, the new value is also added to RankHistory so the
   rating chart stays in step with the player's current rating.
   Used by: Edit Player page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Players_Update
    @PlayerID        INT,
    @SteamID         VARCHAR(17),
    @DisplayName     NVARCHAR(32),
    @PremierRank     INT     = NULL,
    @CompetitiveRank TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OldPremier INT,
            @Found      BIT = 0;

    SELECT @OldPremier = p.PremierRank,
           @Found      = 1
    FROM Tracker.Players p
    WHERE p.PlayerID = @PlayerID;

    IF @Found = 0
        THROW 50020, N'That player no longer exists.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE Tracker.Players
        SET SteamID         = LTRIM(RTRIM(@SteamID)),
            DisplayName     = LTRIM(RTRIM(@DisplayName)),
            PremierRank     = @PremierRank,
            CompetitiveRank = @CompetitiveRank
        WHERE PlayerID = @PlayerID;

        IF @PremierRank IS NOT NULL AND (@OldPremier IS NULL OR @OldPremier <> @PremierRank)
            INSERT Tracker.RankHistory (PlayerID, RankType, MapID, RankValue, RecordedOn)
            VALUES (@PlayerID, 'Premier', NULL, @PremierRank, SYSUTCDATETIME());

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   Players_SetRemoved
   Soft delete: marks a player removed (1) or restores them (0). A
   removed player disappears from lists, pickers and friend comparisons,
   but their rows in past matches stay so scoreboards still add up.
   Used by: Player Profile page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Players_SetRemoved
    @PlayerID  INT,
    @IsRemoved BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Tracker.Players
    SET IsRemoved = @IsRemoved
    WHERE PlayerID = @PlayerID;

    IF @@ROWCOUNT = 0
        THROW 50021, N'That player doesn''t exist.', 1;
END;
GO

/* ---------------------------------------------------------------------
   RankHistory_PremierForPlayer
   A player's Premier rating over a date range, oldest first.
   Used by: Player Profile page (Premier rating chart).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.RankHistory_PremierForPlayer
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT rh.RecordedOn,
           rh.RankValue
    FROM Tracker.RankHistory rh
    WHERE rh.PlayerID = @PlayerID
      AND rh.RankType = 'Premier'
      AND rh.RecordedOn >= @StartDate
      AND rh.RecordedOn < DATEADD(DAY, 1, @EndDate)
    ORDER BY rh.RecordedOn, rh.RankHistoryID;
END;
GO

/* ---------------------------------------------------------------------
   RankHistory_CompetitiveByMap
   A player's current Competitive rank on each map: the most recent
   RankHistory row per map (1 = Silver I ... 18 = Global Elite).
   Used by: Player Profile page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.RankHistory_CompetitiveByMap
    @PlayerID INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Latest AS
    (
        SELECT rh.MapID,
               rh.RankValue,
               rh.RecordedOn,
               ROW_NUMBER() OVER (PARTITION BY rh.MapID
                                  ORDER BY rh.RecordedOn DESC, rh.RankHistoryID DESC) AS rn
        FROM Tracker.RankHistory rh
        WHERE rh.PlayerID = @PlayerID
          AND rh.RankType = 'Competitive'
    )
    SELECT mp.MapName,
           l.RankValue,
           l.RecordedOn
    FROM Latest l
        INNER JOIN Tracker.Maps mp ON mp.MapID = l.MapID
    WHERE l.rn = 1
    ORDER BY l.RankValue DESC, mp.MapName;
END;
GO

/* =====================================================================
   4. MAPS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Maps_List
   Lists every map with the number of matches played on it.
   Used by: Manage Maps page and the map pickers.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Maps_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT mp.MapID,
           mp.MapName,
           mp.MapType,
           COUNT(m.MatchID) AS MatchCount
    FROM Tracker.Maps mp
        LEFT JOIN Tracker.Matches m ON m.MapID = mp.MapID
    GROUP BY mp.MapID, mp.MapName, mp.MapType
    ORDER BY mp.MapName;
END;
GO

/* ---------------------------------------------------------------------
   Maps_Insert
   Adds a map. Returns the new MapID.
   Used by: Manage Maps page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Maps_Insert
    @MapName NVARCHAR(32),
    @MapType VARCHAR(16),
    @MapID   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT Tracker.Maps (MapName, MapType)
    VALUES (LTRIM(RTRIM(@MapName)), @MapType);

    SET @MapID = CAST(SCOPE_IDENTITY() AS INT);
END;
GO

/* ---------------------------------------------------------------------
   Maps_Update
   Renames a map or changes its type.
   Used by: Manage Maps page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Maps_Update
    @MapID   INT,
    @MapName NVARCHAR(32),
    @MapType VARCHAR(16)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Tracker.Maps
    SET MapName = LTRIM(RTRIM(@MapName)),
        MapType = @MapType
    WHERE MapID = @MapID;

    IF @@ROWCOUNT = 0
        THROW 50030, N'That map no longer exists.', 1;
END;
GO

/* =====================================================================
   5. GAME MODES
   ===================================================================== */

/* ---------------------------------------------------------------------
   GameModes_List
   Lists every game mode with the number of matches played in it.
   Used by: Manage Game Modes page and the mode pickers.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.GameModes_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT gm.GameModeID,
           gm.ModeName,
           COUNT(m.MatchID) AS MatchCount
    FROM Tracker.GameModes gm
        LEFT JOIN Tracker.Matches m ON m.GameModeID = gm.GameModeID
    GROUP BY gm.GameModeID, gm.ModeName
    ORDER BY gm.ModeName;
END;
GO

/* ---------------------------------------------------------------------
   GameModes_Insert
   Adds a game mode. Returns the new GameModeID.
   Used by: Manage Game Modes page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.GameModes_Insert
    @ModeName   NVARCHAR(32),
    @GameModeID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT Tracker.GameModes (ModeName)
    VALUES (LTRIM(RTRIM(@ModeName)));

    SET @GameModeID = CAST(SCOPE_IDENTITY() AS INT);
END;
GO

/* ---------------------------------------------------------------------
   GameModes_Update
   Renames a game mode.
   Used by: Manage Game Modes page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.GameModes_Update
    @GameModeID INT,
    @ModeName   NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Tracker.GameModes
    SET ModeName = LTRIM(RTRIM(@ModeName))
    WHERE GameModeID = @GameModeID;

    IF @@ROWCOUNT = 0
        THROW 50042, N'That game mode no longer exists.', 1;
END;
GO

/* =====================================================================
   6. MATCHES, PLAYER MATCH STATS AND ROUNDS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Matches_Search
   Lists matches newest first, one page at a time, with optional
   filters for player, map, mode and date range. When @PlayerID is
   given, each row also carries that player's result, kills and deaths.
   TotalRows is the number of matches across all pages.
   Used by: Matches page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Matches_Search
    @PlayerID   INT  = NULL,
    @MapID      INT  = NULL,
    @GameModeID INT  = NULL,
    @StartDate  DATE = NULL,
    @EndDate    DATE = NULL,
    @PageNumber INT  = 1,
    @PageSize   INT  = 25
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize NOT BETWEEN 1 AND 200 SET @PageSize = 25;

    SELECT m.MatchID,
           m.MatchDate,
           mp.MapName,
           gm.ModeName,
           m.Team1Score,
           m.Team2Score,
           (SELECT COUNT(*) FROM Tracker.PlayerMatchStats c WHERE c.MatchID = m.MatchID) AS PlayerCount,
           ps.Result   AS PlayerResult,
           ps.Kills    AS PlayerKills,
           ps.Deaths   AS PlayerDeaths,
           COUNT(*) OVER ()                                                              AS TotalRows
    FROM Tracker.Matches m
        INNER JOIN Tracker.Maps mp ON mp.MapID = m.MapID
        INNER JOIN Tracker.GameModes gm ON gm.GameModeID = m.GameModeID
        LEFT JOIN Tracker.PlayerMatchStats ps
            ON ps.MatchID = m.MatchID
           AND ps.PlayerID = @PlayerID
    WHERE (@PlayerID IS NULL OR ps.PlayerMatchStatID IS NOT NULL)
      AND (@MapID IS NULL OR m.MapID = @MapID)
      AND (@GameModeID IS NULL OR m.GameModeID = @GameModeID)
      AND (@StartDate IS NULL OR m.MatchDate >= @StartDate)
      AND (@EndDate IS NULL OR m.MatchDate < DATEADD(DAY, 1, @EndDate))
    ORDER BY m.MatchDate DESC, m.MatchID DESC
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);
END;
GO

/* ---------------------------------------------------------------------
   Matches_Get
   Returns one match with its map, mode and how many rounds are
   recorded for it in Tracker.Rounds.
   Used by: Match Details and Edit Match pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Matches_Get
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.MatchID,
           m.MatchDate,
           m.MapID,
           mp.MapName,
           mp.MapType,
           m.GameModeID,
           gm.ModeName,
           m.Team1Score,
           m.Team2Score,
           (SELECT COUNT(*) FROM Tracker.Rounds r WHERE r.MatchID = m.MatchID) AS RoundsRecorded
    FROM Tracker.Matches m
        INNER JOIN Tracker.Maps mp ON mp.MapID = m.MapID
        INNER JOIN Tracker.GameModes gm ON gm.GameModeID = m.GameModeID
    WHERE m.MatchID = @MatchID;
END;
GO

/* ---------------------------------------------------------------------
   PlayerMatchStats_ListByMatch
   Returns the scoreboard for one match: every player's line plus ADR,
   grouped by team and sorted by damage.
   Used by: Match Details and Edit Match pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.PlayerMatchStats_ListByMatch
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.PlayerMatchStatID,
           s.PlayerID,
           p.DisplayName,
           s.TeamNumber,
           s.Kills,
           s.Deaths,
           s.Assists,
           s.HeadshotPercentage,
           s.Damage,
           s.Result,
           CAST(s.Damage * 1.0 / NULLIF(m.Team1Score + m.Team2Score, 0) AS DECIMAL(6, 1)) AS ADR
    FROM Tracker.PlayerMatchStats s
        INNER JOIN Tracker.Matches m ON m.MatchID = s.MatchID
        INNER JOIN Tracker.Players p ON p.PlayerID = s.PlayerID
    WHERE s.MatchID = @MatchID
    ORDER BY s.TeamNumber, s.Damage DESC;
END;
GO

/* ---------------------------------------------------------------------
   Rounds_ListByMatch
   Round-by-round results for one match, in order.
   Used by: Match Details page (round timeline).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Rounds_ListByMatch
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT r.RoundNumber,
           r.WinningTeam,
           r.WinReason,
           r.DurationSeconds
    FROM Tracker.Rounds r
    WHERE r.MatchID = @MatchID
    ORDER BY r.RoundNumber;
END;
GO

/* ---------------------------------------------------------------------
   Matches_Import
   Manual data import: inserts one match and every player's stat line
   in a single transaction, so a match is never saved half-finished.
   Each player's Result (W/L/T) is worked out from their TeamNumber and
   the final score. Round-by-round data isn't entered by hand, so an
   imported match has no Rounds rows. Returns the new MatchID.
   Used by: Import Match page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Matches_Import
    @MapID       INT,
    @GameModeID  INT,
    @MatchDate   DATETIME2(0),
    @Team1Score  TINYINT,
    @Team2Score  TINYINT,
    @Stats       Tracker.PlayerStatInput READONLY,
    @MatchID     INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RoundsPlayed INT = CAST(@Team1Score AS INT) + @Team2Score;

    IF NOT EXISTS (SELECT 1 FROM Tracker.GameModes WHERE GameModeID = @GameModeID)
        THROW 50050, N'Choose a game mode.', 1;

    IF NOT EXISTS (SELECT 1 FROM Tracker.Maps WHERE MapID = @MapID)
        THROW 50051, N'Choose a map.', 1;

    IF @MatchDate > DATEADD(DAY, 1, SYSDATETIME())
        THROW 50052, N'The match date can''t be in the future.', 1;

    IF @RoundsPlayed = 0
        THROW 50053, N'Enter the final score. Both scores can''t be 0.', 1;

    IF @Team1Score > 60 OR @Team2Score > 60
        THROW 50054, N'A score can''t be higher than 60.', 1;

    IF NOT EXISTS (SELECT 1 FROM @Stats)
        THROW 50055, N'Add at least one player.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE TeamNumber NOT IN (1, 2))
        THROW 50056, N'Every player needs a team (1 or 2).', 1;

    IF EXISTS (SELECT 1 FROM @Stats GROUP BY TeamNumber HAVING COUNT(*) > 10)
        THROW 50057, N'A team can have at most 10 players.', 1;

    IF EXISTS (SELECT 1 FROM @Stats s
               WHERE NOT EXISTS (SELECT 1 FROM Tracker.Players p
                                 WHERE p.PlayerID = s.PlayerID AND p.IsRemoved = 0))
        THROW 50058, N'One of the players doesn''t exist or has been removed.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills < 0 OR Deaths < 0 OR Assists < 0 OR Damage < 0)
        THROW 50059, N'Stats can''t be negative.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE HeadshotPercentage NOT BETWEEN 0 AND 100)
        THROW 50060, N'Headshot % must be between 0 and 100.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills = 0 AND HeadshotPercentage > 0)
        THROW 50062, N'A player with no kills can''t have a headshot %.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Deaths > @RoundsPlayed)
        THROW 50061, N'A player can''t die more times than there were rounds.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT Tracker.Matches (MapID, GameModeID, MatchDate, Team1Score, Team2Score)
        VALUES (@MapID, @GameModeID, @MatchDate, @Team1Score, @Team2Score);

        SET @MatchID = CAST(SCOPE_IDENTITY() AS INT);

        INSERT Tracker.PlayerMatchStats
            (MatchID, PlayerID, TeamNumber, Kills, Deaths, Assists, HeadshotPercentage, Damage, Result)
        SELECT @MatchID,
               s.PlayerID,
               s.TeamNumber,
               s.Kills,
               s.Deaths,
               s.Assists,
               s.HeadshotPercentage,
               s.Damage,
               CASE
                   WHEN @Team1Score = @Team2Score THEN 'T'
                   WHEN (s.TeamNumber = 1 AND @Team1Score > @Team2Score)
                     OR (s.TeamNumber = 2 AND @Team2Score > @Team1Score) THEN 'W'
                   ELSE 'L'
               END
        FROM @Stats s;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   Matches_Update
   Fixes a recorded match: changes its map and date and each player's
   kills, deaths, assists, headshot % and damage, in one transaction.
   The score, teams and roster can't change here because each player's
   Result and the match's Rounds rows depend on them. The TeamNumber
   column of @Stats is ignored.
   Used by: Edit Match page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Matches_Update
    @MatchID    INT,
    @MapID      INT,
    @MatchDate  DATETIME2(0),
    @Stats      Tracker.PlayerStatInput READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RoundsPlayed INT;

    SELECT @RoundsPlayed = CAST(m.Team1Score AS INT) + m.Team2Score
    FROM Tracker.Matches m
    WHERE m.MatchID = @MatchID;

    IF @RoundsPlayed IS NULL
        THROW 50070, N'That match doesn''t exist.', 1;

    IF NOT EXISTS (SELECT 1 FROM Tracker.Maps WHERE MapID = @MapID)
        THROW 50071, N'Choose a map.', 1;

    IF @MatchDate > DATEADD(DAY, 1, SYSDATETIME())
        THROW 50072, N'The match date can''t be in the future.', 1;

    IF EXISTS (SELECT 1 FROM @Stats i
               WHERE NOT EXISTS (SELECT 1 FROM Tracker.PlayerMatchStats s
                                 WHERE s.MatchID = @MatchID AND s.PlayerID = i.PlayerID))
        THROW 50073, N'One of the players isn''t in this match.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills < 0 OR Deaths < 0 OR Assists < 0 OR Damage < 0)
        THROW 50074, N'Stats can''t be negative.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE HeadshotPercentage NOT BETWEEN 0 AND 100)
        THROW 50075, N'Headshot % must be between 0 and 100.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills = 0 AND HeadshotPercentage > 0)
        THROW 50077, N'A player with no kills can''t have a headshot %.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Deaths > @RoundsPlayed)
        THROW 50076, N'A player can''t die more times than there were rounds.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE Tracker.Matches
        SET MapID     = @MapID,
            MatchDate = @MatchDate
        WHERE MatchID = @MatchID;

        UPDATE s
        SET s.Kills              = i.Kills,
            s.Deaths             = i.Deaths,
            s.Assists            = i.Assists,
            s.HeadshotPercentage = i.HeadshotPercentage,
            s.Damage             = i.Damage
        FROM Tracker.PlayerMatchStats s
            INNER JOIN @Stats i ON i.PlayerID = s.PlayerID
        WHERE s.MatchID = @MatchID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   Matches_Delete
   Deletes a match with its stat lines and rounds, children first, in
   one transaction. The schema has no soft-delete flag on Matches, so
   this is a hard delete; the app asks for confirmation first.
   Used by: Match Details page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Matches_Delete
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM Tracker.Matches WHERE MatchID = @MatchID)
        THROW 50080, N'That match doesn''t exist.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DELETE Tracker.Rounds WHERE MatchID = @MatchID;
        DELETE Tracker.PlayerMatchStats WHERE MatchID = @MatchID;
        DELETE Tracker.Matches WHERE MatchID = @MatchID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* =====================================================================
   7. FRIENDSHIPS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Friendships_ListForPlayer
   Lists every friendship row a player is part of, in either column:
   accepted friends, pending requests (sent or received) and declined
   ones. Direction says whether @PlayerID sent the request (Outgoing)
   or received it (Incoming).
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Friendships_ListForPlayer
    @PlayerID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID AS OtherPlayerID,
           p.DisplayName,
           p.PremierRank,
           f.Status,
           CASE WHEN f.PlayerID = @PlayerID THEN 'Outgoing' ELSE 'Incoming' END AS Direction,
           f.RequestedOn,
           f.FriendsSince
    FROM Tracker.Friendships f
        INNER JOIN Tracker.Players p
            ON p.PlayerID = CASE WHEN f.PlayerID = @PlayerID THEN f.FriendID ELSE f.PlayerID END
    WHERE (f.PlayerID = @PlayerID OR f.FriendID = @PlayerID)
      AND p.IsRemoved = 0
    ORDER BY p.DisplayName;
END;
GO

/* ---------------------------------------------------------------------
   Friendships_Add
   @PlayerID sends a friend request to @FriendID.
     - No row for the pair yet: inserts a Pending request.
     - The other player already sent a Pending request: accepts it.
     - An earlier request was Declined: turns it into a new Pending
       request from @PlayerID.
     - Already friends, or a request is already waiting: error.
   Returns the friendship's Status afterwards.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Friendships_Add
    @PlayerID INT,
    @FriendID INT,
    @Status   VARCHAR(10) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingID     INT,
            @ExistingFrom   INT,
            @ExistingStatus VARCHAR(10);

    IF @PlayerID = @FriendID
        THROW 50090, N'A player can''t add themselves as a friend.', 1;

    IF (SELECT COUNT(*) FROM Tracker.Players
        WHERE PlayerID IN (@PlayerID, @FriendID) AND IsRemoved = 0) < 2
        THROW 50092, N'One of these players doesn''t exist or has been removed.', 1;

    SELECT @ExistingID     = f.FriendshipID,
           @ExistingFrom   = f.PlayerID,
           @ExistingStatus = f.Status
    FROM Tracker.Friendships f
    WHERE f.LowPlayerID  = CASE WHEN @PlayerID < @FriendID THEN @PlayerID ELSE @FriendID END
      AND f.HighPlayerID = CASE WHEN @PlayerID < @FriendID THEN @FriendID ELSE @PlayerID END;

    IF @ExistingID IS NULL
    BEGIN
        INSERT Tracker.Friendships (PlayerID, FriendID, Status)
        VALUES (@PlayerID, @FriendID, 'Pending');
        SET @Status = 'Pending';
        RETURN;
    END;

    IF @ExistingStatus = 'Accepted'
        THROW 50091, N'These players are already friends.', 1;

    IF @ExistingStatus = 'Pending' AND @ExistingFrom = @PlayerID
        THROW 50093, N'A friend request is already waiting for an answer.', 1;

    IF @ExistingStatus = 'Pending'
    BEGIN
        -- They already asked us, so adding them back accepts their request.
        UPDATE Tracker.Friendships
        SET Status       = 'Accepted',
            FriendsSince = SYSUTCDATETIME()
        WHERE FriendshipID = @ExistingID;
        SET @Status = 'Accepted';
        RETURN;
    END;

    -- Declined earlier: start a fresh request from @PlayerID.
    UPDATE Tracker.Friendships
    SET PlayerID     = @PlayerID,
        FriendID     = @FriendID,
        Status       = 'Pending',
        RequestedOn  = SYSUTCDATETIME(),
        FriendsSince = NULL
    WHERE FriendshipID = @ExistingID;
    SET @Status = 'Pending';
END;
GO

/* ---------------------------------------------------------------------
   Friendships_Respond
   @PlayerID answers a Pending request that @RequesterID sent them.
   @Accept = 1 accepts it (FriendsSince = now), 0 declines it.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Friendships_Respond
    @PlayerID    INT,
    @RequesterID INT,
    @Accept      BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Tracker.Friendships
    SET Status       = CASE WHEN @Accept = 1 THEN 'Accepted' ELSE 'Declined' END,
        FriendsSince = CASE WHEN @Accept = 1 THEN SYSUTCDATETIME() ELSE NULL END
    WHERE PlayerID = @RequesterID
      AND FriendID = @PlayerID
      AND Status   = 'Pending';

    IF @@ROWCOUNT = 0
        THROW 50094, N'There''s no pending request from that player.', 1;
END;
GO

/* ---------------------------------------------------------------------
   Friendships_Remove
   Deletes the friendship row for a pair, whoever sent it: unfriends an
   accepted friend, cancels a pending request or clears a declined one.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE Tracker.Friendships_Remove
    @PlayerID INT,
    @FriendID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE Tracker.Friendships
    WHERE LowPlayerID  = CASE WHEN @PlayerID < @FriendID THEN @PlayerID ELSE @FriendID END
      AND HighPlayerID = CASE WHEN @PlayerID < @FriendID THEN @FriendID ELSE @PlayerID END;
END;
GO
