/* =====================================================================
   CS Pub Match Stat Tracker - Tables.sql
   Creates every table with its primary key, unique, foreign key and
   check constraints.

   Run order:  Tables.sql -> Procedures.sql -> Data.sql
   Run this script inside the database you created for the project,
   for example:
       CREATE DATABASE CSStats;
       GO
       USE CSStats;
       GO
   The script drops the tables first so it can be re-run.
   ===================================================================== */

SET NOCOUNT ON;
GO

DROP TABLE IF EXISTS dbo.PlayerMatchStats;
DROP TABLE IF EXISTS dbo.Friendships;
DROP TABLE IF EXISTS dbo.Matches;
DROP TABLE IF EXISTS dbo.Players;
DROP TABLE IF EXISTS dbo.Maps;
DROP TABLE IF EXISTS dbo.GameModes;
GO

/* ---------------------------------------------------------------------
   Players: one row per registered player.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Players
(
    PlayerID    INT IDENTITY(1, 1) NOT NULL,
    Username    VARCHAR(32)  NOT NULL,
    Email       VARCHAR(255) NOT NULL,
    JoinDate    DATE         NOT NULL
        CONSTRAINT DF_Players_JoinDate DEFAULT (CAST(SYSDATETIME() AS DATE)),
    [Rank]      VARCHAR(20)  NOT NULL
        CONSTRAINT DF_Players_Rank DEFAULT ('Unranked'),

    CONSTRAINT PK_Players PRIMARY KEY (PlayerID),
    CONSTRAINT UQ_Players_Username UNIQUE (Username),
    CONSTRAINT UQ_Players_Email UNIQUE (Email),
    CONSTRAINT CK_Players_Username CHECK (LEN(Username) >= 3),
    CONSTRAINT CK_Players_Email CHECK (Email LIKE '%_@_%._%'),
    CONSTRAINT CK_Players_Rank CHECK ([Rank] IN
        ('Unranked', 'Silver', 'Gold Nova', 'Master Guardian',
         'Legendary Eagle', 'Supreme', 'Global Elite'))
);
GO

/* ---------------------------------------------------------------------
   Maps: every map a match can be played on.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Maps
(
    MapID       INT IDENTITY(1, 1) NOT NULL,
    MapName     VARCHAR(32) NOT NULL,
    MapType     VARCHAR(16) NOT NULL,

    CONSTRAINT PK_Maps PRIMARY KEY (MapID),
    CONSTRAINT UQ_Maps_MapName UNIQUE (MapName),
    CONSTRAINT CK_Maps_MapType CHECK (MapType IN ('Defusal', 'Hostage'))
);
GO

/* ---------------------------------------------------------------------
   GameModes: matchmaking modes and their team size / round limit.
   MaxRounds is the regulation maximum (Premier overtime can go past it).
   --------------------------------------------------------------------- */
CREATE TABLE dbo.GameModes
(
    GameModeID      INT IDENTITY(1, 1) NOT NULL,
    ModeName        VARCHAR(20) NOT NULL,
    PlayersPerTeam  TINYINT     NOT NULL,
    MaxRounds       TINYINT     NOT NULL,

    CONSTRAINT PK_GameModes PRIMARY KEY (GameModeID),
    CONSTRAINT UQ_GameModes_ModeName UNIQUE (ModeName),
    CONSTRAINT CK_GameModes_PlayersPerTeam CHECK (PlayersPerTeam BETWEEN 1 AND 10),
    CONSTRAINT CK_GameModes_MaxRounds CHECK (MaxRounds BETWEEN 1 AND 60)
);
GO

/* ---------------------------------------------------------------------
   Matches: one row per pub match.
   IsRemoved is a soft delete flag: removed matches stay in the table
   but are left out of lists and stats.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Matches
(
    MatchID     INT IDENTITY(1, 1) NOT NULL,
    MapID       INT      NOT NULL,
    GameModeID  INT      NOT NULL,
    MatchDate   DATETIME NOT NULL,
    Team1Score  TINYINT  NOT NULL,
    Team2Score  TINYINT  NOT NULL,
    IsRemoved   BIT      NOT NULL
        CONSTRAINT DF_Matches_IsRemoved DEFAULT (0),

    CONSTRAINT PK_Matches PRIMARY KEY (MatchID),
    CONSTRAINT FK_Matches_Maps FOREIGN KEY (MapID)
        REFERENCES dbo.Maps (MapID),
    CONSTRAINT FK_Matches_GameModes FOREIGN KEY (GameModeID)
        REFERENCES dbo.GameModes (GameModeID),
    CONSTRAINT CK_Matches_RoundsPlayed CHECK (Team1Score + Team2Score > 0)
);
GO

/* ---------------------------------------------------------------------
   PlayerMatchStats: one row per player per match.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.PlayerMatchStats
(
    PlayerMatchStatID   INT IDENTITY(1, 1) NOT NULL,
    MatchID             INT      NOT NULL,
    PlayerID            INT      NOT NULL,
    Kills               SMALLINT NOT NULL,
    Deaths              SMALLINT NOT NULL,
    Assists             SMALLINT NOT NULL,
    HeadshotCount       SMALLINT NOT NULL,
    Damage              INT      NOT NULL,
    Result              VARCHAR(4) NOT NULL,

    CONSTRAINT PK_PlayerMatchStats PRIMARY KEY (PlayerMatchStatID),
    CONSTRAINT UQ_PlayerMatchStats_Match_Player UNIQUE (MatchID, PlayerID),
    CONSTRAINT FK_PlayerMatchStats_Matches FOREIGN KEY (MatchID)
        REFERENCES dbo.Matches (MatchID),
    CONSTRAINT FK_PlayerMatchStats_Players FOREIGN KEY (PlayerID)
        REFERENCES dbo.Players (PlayerID),
    CONSTRAINT CK_PlayerMatchStats_NonNegative CHECK
        (Kills >= 0 AND Deaths >= 0 AND Assists >= 0 AND HeadshotCount >= 0 AND Damage >= 0),
    CONSTRAINT CK_PlayerMatchStats_Headshots CHECK (HeadshotCount <= Kills),
    CONSTRAINT CK_PlayerMatchStats_Result CHECK (Result IN ('Win', 'Loss', 'Tie'))
);
GO

/* ---------------------------------------------------------------------
   Friendships: junction table resolving the Players <-> Players
   many-to-many relationship. Each pair is stored once, with the lower
   PlayerID first, so (A, B) and (B, A) can't both exist.
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Friendships
(
    FriendshipID    INT IDENTITY(1, 1) NOT NULL,
    PlayerID        INT  NOT NULL,
    FriendID        INT  NOT NULL,
    FriendsSince    DATE NOT NULL
        CONSTRAINT DF_Friendships_FriendsSince DEFAULT (CAST(SYSDATETIME() AS DATE)),

    CONSTRAINT PK_Friendships PRIMARY KEY (FriendshipID),
    CONSTRAINT UQ_Friendships_Pair UNIQUE (PlayerID, FriendID),
    CONSTRAINT FK_Friendships_Player FOREIGN KEY (PlayerID)
        REFERENCES dbo.Players (PlayerID),
    CONSTRAINT FK_Friendships_Friend FOREIGN KEY (FriendID)
        REFERENCES dbo.Players (PlayerID),
    CONSTRAINT CK_Friendships_Order CHECK (PlayerID < FriendID)
);
GO

/* Supporting indexes for the foreign keys the reports filter on. */
CREATE INDEX IX_Matches_MatchDate ON dbo.Matches (MatchDate) INCLUDE (MapID, GameModeID, Team1Score, Team2Score, IsRemoved);
CREATE INDEX IX_PlayerMatchStats_PlayerID ON dbo.PlayerMatchStats (PlayerID) INCLUDE (MatchID, Kills, Deaths, Assists, HeadshotCount, Damage, Result);
CREATE INDEX IX_Friendships_FriendID ON dbo.Friendships (FriendID);
GO
