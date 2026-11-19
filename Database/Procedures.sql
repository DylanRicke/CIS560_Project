/* =====================================================================
   CS Pub Match Stat Tracker - Procedures.sql
   Every SQL statement the web app runs is a stored procedure in this
   file. The C# code only calls these procedures by name.

   Run order:  Tables.sql -> Procedures.sql -> Data.sql

   Contents
     1. Table type used to pass a match's player rows to a procedure
     2. Aggregating queries (the four report procedures)
     3. Players
     4. Maps
     5. Game modes
     6. Matches and player match stats
     7. Friendships

   Conventions
     - Date ranges are inclusive: @EndDate covers the whole day.
     - Matches with IsRemoved = 1 are left out of every list and report.
     - K/D = kills / deaths. ADR = damage / rounds played, where rounds
       played = Team1Score + Team2Score. Win rate is a percentage of
       games played; ties count as games but not as wins.
     - Errors meant for the user are raised with THROW and numbers
       50001-50099; the app shows their message text.
   ===================================================================== */

SET NOCOUNT ON;
GO

/* =====================================================================
   1. TABLE TYPE
   ===================================================================== */

-- Procedures that take the type have to go before the type can be dropped.
DROP PROCEDURE IF EXISTS dbo.Matches_Import;
DROP PROCEDURE IF EXISTS dbo.Matches_Update;
DROP TYPE IF EXISTS dbo.PlayerStatInput;
GO

/* One row per player in a match, sent from the app as a table-valued
   parameter. Team (1 or 2) is only used to work out each player's
   Result from the final score; it isn't stored. */
CREATE TYPE dbo.PlayerStatInput AS TABLE
(
    PlayerID        INT      NOT NULL PRIMARY KEY,
    Team            TINYINT  NULL,
    Kills           SMALLINT NOT NULL,
    Deaths          SMALLINT NOT NULL,
    Assists         SMALLINT NOT NULL,
    HeadshotCount   SMALLINT NOT NULL,
    Damage          INT      NOT NULL
);
GO

/* =====================================================================
   2. AGGREGATING QUERIES
   ===================================================================== */

/* ---------------------------------------------------------------------
   Report_PlayerTotals
   Sums a player's PlayerMatchStats rows to get accumulated totals
   from @StartDate up to today. Pass NULL for @StartDate to cover all
   time. A player with no games in the range still gets one row of zeros.
   Used by: Player Profile page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Report_PlayerTotals
    @PlayerID  INT,
    @StartDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID,
           p.Username                                               AS DisplayName,
           COUNT(s.PlayerMatchStatID)                               AS GamesPlayed,
           ISNULL(SUM(s.Kills), 0)                                  AS TotalKills,
           ISNULL(SUM(s.Deaths), 0)                                 AS TotalDeaths,
           ISNULL(SUM(s.Assists), 0)                                AS TotalAssists,
           ISNULL(SUM(s.Damage), 0)                                 AS TotalDamage,
           SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END)        AS TotalWins
    FROM dbo.Players p
        LEFT JOIN (dbo.PlayerMatchStats s
            INNER JOIN dbo.Matches m
                ON m.MatchID = s.MatchID
               AND m.IsRemoved = 0
               AND (@StartDate IS NULL OR m.MatchDate >= @StartDate))
            ON s.PlayerID = p.PlayerID
    WHERE p.PlayerID = @PlayerID
    GROUP BY p.PlayerID, p.Username;
END;
GO

/* ---------------------------------------------------------------------
   Report_FriendComparison
   For a given player, compares their overall K/D, ADR and win rate
   against each of their friends over a date range. Each Difference
   column is player minus friend, so a positive number means the player
   is better on that stat. A friend with no games in the range shows
   FriendGamesPlayed = 0 and NULL stats.
   Friendships stores each pair once (lower PlayerID first), so friends
   are looked up from both columns.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Report_FriendComparison
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    WITH Friends AS
    (
        SELECT f.FriendID AS FriendID
        FROM dbo.Friendships f
        WHERE f.PlayerID = @PlayerID
        UNION
        SELECT f.PlayerID
        FROM dbo.Friendships f
        WHERE f.FriendID = @PlayerID
    ),
    Totals AS
    (
        SELECT s.PlayerID,
               COUNT(*)                                             AS GamesPlayed,
               SUM(s.Kills)                                         AS Kills,
               SUM(s.Deaths)                                        AS Deaths,
               SUM(s.Damage)                                        AS Damage,
               SUM(m.Team1Score + m.Team2Score)                     AS RoundsPlayed,
               SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END)    AS Wins
        FROM dbo.PlayerMatchStats s
            INNER JOIN dbo.Matches m ON m.MatchID = s.MatchID
        WHERE m.IsRemoved = 0
          AND m.MatchDate >= @StartDate
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
           p.Username                       AS FriendName,
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
        INNER JOIN dbo.Players p ON p.PlayerID = fr.FriendID
        LEFT JOIN Rates f  ON f.PlayerID = fr.FriendID
        LEFT JOIN Rates me ON me.PlayerID = @PlayerID
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
CREATE OR ALTER PROCEDURE dbo.Report_PlayerMapStats
    @PlayerID  INT,
    @StartDate DATE,
    @EndDate   DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT mp.MapName,
           COUNT(*)                                                         AS GamesPlayed,
           SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END)                AS Wins,
           CAST(SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END) * 100.0
                / COUNT(*) AS DECIMAL(5, 1))                                AS WinRate,
           CAST(AVG(s.Kills * 1.0) AS DECIMAL(5, 1))                        AS AvgKills,
           CAST(AVG(s.Damage * 1.0) AS DECIMAL(7, 1))                       AS AvgDamage,
           RANK() OVER (ORDER BY SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END) * 1.0
                                 / COUNT(*) DESC,
                                 AVG(s.Damage * 1.0) DESC)                  AS MapRank
    FROM dbo.PlayerMatchStats s
        INNER JOIN dbo.Matches m ON m.MatchID = s.MatchID
        INNER JOIN dbo.Maps mp ON mp.MapID = m.MapID
    WHERE s.PlayerID = @PlayerID
      AND m.IsRemoved = 0
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
   Used by: Player Profile page (trend chart and table).
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Report_PlayerMonthlyTrend
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
           CAST(SUM(CASE WHEN s.Result = 'Win' THEN 1 ELSE 0 END) * 100.0
                / COUNT(*) AS DECIMAL(5, 1))                                AS WinRate
    FROM dbo.PlayerMatchStats s
        INNER JOIN dbo.Matches m ON m.MatchID = s.MatchID
    WHERE s.PlayerID = @PlayerID
      AND m.IsRemoved = 0
      AND m.MatchDate >= @StartDate
      AND m.MatchDate < DATEADD(DAY, 1, @EndDate)
    GROUP BY DATEFROMPARTS(YEAR(m.MatchDate), MONTH(m.MatchDate), 1)
    ORDER BY [Month];
END;
GO

/* =====================================================================
   3. PLAYERS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Players_Search
   Lists players, optionally filtered by part of a username or email
   and by rank, with the number of (non-removed) matches each has played.
   Used by: Players page, and the player pickers on other pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Players_Search
    @Search VARCHAR(255) = NULL,
    @Rank   VARCHAR(20)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID,
           p.Username,
           p.Email,
           p.JoinDate,
           p.[Rank],
           COUNT(s.PlayerMatchStatID) AS GamesPlayed
    FROM dbo.Players p
        LEFT JOIN (dbo.PlayerMatchStats s
            INNER JOIN dbo.Matches m
                ON m.MatchID = s.MatchID
               AND m.IsRemoved = 0)
            ON s.PlayerID = p.PlayerID
    WHERE (@Search IS NULL
           OR p.Username LIKE '%' + @Search + '%'
           OR p.Email LIKE '%' + @Search + '%')
      AND (@Rank IS NULL OR p.[Rank] = @Rank)
    GROUP BY p.PlayerID, p.Username, p.Email, p.JoinDate, p.[Rank]
    ORDER BY p.Username;
END;
GO

/* ---------------------------------------------------------------------
   Players_Get
   Returns one player.
   Used by: Player Profile and Edit Player pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Players_Get
    @PlayerID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID, p.Username, p.Email, p.JoinDate, p.[Rank]
    FROM dbo.Players p
    WHERE p.PlayerID = @PlayerID;
END;
GO

/* ---------------------------------------------------------------------
   Players_Insert
   Adds a player. JoinDate defaults to today. Returns the new PlayerID.
   Duplicate usernames or emails are rejected by the unique constraints.
   Used by: Add Player page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Players_Insert
    @Username VARCHAR(32),
    @Email    VARCHAR(255),
    @Rank     VARCHAR(20),
    @PlayerID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT dbo.Players (Username, Email, [Rank])
    VALUES (LTRIM(RTRIM(@Username)), LOWER(LTRIM(RTRIM(@Email))), @Rank);

    SET @PlayerID = CAST(SCOPE_IDENTITY() AS INT);
END;
GO

/* ---------------------------------------------------------------------
   Players_Update
   Changes a player's username, email and rank.
   Used by: Edit Player page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Players_Update
    @PlayerID INT,
    @Username VARCHAR(32),
    @Email    VARCHAR(255),
    @Rank     VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Players
    SET Username = LTRIM(RTRIM(@Username)),
        Email    = LOWER(LTRIM(RTRIM(@Email))),
        [Rank]   = @Rank
    WHERE PlayerID = @PlayerID;

    IF @@ROWCOUNT = 0
        THROW 50020, N'That player no longer exists.', 1;
END;
GO

/* =====================================================================
   4. MAPS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Maps_List
   Lists every map with the number of (non-removed) matches played on it.
   Used by: Manage Maps page and the map pickers.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Maps_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT mp.MapID,
           mp.MapName,
           mp.MapType,
           COUNT(m.MatchID) AS MatchCount
    FROM dbo.Maps mp
        LEFT JOIN dbo.Matches m
            ON m.MapID = mp.MapID
           AND m.IsRemoved = 0
    GROUP BY mp.MapID, mp.MapName, mp.MapType
    ORDER BY mp.MapName;
END;
GO

/* ---------------------------------------------------------------------
   Maps_Insert
   Adds a map. Returns the new MapID.
   Used by: Manage Maps page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Maps_Insert
    @MapName VARCHAR(32),
    @MapType VARCHAR(16),
    @MapID   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT dbo.Maps (MapName, MapType)
    VALUES (LTRIM(RTRIM(@MapName)), @MapType);

    SET @MapID = CAST(SCOPE_IDENTITY() AS INT);
END;
GO

/* ---------------------------------------------------------------------
   Maps_Update
   Renames a map or changes its type.
   Used by: Manage Maps page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Maps_Update
    @MapID   INT,
    @MapName VARCHAR(32),
    @MapType VARCHAR(16)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Maps
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
   Lists every game mode with the number of (non-removed) matches.
   Used by: Manage Game Modes page and the mode pickers.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.GameModes_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT gm.GameModeID,
           gm.ModeName,
           gm.PlayersPerTeam,
           gm.MaxRounds,
           COUNT(m.MatchID) AS MatchCount
    FROM dbo.GameModes gm
        LEFT JOIN dbo.Matches m
            ON m.GameModeID = gm.GameModeID
           AND m.IsRemoved = 0
    GROUP BY gm.GameModeID, gm.ModeName, gm.PlayersPerTeam, gm.MaxRounds
    ORDER BY gm.ModeName;
END;
GO

/* ---------------------------------------------------------------------
   GameModes_Insert
   Adds a game mode. Returns the new GameModeID.
   Used by: Manage Game Modes page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.GameModes_Insert
    @ModeName       VARCHAR(20),
    @PlayersPerTeam TINYINT,
    @MaxRounds      TINYINT,
    @GameModeID     INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT dbo.GameModes (ModeName, PlayersPerTeam, MaxRounds)
    VALUES (LTRIM(RTRIM(@ModeName)), @PlayersPerTeam, @MaxRounds);

    SET @GameModeID = CAST(SCOPE_IDENTITY() AS INT);
END;
GO

/* ---------------------------------------------------------------------
   GameModes_Update
   Changes a game mode. Refuses a team size or round limit that matches
   already recorded in this mode would break.
   Used by: Manage Game Modes page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.GameModes_Update
    @GameModeID     INT,
    @ModeName       VARCHAR(20),
    @PlayersPerTeam TINYINT,
    @MaxRounds      TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1
               FROM dbo.PlayerMatchStats s
                   INNER JOIN dbo.Matches m ON m.MatchID = s.MatchID
               WHERE m.GameModeID = @GameModeID
               GROUP BY s.MatchID
               HAVING COUNT(*) > 2 * @PlayersPerTeam)
        THROW 50040, N'Some matches in this mode have more players than that team size allows.', 1;

    IF EXISTS (SELECT 1
               FROM dbo.Matches m
               WHERE m.GameModeID = @GameModeID
                 AND (m.Team1Score > @MaxRounds OR m.Team2Score > @MaxRounds))
        THROW 50041, N'Some matches in this mode have a score higher than that round limit.', 1;

    UPDATE dbo.GameModes
    SET ModeName       = LTRIM(RTRIM(@ModeName)),
        PlayersPerTeam = @PlayersPerTeam,
        MaxRounds      = @MaxRounds
    WHERE GameModeID = @GameModeID;

    IF @@ROWCOUNT = 0
        THROW 50042, N'That game mode no longer exists.', 1;
END;
GO

/* =====================================================================
   6. MATCHES AND PLAYER MATCH STATS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Matches_Search
   Lists matches newest first, one page at a time, with optional
   filters for player, map, mode and date range. When @PlayerID is
   given, each row also carries that player's result, kills and deaths.
   TotalRows is the number of matches across all pages.
   Used by: Matches page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Matches_Search
    @PlayerID       INT  = NULL,
    @MapID          INT  = NULL,
    @GameModeID     INT  = NULL,
    @StartDate      DATE = NULL,
    @EndDate        DATE = NULL,
    @IncludeRemoved BIT  = 0,
    @PageNumber     INT  = 1,
    @PageSize       INT  = 25
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
           m.IsRemoved,
           (SELECT COUNT(*) FROM dbo.PlayerMatchStats c WHERE c.MatchID = m.MatchID) AS PlayerCount,
           ps.Result   AS PlayerResult,
           ps.Kills    AS PlayerKills,
           ps.Deaths   AS PlayerDeaths,
           COUNT(*) OVER ()                                                          AS TotalRows
    FROM dbo.Matches m
        INNER JOIN dbo.Maps mp ON mp.MapID = m.MapID
        INNER JOIN dbo.GameModes gm ON gm.GameModeID = m.GameModeID
        LEFT JOIN dbo.PlayerMatchStats ps
            ON ps.MatchID = m.MatchID
           AND ps.PlayerID = @PlayerID
    WHERE (@IncludeRemoved = 1 OR m.IsRemoved = 0)
      AND (@PlayerID IS NULL OR ps.PlayerMatchStatID IS NOT NULL)
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
   Returns one match with its map and mode.
   Used by: Match Details and Edit Match pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Matches_Get
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT m.MatchID,
           m.MatchDate,
           m.MapID,
           mp.MapName,
           m.GameModeID,
           gm.ModeName,
           gm.PlayersPerTeam,
           gm.MaxRounds,
           m.Team1Score,
           m.Team2Score,
           m.IsRemoved
    FROM dbo.Matches m
        INNER JOIN dbo.Maps mp ON mp.MapID = m.MapID
        INNER JOIN dbo.GameModes gm ON gm.GameModeID = m.GameModeID
    WHERE m.MatchID = @MatchID;
END;
GO

/* ---------------------------------------------------------------------
   PlayerMatchStats_ListByMatch
   Returns the scoreboard for one match: every player's line plus
   headshot % and ADR. Team is worked out from Result and the score
   (winners are on the side with the higher score); it is NULL for a tie
   because the schema doesn't store sides.
   Used by: Match Details and Edit Match pages.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.PlayerMatchStats_ListByMatch
    @MatchID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.PlayerMatchStatID,
           s.PlayerID,
           p.Username,
           s.Kills,
           s.Deaths,
           s.Assists,
           s.HeadshotCount,
           s.Damage,
           s.Result,
           CAST(s.HeadshotCount * 100.0 / NULLIF(s.Kills, 0) AS DECIMAL(5, 1))            AS HeadshotPct,
           CAST(s.Damage * 1.0 / (m.Team1Score + m.Team2Score) AS DECIMAL(6, 1))          AS ADR,
           CAST(CASE
                    WHEN s.Result = 'Tie' THEN NULL
                    WHEN s.Result = 'Win'  AND m.Team1Score > m.Team2Score THEN 1
                    WHEN s.Result = 'Loss' AND m.Team1Score < m.Team2Score THEN 1
                    ELSE 2
                END AS TINYINT)                                                           AS Team
    FROM dbo.PlayerMatchStats s
        INNER JOIN dbo.Matches m ON m.MatchID = s.MatchID
        INNER JOIN dbo.Players p ON p.PlayerID = s.PlayerID
    WHERE s.MatchID = @MatchID
    ORDER BY Team, s.Damage DESC;
END;
GO

/* ---------------------------------------------------------------------
   Matches_Import
   Manual data import: inserts one match and every player's stat line
   in a single transaction, so a match is never saved half-finished.
   Each player's Result (Win/Loss/Tie) is worked out from their team
   and the final score. Returns the new MatchID.
   Used by: Import Match page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Matches_Import
    @MapID       INT,
    @GameModeID  INT,
    @MatchDate   DATETIME,
    @Team1Score  TINYINT,
    @Team2Score  TINYINT,
    @Stats       dbo.PlayerStatInput READONLY,
    @MatchID     INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @PlayersPerTeam TINYINT,
            @MaxRounds      TINYINT,
            @RoundsPlayed   INT = CAST(@Team1Score AS INT) + @Team2Score,
            @Message        NVARCHAR(300);

    SELECT @PlayersPerTeam = gm.PlayersPerTeam,
           @MaxRounds      = gm.MaxRounds
    FROM dbo.GameModes gm
    WHERE gm.GameModeID = @GameModeID;

    IF @PlayersPerTeam IS NULL
        THROW 50050, N'Choose a game mode.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Maps WHERE MapID = @MapID)
        THROW 50051, N'Choose a map.', 1;

    IF @MatchDate > DATEADD(DAY, 1, GETDATE())
        THROW 50052, N'The match date can''t be in the future.', 1;

    IF @RoundsPlayed = 0
        THROW 50053, N'Enter the final score. Both scores can''t be 0.', 1;

    IF @Team1Score > @MaxRounds OR @Team2Score > @MaxRounds
    BEGIN
        SET @Message = CONCAT(N'A score can''t be higher than ', @MaxRounds, N', the round limit for this mode.');
        THROW 50054, @Message, 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM @Stats)
        THROW 50055, N'Add at least one player.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Team IS NULL OR Team NOT IN (1, 2))
        THROW 50056, N'Every player needs a team (1 or 2).', 1;

    IF EXISTS (SELECT 1 FROM @Stats GROUP BY Team HAVING COUNT(*) > @PlayersPerTeam)
    BEGIN
        SET @Message = CONCAT(N'This mode allows at most ', @PlayersPerTeam, N' players per team.');
        THROW 50057, @Message, 1;
    END;

    IF EXISTS (SELECT 1 FROM @Stats s WHERE NOT EXISTS (SELECT 1 FROM dbo.Players p WHERE p.PlayerID = s.PlayerID))
        THROW 50058, N'One of the players doesn''t exist.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills < 0 OR Deaths < 0 OR Assists < 0 OR HeadshotCount < 0 OR Damage < 0)
        THROW 50059, N'Stats can''t be negative.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE HeadshotCount > Kills)
        THROW 50060, N'A player can''t have more headshots than kills.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Deaths > @RoundsPlayed)
        THROW 50061, N'A player can''t die more times than there were rounds.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT dbo.Matches (MapID, GameModeID, MatchDate, Team1Score, Team2Score)
        VALUES (@MapID, @GameModeID, @MatchDate, @Team1Score, @Team2Score);

        SET @MatchID = CAST(SCOPE_IDENTITY() AS INT);

        INSERT dbo.PlayerMatchStats
            (MatchID, PlayerID, Kills, Deaths, Assists, HeadshotCount, Damage, Result)
        SELECT @MatchID,
               s.PlayerID,
               s.Kills,
               s.Deaths,
               s.Assists,
               s.HeadshotCount,
               s.Damage,
               CASE
                   WHEN @Team1Score = @Team2Score THEN 'Tie'
                   WHEN (s.Team = 1 AND @Team1Score > @Team2Score)
                     OR (s.Team = 2 AND @Team2Score > @Team1Score) THEN 'Win'
                   ELSE 'Loss'
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
   kills, deaths, assists, headshots and damage, in one transaction.
   The score and the roster can't change here because each player's
   Result depends on them. The Team column of @Stats is ignored.
   Used by: Edit Match page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Matches_Update
    @MatchID    INT,
    @MapID      INT,
    @MatchDate  DATETIME,
    @Stats      dbo.PlayerStatInput READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RoundsPlayed INT;

    SELECT @RoundsPlayed = CAST(m.Team1Score AS INT) + m.Team2Score
    FROM dbo.Matches m
    WHERE m.MatchID = @MatchID
      AND m.IsRemoved = 0;

    IF @RoundsPlayed IS NULL
        THROW 50070, N'That match doesn''t exist or has been removed.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Maps WHERE MapID = @MapID)
        THROW 50071, N'Choose a map.', 1;

    IF @MatchDate > DATEADD(DAY, 1, GETDATE())
        THROW 50072, N'The match date can''t be in the future.', 1;

    IF EXISTS (SELECT 1 FROM @Stats i
               WHERE NOT EXISTS (SELECT 1 FROM dbo.PlayerMatchStats s
                                 WHERE s.MatchID = @MatchID AND s.PlayerID = i.PlayerID))
        THROW 50073, N'One of the players isn''t in this match.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Kills < 0 OR Deaths < 0 OR Assists < 0 OR HeadshotCount < 0 OR Damage < 0)
        THROW 50074, N'Stats can''t be negative.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE HeadshotCount > Kills)
        THROW 50075, N'A player can''t have more headshots than kills.', 1;

    IF EXISTS (SELECT 1 FROM @Stats WHERE Deaths > @RoundsPlayed)
        THROW 50076, N'A player can''t die more times than there were rounds.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE dbo.Matches
        SET MapID     = @MapID,
            MatchDate = @MatchDate
        WHERE MatchID = @MatchID;

        UPDATE s
        SET s.Kills         = i.Kills,
            s.Deaths        = i.Deaths,
            s.Assists       = i.Assists,
            s.HeadshotCount = i.HeadshotCount,
            s.Damage        = i.Damage
        FROM dbo.PlayerMatchStats s
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
   Matches_SetRemoved
   Soft delete: marks a match removed (1) or restores it (0). Removed
   matches keep their rows but drop out of lists and stats.
   Used by: Match Details page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Matches_SetRemoved
    @MatchID   INT,
    @IsRemoved BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Matches
    SET IsRemoved = @IsRemoved
    WHERE MatchID = @MatchID;

    IF @@ROWCOUNT = 0
        THROW 50080, N'That match doesn''t exist.', 1;
END;
GO

/* =====================================================================
   7. FRIENDSHIPS
   ===================================================================== */

/* ---------------------------------------------------------------------
   Friendships_ListForPlayer
   Lists a player's friends, looking in both columns of Friendships.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Friendships_ListForPlayer
    @PlayerID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.PlayerID AS FriendID,
           p.Username,
           p.[Rank],
           f.FriendsSince
    FROM dbo.Friendships f
        INNER JOIN dbo.Players p
            ON p.PlayerID = CASE WHEN f.PlayerID = @PlayerID THEN f.FriendID ELSE f.PlayerID END
    WHERE f.PlayerID = @PlayerID
       OR f.FriendID = @PlayerID
    ORDER BY p.Username;
END;
GO

/* ---------------------------------------------------------------------
   Friendships_Add
   Makes two players friends. Stores the pair with the lower PlayerID
   first to satisfy CK_Friendships_Order.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Friendships_Add
    @PlayerID INT,
    @FriendID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Low  INT = CASE WHEN @PlayerID < @FriendID THEN @PlayerID ELSE @FriendID END,
            @High INT = CASE WHEN @PlayerID < @FriendID THEN @FriendID ELSE @PlayerID END;

    IF @PlayerID = @FriendID
        THROW 50090, N'A player can''t add themselves as a friend.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Friendships WHERE PlayerID = @Low AND FriendID = @High)
        THROW 50091, N'These players are already friends.', 1;

    INSERT dbo.Friendships (PlayerID, FriendID)
    VALUES (@Low, @High);
END;
GO

/* ---------------------------------------------------------------------
   Friendships_Remove
   Ends a friendship. This is a hard delete because the row only
   records the relationship.
   Used by: Compare Friends page.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.Friendships_Remove
    @PlayerID INT,
    @FriendID INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.Friendships
    WHERE PlayerID = CASE WHEN @PlayerID < @FriendID THEN @PlayerID ELSE @FriendID END
      AND FriendID = CASE WHEN @PlayerID < @FriendID THEN @FriendID ELSE @PlayerID END;
END;
GO
