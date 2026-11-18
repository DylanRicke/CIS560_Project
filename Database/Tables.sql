/*
    CS2 Stat Tracker - CIS 560 Team 7
    Tables.sql : creates the Tracker schema and every table, with
                 primary key, unique, foreign key, check and default constraints.

    Safe to re-run: it drops the existing tables first (children before parents).

    Sections marked  [ADDED]  are additions proposed on top of the approved design
    (Rounds, RankHistory, Friendship status, Player SteamID/CreatedOn/IsRemoved).
    If the team decides against one, delete that block here AND set the matching
    flag in tools/generate_data.py before regenerating the data.
*/

/* ---------- Drop existing objects (reverse dependency order) ---------- */
DROP TABLE IF EXISTS Tracker.RankHistory;
DROP TABLE IF EXISTS Tracker.Rounds;
DROP TABLE IF EXISTS Tracker.Friendships;
DROP TABLE IF EXISTS Tracker.PlayerMatchStats;
DROP TABLE IF EXISTS Tracker.Matches;
DROP TABLE IF EXISTS Tracker.GameModes;
DROP TABLE IF EXISTS Tracker.Maps;
DROP TABLE IF EXISTS Tracker.Players;
GO

IF SCHEMA_ID(N'Tracker') IS NULL
    EXEC(N'CREATE SCHEMA Tracker;');
GO

/* ---------- Players ---------- */
CREATE TABLE Tracker.Players
(
    PlayerID        INT IDENTITY(1,1) NOT NULL,
    SteamID         VARCHAR(17)   NOT NULL,              -- [ADDED] 17-digit SteamID64
    DisplayName     NVARCHAR(32)  NOT NULL,
    PremierRank     INT           NULL,                  -- CS Rating; NULL = unranked
    CompetitiveRank TINYINT       NULL,                  -- 1 = Silver I ... 18 = Global Elite (best map rank)
    CreatedOn       DATETIME2(0)  NOT NULL CONSTRAINT DF_Players_CreatedOn DEFAULT SYSUTCDATETIME(), -- [ADDED]
    IsRemoved       BIT           NOT NULL CONSTRAINT DF_Players_IsRemoved DEFAULT 0,                -- [ADDED] soft delete

    CONSTRAINT PK_Players PRIMARY KEY (PlayerID),
    CONSTRAINT UQ_Players_SteamID UNIQUE (SteamID),
    CONSTRAINT CK_Players_SteamID CHECK (LEN(SteamID) = 17 AND SteamID NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_Players_DisplayName CHECK (LEN(LTRIM(RTRIM(DisplayName))) > 0),
    CONSTRAINT CK_Players_PremierRank CHECK (PremierRank BETWEEN 0 AND 40000),
    CONSTRAINT CK_Players_CompetitiveRank CHECK (CompetitiveRank BETWEEN 1 AND 18)
);

/* ---------- Maps ---------- */
CREATE TABLE Tracker.Maps
(
    MapID   INT IDENTITY(1,1) NOT NULL,
    MapName NVARCHAR(32) NOT NULL,
    MapType VARCHAR(16)  NOT NULL,

    CONSTRAINT PK_Maps PRIMARY KEY (MapID),
    CONSTRAINT UQ_Maps_MapName UNIQUE (MapName),
    CONSTRAINT CK_Maps_MapType CHECK (MapType IN ('Defusal', 'Hostage'))
);

/* ---------- GameModes ---------- */
CREATE TABLE Tracker.GameModes
(
    GameModeID INT IDENTITY(1,1) NOT NULL,
    ModeName   NVARCHAR(32) NOT NULL,

    CONSTRAINT PK_GameModes PRIMARY KEY (GameModeID),
    CONSTRAINT UQ_GameModes_ModeName UNIQUE (ModeName)
);

/* ---------- Matches ---------- */
CREATE TABLE Tracker.Matches
(
    MatchID    INT IDENTITY(1,1) NOT NULL,
    MapID      INT          NOT NULL,
    GameModeID INT          NOT NULL,
    MatchDate  DATETIME2(0) NOT NULL,
    Team1Score TINYINT      NOT NULL,
    Team2Score TINYINT      NOT NULL,

    CONSTRAINT PK_Matches PRIMARY KEY (MatchID),
    CONSTRAINT FK_Matches_Maps FOREIGN KEY (MapID) REFERENCES Tracker.Maps (MapID),
    CONSTRAINT FK_Matches_GameModes FOREIGN KEY (GameModeID) REFERENCES Tracker.GameModes (GameModeID),
    CONSTRAINT CK_Matches_Scores CHECK (Team1Score <= 60 AND Team2Score <= 60)
);

/* ---------- PlayerMatchStats ---------- */
CREATE TABLE Tracker.PlayerMatchStats
(
    PlayerMatchStatID  INT IDENTITY(1,1) NOT NULL,
    MatchID            INT          NOT NULL,
    PlayerID           INT          NOT NULL,
    TeamNumber         TINYINT      NOT NULL,
    Kills              SMALLINT     NOT NULL,
    Deaths             SMALLINT     NOT NULL,
    Assists            SMALLINT     NOT NULL,
    HeadshotPercentage DECIMAL(5,2) NOT NULL,   -- 0.00 - 100.00
    Damage             INT          NOT NULL,   -- total damage; ADR = Damage / (Team1Score + Team2Score)
    Result             CHAR(1)      NOT NULL,   -- W / L / T

    CONSTRAINT PK_PlayerMatchStats PRIMARY KEY (PlayerMatchStatID),
    CONSTRAINT FK_PlayerMatchStats_Matches FOREIGN KEY (MatchID) REFERENCES Tracker.Matches (MatchID),
    CONSTRAINT FK_PlayerMatchStats_Players FOREIGN KEY (PlayerID) REFERENCES Tracker.Players (PlayerID),
    CONSTRAINT UQ_PlayerMatchStats_MatchPlayer UNIQUE (MatchID, PlayerID),
    CONSTRAINT CK_PlayerMatchStats_TeamNumber CHECK (TeamNumber IN (1, 2)),
    CONSTRAINT CK_PlayerMatchStats_Counts CHECK (Kills >= 0 AND Deaths >= 0 AND Assists >= 0 AND Damage >= 0),
    CONSTRAINT CK_PlayerMatchStats_HS CHECK (HeadshotPercentage BETWEEN 0 AND 100),
    CONSTRAINT CK_PlayerMatchStats_Result CHECK (Result IN ('W', 'L', 'T'))
);

/* ---------- Friendships ----------
   One row per pair. PlayerID = who sent the request, FriendID = who received it.
   The computed Low/High columns stop the same pair being stored twice in reverse. */
CREATE TABLE Tracker.Friendships
(
    FriendshipID INT IDENTITY(1,1) NOT NULL,
    PlayerID     INT          NOT NULL,
    FriendID     INT          NOT NULL,
    Status       VARCHAR(10)  NOT NULL CONSTRAINT DF_Friendships_Status DEFAULT 'Pending',          -- [ADDED]
    RequestedOn  DATETIME2(0) NOT NULL CONSTRAINT DF_Friendships_RequestedOn DEFAULT SYSUTCDATETIME(), -- [ADDED]
    FriendsSince DATETIME2(0) NULL,     -- set when the request is accepted
    LowPlayerID  AS (CASE WHEN PlayerID < FriendID THEN PlayerID ELSE FriendID END) PERSISTED,
    HighPlayerID AS (CASE WHEN PlayerID < FriendID THEN FriendID ELSE PlayerID END) PERSISTED,

    CONSTRAINT PK_Friendships PRIMARY KEY (FriendshipID),
    CONSTRAINT FK_Friendships_Player FOREIGN KEY (PlayerID) REFERENCES Tracker.Players (PlayerID),
    CONSTRAINT FK_Friendships_Friend FOREIGN KEY (FriendID) REFERENCES Tracker.Players (PlayerID),
    CONSTRAINT UQ_Friendships_Pair UNIQUE (LowPlayerID, HighPlayerID),
    CONSTRAINT CK_Friendships_NotSelf CHECK (PlayerID <> FriendID),
    CONSTRAINT CK_Friendships_Status CHECK (Status IN ('Pending', 'Accepted', 'Declined')),
    CONSTRAINT CK_Friendships_Since CHECK
        ((Status = 'Accepted' AND FriendsSince IS NOT NULL AND FriendsSince >= RequestedOn)
      OR (Status <> 'Accepted' AND FriendsSince IS NULL))
);

/* ---------- Rounds [ADDED] ----------
   One row per round played. Lets the app show a round timeline and is the
   "thousands of rows" table the assignment asks for. */
CREATE TABLE Tracker.Rounds
(
    RoundID         INT IDENTITY(1,1) NOT NULL,
    MatchID         INT         NOT NULL,
    RoundNumber     TINYINT     NOT NULL,
    WinningTeam     TINYINT     NOT NULL,
    WinReason       VARCHAR(16) NOT NULL,
    DurationSeconds SMALLINT    NOT NULL,

    CONSTRAINT PK_Rounds PRIMARY KEY (RoundID),
    CONSTRAINT FK_Rounds_Matches FOREIGN KEY (MatchID) REFERENCES Tracker.Matches (MatchID),
    CONSTRAINT UQ_Rounds_MatchRound UNIQUE (MatchID, RoundNumber),
    CONSTRAINT CK_Rounds_RoundNumber CHECK (RoundNumber >= 1),
    CONSTRAINT CK_Rounds_WinningTeam CHECK (WinningTeam IN (1, 2)),
    CONSTRAINT CK_Rounds_WinReason CHECK (WinReason IN ('Elimination', 'BombExploded', 'BombDefused', 'TimeExpired', 'HostageRescued')),
    CONSTRAINT CK_Rounds_Duration CHECK (DurationSeconds BETWEEN 1 AND 175)
);

/* ---------- RankHistory [ADDED] ----------
   Premier rating is one number per player; Competitive rank is per map in CS2,
   so Competitive rows must name a map and Premier rows must not. */
CREATE TABLE Tracker.RankHistory
(
    RankHistoryID INT IDENTITY(1,1) NOT NULL,
    PlayerID      INT          NOT NULL,
    RankType      VARCHAR(12)  NOT NULL,
    MapID         INT          NULL,
    RankValue     INT          NOT NULL,
    RecordedOn    DATETIME2(0) NOT NULL,

    CONSTRAINT PK_RankHistory PRIMARY KEY (RankHistoryID),
    CONSTRAINT FK_RankHistory_Players FOREIGN KEY (PlayerID) REFERENCES Tracker.Players (PlayerID),
    CONSTRAINT FK_RankHistory_Maps FOREIGN KEY (MapID) REFERENCES Tracker.Maps (MapID),
    CONSTRAINT CK_RankHistory_Type CHECK
        ((RankType = 'Premier'     AND MapID IS NULL     AND RankValue BETWEEN 0 AND 40000)
      OR (RankType = 'Competitive' AND MapID IS NOT NULL AND RankValue BETWEEN 1 AND 18))
);
GO

/* ---------- Indexes for the queries the app runs most ---------- */
CREATE INDEX IX_PlayerMatchStats_Player ON Tracker.PlayerMatchStats (PlayerID) INCLUDE (MatchID, Kills, Deaths, Assists, Damage, Result);
CREATE INDEX IX_Matches_Date ON Tracker.Matches (MatchDate);
CREATE INDEX IX_Friendships_Friend ON Tracker.Friendships (FriendID);
CREATE INDEX IX_RankHistory_Player ON Tracker.RankHistory (PlayerID, RankType, RecordedOn);
CREATE INDEX IX_Players_DisplayName ON Tracker.Players (DisplayName);
GO
