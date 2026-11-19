/*
    CS2 Stat Tracker - data generator (CIS 560 Team 7)

    Writes the INSERT scripts in Database/Data/ for every table in Tables.sql.
    The data is simulated round by round, so everything lines up:
      * match scores follow the real CS2 formats (MR24 first-to-13, Premier overtime,
        Competitive ties at 12-12, MR16 first-to-9 for Casual/Wingman)
      * every round row adds up to the final score
      * each player dies at most once per round and only living players get kills
      * team kills = enemy deaths, better players get higher K/D and ADR on average
      * players queue in friend "stacks", so the friend-comparison query has real data
      * Premier rating moves after every Premier match; Competitive rank is per map

    Run from anywhere inside the repo:
        dotnet run --project tools/DataGenerator
    or pass the output folder explicitly:
        dotnet run --project tools/DataGenerator -- "C:\path\to\Database\Data"

    Same Seed -> identical output, so the whole team gets the same database.
*/

using System.Globalization;
using System.Text;

namespace DataGenerator;

public static class Settings
{
    public const int Seed = 560;
    public const int NumPlayers = 150;               // includes the demo players below
    public const int NumMatches = 1200;
    public static readonly DateTime FirstMatch = new(2025, 9, 1);
    public static readonly DateTime LastMatch = new(2026, 11, 15);
    public const bool IncludeRounds = true;          // set false if the team drops Tracker.Rounds
    public const bool IncludeRankHistory = true;     // set false if the team drops Tracker.RankHistory
    public const int Batch = 1000;                   // SQL Server allows at most 1000 rows per VALUES list

    // Your own accounts for the demo. They queue together as one stack.
    // (display name, skill: 1.0 = average, 1.3 = strong, 0.8 = weak)
    public static readonly (string Name, double Skill)[] DemoPlayers =
    {
        ("Team7_Member1", 1.15),
        ("Team7_Member2", 1.00),
        ("Team7_Member3", 0.92),
        ("Team7_Member4", 1.05),
    };

    public static readonly (string Name, string Type)[] Maps =
    {
        ("Ancient", "Defusal"), ("Anubis", "Defusal"), ("Dust II", "Defusal"),
        ("Inferno", "Defusal"), ("Mirage", "Defusal"), ("Nuke", "Defusal"),
        ("Train", "Defusal"), ("Overpass", "Defusal"), ("Vertigo", "Defusal"),
        ("Office", "Hostage"), ("Italy", "Hostage"),
    };

    public static readonly GameMode[] Modes =
    {
        new("Premier",     Size: 5,  Win: 13, Reg: 24, Overtime: true,  Weight: 0.40, MapTypes: new[] { "Defusal" }),
        new("Competitive", Size: 5,  Win: 13, Reg: 24, Overtime: false, Weight: 0.30, MapTypes: new[] { "Defusal" }),
        new("Casual",      Size: 10, Win: 9,  Reg: 16, Overtime: false, Weight: 0.20, MapTypes: new[] { "Defusal", "Hostage" }),
        new("Wingman",     Size: 2,  Win: 9,  Reg: 16, Overtime: false, Weight: 0.10, MapTypes: new[] { "Defusal" }),
    };

    public static readonly string[] PremierPool = { "Ancient", "Anubis", "Dust II", "Inferno", "Mirage", "Nuke", "Train" };

    public static readonly string[] Adjectives =
    {
        "Silent", "Toxic", "Lucky", "Frozen", "Rapid", "Shadow", "Crimson", "Lazy", "Golden",
        "Rusty", "Feral", "Neon", "Salty", "Hollow", "Turbo", "Quiet", "Wild", "Atomic", "Mellow",
        "Grim", "Sneaky", "Brave", "Spicy", "Cosmic", "Velvet",
    };

    public static readonly string[] Nouns =
    {
        "Fox", "Spray", "Flick", "AWPer", "Ninja", "Rush", "Smoke", "Molly", "Lurker", "Entry",
        "Eco", "Clutch", "Peek", "Deagle", "Kobe", "Pixel", "Viper", "Raven", "Tiger", "Ghost",
        "Wolf", "Taco", "Panda", "Comet", "Otter",
    };
}

public record GameMode(string Name, int Size, int Win, int Reg, bool Overtime, double Weight, string[] MapTypes);

public class Player
{
    public int Id;
    public string Name = "";
    public string SteamId = "";
    public double Skill;
    public double Activity;
    public double Headshot;     // chance a kill is a headshot
    public double Trend;        // skill change per year
    public DateTime Created;
    public bool Removed;
    public int? Premier;        // current CS Rating
    public Dictionary<string, int> Comp = new();   // map name -> 1..18

    public double SkillAt(DateTime when)
    {
        double years = (when - Settings.FirstMatch).TotalDays / 365.0;
        return Math.Clamp(Skill + Trend * years, 0.4, 1.9);
    }
}

public class Stat
{
    public int Kills, Deaths, Assists, Headshots;
    public double Damage;
}

// ---------------------------------------------------------------- row types
public record PlayerRow(int PlayerID, string SteamID, string DisplayName, int? PremierRank, int? CompetitiveRank, DateTime CreatedOn, bool IsRemoved);
public record MatchRow(int MatchID, int MapID, int GameModeID, DateTime MatchDate, int Team1Score, int Team2Score);
public record StatRow(int PlayerMatchStatID, int MatchID, int PlayerID, int TeamNumber, int Kills, int Deaths, int Assists, decimal HeadshotPercentage, int Damage, string Result);
public record RoundRow(int RoundID, int MatchID, int RoundNumber, int WinningTeam, string WinReason, int DurationSeconds);
public record RankRow(int RankHistoryID, int PlayerID, string RankType, int? MapID, int RankValue, DateTime RecordedOn);
public record FriendRow(int FriendshipID, int PlayerID, int FriendID, string Status, DateTime RequestedOn, DateTime? FriendsSince);

// ---------------------------------------------------------------- random helpers
public class Rng
{
    private readonly Random _r;
    public Rng(int seed) => _r = new Random(seed);

    public double Next() => _r.NextDouble();
    public int Int(int lo, int hiInclusive) => _r.Next(lo, hiInclusive + 1);
    public T Pick<T>(IReadOnlyList<T> items) => items[_r.Next(items.Count)];

    public double Gauss(double mean, double sd)
    {
        double u1 = 1.0 - _r.NextDouble(), u2 = _r.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public double LogNormal(double mu, double sigma) => Math.Exp(Gauss(mu, sigma));
    public double Uniform(double lo, double hi) => lo + (hi - lo) * _r.NextDouble();

    public double Triangular(double lo, double hi, double mode)
    {
        double u = _r.NextDouble(), c = (mode - lo) / (hi - lo);
        return u < c ? lo + Math.Sqrt(u * (hi - lo) * (mode - lo))
                     : hi - Math.Sqrt((1 - u) * (hi - lo) * (hi - mode));
    }

    public int WeightedIndex(IReadOnlyList<double> weights)
    {
        double total = weights.Sum(), x = _r.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            x -= weights[i];
            if (x < 0) return i;
        }
        return weights.Count - 1;
    }

    public T WeightedPick<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights) => items[WeightedIndex(weights)];

    public DateTime Date(DateTime start, DateTime end) =>
        start.AddSeconds(_r.NextInt64(0, (long)(end - start).TotalSeconds + 1));

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _r.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public List<T> Sample<T>(IReadOnlyList<T> items, int k)
    {
        var copy = items.ToList();
        Shuffle(copy);
        return copy.Take(k).ToList();
    }
}

public static class Program
{
    private static readonly Rng R = new(Settings.Seed);

    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : FindDataFolder();
        Directory.CreateDirectory(outDir);

        var players = MakePlayers();
        var stacks = MakeStacks(players);
        var mapIds = Settings.Maps.Select((m, i) => (m.Name, Id: i + 1)).ToDictionary(x => x.Name, x => x.Id);
        var mapTypes = Settings.Maps.ToDictionary(m => m.Name, m => m.Type);
        var modeIds = Settings.Modes.Select((m, i) => (m.Name, Id: i + 1)).ToDictionary(x => x.Name, x => x.Id);

        var dates = Enumerable.Range(0, Settings.NumMatches)
            .Select(_ => R.Date(Settings.FirstMatch, Settings.LastMatch)).OrderBy(d => d).ToList();

        var matchRows = new List<MatchRow>();
        var statRows = new List<StatRow>();
        var roundRows = new List<RoundRow>();
        var rankRows = new List<RankRow>();
        int statId = 0, roundId = 0, rankId = 0;

        for (int m = 0; m < dates.Count; m++)
        {
            int matchId = m + 1;
            DateTime when = dates[m];
            var mode = R.WeightedPick(Settings.Modes, Settings.Modes.Select(x => x.Weight).ToList());
            string mapName = mode.Name == "Premier"
                ? R.Pick(Settings.PremierPool)
                : R.Pick(Settings.Maps.Where(x => mode.MapTypes.Contains(x.Type)).Select(x => x.Name).ToList());

            var teams = PickTeams(stacks, mode.Size);
            var (score, rounds, stats) = SimulateMatch(mode, mapTypes[mapName], teams, when);

            matchRows.Add(new MatchRow(matchId, mapIds[mapName], modeIds[mode.Name], when, score[0], score[1]));
            if (Settings.IncludeRounds)
                foreach (var (no, winTeam, reason, dur) in rounds)
                    roundRows.Add(new RoundRow(++roundId, matchId, no, winTeam, reason, dur));

            for (int t = 0; t < 2; t++)
            {
                int mine = score[t], theirs = score[1 - t];
                string result = mine > theirs ? "W" : mine < theirs ? "L" : "T";
                foreach (var p in teams[t])
                {
                    var s = stats[p.Id];
                    decimal hs = s.Kills > 0 ? Math.Round(100m * s.Headshots / s.Kills, 2) : 0m;
                    statRows.Add(new StatRow(++statId, matchId, p.Id, t + 1, s.Kills, s.Deaths, s.Assists, hs, (int)s.Damage, result));

                    // rank changes
                    DateTime stamp = when.AddMinutes(45);
                    if (mode.Name == "Premier")
                    {
                        if (p.Premier is null)
                            p.Premier = (int)Math.Clamp(R.Gauss(14000 + (p.Skill - 1) * 22000, 1500), 1000, 32000);
                        else
                            p.Premier = Math.Clamp(p.Premier.Value + R.Int(90, 260) * (result == "W" ? 1 : -1), 0, 40000);
                        rankRows.Add(new RankRow(++rankId, p.Id, "Premier", null, p.Premier.Value, stamp));
                    }
                    else if (mode.Name == "Competitive")
                    {
                        int? old = p.Comp.TryGetValue(mapName, out int o) ? o : null;
                        int next;
                        if (old is null)
                            next = (int)Math.Clamp(Math.Round(R.Gauss(9 + (p.Skill - 1) * 14, 1.5)), 1, 18);
                        else if (result == "W" && R.Next() < 0.25)
                            next = Math.Min(18, old.Value + 1);
                        else if (result == "L" && R.Next() < 0.25)
                            next = Math.Max(1, old.Value - 1);
                        else
                            next = old.Value;

                        if (next != old)
                        {
                            p.Comp[mapName] = next;
                            rankRows.Add(new RankRow(++rankId, p.Id, "Competitive", mapIds[mapName], next, stamp));
                        }
                    }
                }
            }
        }

        var friendRows = MakeFriendships(players, stacks);

        var playerRows = players.Select(p => new PlayerRow(p.Id, p.SteamId, p.Name, p.Premier,
            p.Comp.Count > 0 ? p.Comp.Values.Max() : null, p.Created, p.Removed)).ToList();

        Validate(playerRows, matchRows, statRows, roundRows, rankRows, friendRows);

        WriteTable(outDir, "01_Players.sql", "Tracker.Players",
            new[] { "PlayerID", "SteamID", "DisplayName", "PremierRank", "CompetitiveRank", "CreatedOn", "IsRemoved" },
            playerRows.Select(r => new object?[] { r.PlayerID, r.SteamID, r.DisplayName, r.PremierRank, r.CompetitiveRank, r.CreatedOn, r.IsRemoved }));
        WriteTable(outDir, "02_Maps.sql", "Tracker.Maps", new[] { "MapID", "MapName", "MapType" },
            Settings.Maps.Select((m, i) => new object?[] { i + 1, m.Name, m.Type }));
        WriteTable(outDir, "03_GameModes.sql", "Tracker.GameModes", new[] { "GameModeID", "ModeName" },
            Settings.Modes.Select((m, i) => new object?[] { i + 1, m.Name }));
        WriteTable(outDir, "04_Matches.sql", "Tracker.Matches",
            new[] { "MatchID", "MapID", "GameModeID", "MatchDate", "Team1Score", "Team2Score" },
            matchRows.Select(r => new object?[] { r.MatchID, r.MapID, r.GameModeID, r.MatchDate, r.Team1Score, r.Team2Score }));
        WriteTable(outDir, "05_PlayerMatchStats.sql", "Tracker.PlayerMatchStats",
            new[] { "PlayerMatchStatID", "MatchID", "PlayerID", "TeamNumber", "Kills", "Deaths", "Assists", "HeadshotPercentage", "Damage", "Result" },
            statRows.Select(r => new object?[] { r.PlayerMatchStatID, r.MatchID, r.PlayerID, r.TeamNumber, r.Kills, r.Deaths, r.Assists, r.HeadshotPercentage, r.Damage, r.Result }));
        WriteTable(outDir, "06_Friendships.sql", "Tracker.Friendships",
            new[] { "FriendshipID", "PlayerID", "FriendID", "Status", "RequestedOn", "FriendsSince" },
            friendRows.Select(r => new object?[] { r.FriendshipID, r.PlayerID, r.FriendID, r.Status, r.RequestedOn, r.FriendsSince }));
        if (Settings.IncludeRounds)
            WriteTable(outDir, "07_Rounds.sql", "Tracker.Rounds",
                new[] { "RoundID", "MatchID", "RoundNumber", "WinningTeam", "WinReason", "DurationSeconds" },
                roundRows.Select(r => new object?[] { r.RoundID, r.MatchID, r.RoundNumber, r.WinningTeam, r.WinReason, r.DurationSeconds }));
        if (Settings.IncludeRankHistory)
            WriteTable(outDir, "08_RankHistory.sql", "Tracker.RankHistory",
                new[] { "RankHistoryID", "PlayerID", "RankType", "MapID", "RankValue", "RecordedOn" },
                rankRows.Select(r => new object?[] { r.RankHistoryID, r.PlayerID, r.RankType, r.MapID, r.RankValue, r.RecordedOn }));

        Console.WriteLine($"Players {playerRows.Count} | Maps {Settings.Maps.Length} | GameModes {Settings.Modes.Length} | " +
                          $"Matches {matchRows.Count} | PlayerMatchStats {statRows.Count} | Friendships {friendRows.Count} | " +
                          $"Rounds {roundRows.Count} | RankHistory {rankRows.Count}");
        Console.WriteLine($"Written to {outDir}");
        return 0;
    }

    /// <summary>Walks up from the current folder until it finds the repo's Database folder.</summary>
    private static string FindDataFolder()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Database");
            if (Directory.Exists(candidate)) return Path.Combine(candidate, "Data");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Couldn't find the Database folder. Run from inside the repo or pass the output folder as an argument.");
    }

    // ------------------------------------------------------------ players and stacks
    private static List<Player> MakePlayers()
    {
        var players = new List<Player>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamIds = new HashSet<string>();

        for (int id = 1; id <= Settings.NumPlayers; id++)
        {
            var p = new Player { Id = id };
            if (id <= Settings.DemoPlayers.Length)
            {
                (p.Name, p.Skill) = Settings.DemoPlayers[id - 1];
                p.Activity = 2.5;                                  // demo accounts play a lot
            }
            else
            {
                string name;
                do
                {
                    name = R.Pick(Settings.Adjectives) + R.Pick(Settings.Nouns);
                    if (R.Next() < 0.4) name += R.Int(1, 999);
                } while (names.Contains(name));
                p.Name = name;
                p.Skill = Math.Clamp(R.Gauss(1.0, 0.22), 0.5, 1.7);
                p.Activity = R.LogNormal(0, 0.6);
            }
            names.Add(p.Name);

            string sid;
            do { sid = "7656119" + R.Int(1_000_000_000, int.MaxValue - 1); }   // 7 + 10 digits = 17
            while (!steamIds.Add(sid));
            p.SteamId = sid;

            p.Headshot = Math.Clamp(R.Gauss(0.45, 0.09), 0.18, 0.78);
            p.Trend = R.Gauss(0.04, 0.08);
            p.Created = R.Date(Settings.FirstMatch.AddDays(-400), Settings.FirstMatch.AddDays(-1));
            p.Removed = id > Settings.DemoPlayers.Length && R.Next() < 0.03;
            players.Add(p);
        }
        return players;
    }

    /// <summary>Groups of 1-5 friends who queue together. Demo players are one stack.</summary>
    private static List<List<Player>> MakeStacks(List<Player> players)
    {
        var demo = players.Where(p => p.Id <= Settings.DemoPlayers.Length).ToList();
        var rest = players.Where(p => p.Id > Settings.DemoPlayers.Length).ToList();
        R.Shuffle(rest);

        var stacks = new List<List<Player>> { demo };
        double[] sizeWeights = { 30, 25, 20, 15, 10 };
        for (int i = 0; i < rest.Count;)
        {
            int size = R.WeightedIndex(sizeWeights) + 1;
            stacks.Add(rest.Skip(i).Take(size).ToList());
            i += size;
        }
        return stacks;
    }

    private static List<FriendRow> MakeFriendships(List<Player> players, List<List<Player>> stacks)
    {
        var rows = new List<FriendRow>();
        var pairs = new HashSet<(int, int)>();

        void Add(Player a, Player b, string status)
        {
            var key = (Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id));
            if (a.Id == b.Id || !pairs.Add(key)) return;
            DateTime requested = R.Date(a.Created > b.Created ? a.Created : b.Created, Settings.FirstMatch.AddDays(60));
            DateTime? since = status == "Accepted" ? requested.AddHours(R.Int(1, 120)) : null;
            var (sender, receiver) = R.Next() < 0.5 ? (a, b) : (b, a);
            rows.Add(new FriendRow(rows.Count + 1, sender.Id, receiver.Id, status, requested, since));
        }

        foreach (var stack in stacks)
            for (int i = 0; i < stack.Count; i++)
                for (int j = i + 1; j < stack.Count; j++)
                    Add(stack[i], stack[j], "Accepted");

        string[] statuses = { "Accepted", "Pending", "Declined" };
        double[] statusWeights = { 80, 12, 8 };
        while (rows.Count < 320)
        {
            var pair = R.Sample(players, 2);
            Add(pair[0], pair[1], statuses[R.WeightedIndex(statusWeights)]);
        }
        return rows;
    }

    // ------------------------------------------------------------ matches
    /// <summary>Fill two teams of `size` from friend stacks without repeating a player.</summary>
    private static List<Player>[] PickTeams(List<List<Player>> stacks, int size)
    {
        var used = new HashSet<int>();
        var teams = new[] { new List<Player>(), new List<Player>() };
        var weights = stacks.Select(s => s.Sum(p => p.Activity)).ToList();

        foreach (var team in teams)
        {
            int attempts = 0;
            while (team.Count < size)
            {
                attempts++;
                var stack = R.WeightedPick(stacks, weights);
                var free = stack.Where(p => !used.Contains(p.Id)).ToList();
                if (free.Count > 1 && R.Next() < 0.35)            // sometimes friends queue without each other
                    free = R.Sample(free, R.Int(1, free.Count - 1));
                int room = size - team.Count;
                if (free.Count == 0 || (free.Count > room && attempts < 50)) continue;
                foreach (var p in free.Take(room))
                {
                    team.Add(p);
                    used.Add(p.Id);
                }
            }
        }
        return teams;
    }

    /// <summary>Team 1 starts CT, swaps at half, then swaps every 3 rounds in overtime.</summary>
    private static string Team1Side(int round, int reg)
    {
        if (round <= reg) return round <= reg / 2 ? "CT" : "T";
        int otHalf = (round - reg - 1) / 3;
        return otHalf % 2 == 0 ? "CT" : "T";
    }

    private static (int[] Score, List<(int No, int WinTeam, string Reason, int Duration)> Rounds, Dictionary<int, Stat> Stats)
        SimulateMatch(GameMode mode, string mapType, List<Player>[] teams, DateTime when)
    {
        int size = mode.Size;
        var skills = teams.Select(t => t.Select(p => p.SkillAt(when)).ToArray()).ToArray();
        var strength = skills.Select(s => s.Average()).ToArray();
        var stats = teams.SelectMany(t => t).ToDictionary(p => p.Id, _ => new Stat());
        var rounds = new List<(int, int, string, int)>();
        var score = new int[2];
        int rno = 0;

        bool Finished()
        {
            if (rno <= mode.Reg)
            {
                if (Math.Max(score[0], score[1]) >= mode.Win) return true;
                if (rno == mode.Reg) return !mode.Overtime;     // tied at the end of regulation
                return false;
            }
            // overtime: blocks of 6 rounds, first to 4 in the block wins, 3-3 starts another block
            int block = (rno - mode.Reg - 1) / 6;
            int baseScore = mode.Reg / 2 + 3 * block;
            return Math.Max(score[0], score[1]) >= baseScore + 4;
        }

        while (!Finished())
        {
            rno++;
            string side1 = Team1Side(rno, mode.Reg);
            double ctBonus = mapType == "Defusal" ? 0.04 : 0.02;
            double diff = strength[0] - strength[1] + (side1 == "CT" ? ctBonus : -ctBonus);
            double p1 = 1 / (1 + Math.Exp(-5.0 * diff));
            int winner = R.Next() < p1 ? 0 : 1;
            int loser = 1 - winner;
            string winnerSide = winner == 0 ? side1 : (side1 == "CT" ? "T" : "CT");

            // how the round ended
            string reason;
            if (mapType == "Hostage")
                // CTs win by rescuing or eliminating; Ts win by eliminating or running out the clock.
                reason = winnerSide == "T"
                    ? R.WeightedPick(new[] { "Elimination", "TimeExpired" }, new double[] { 85, 15 })
                    : R.WeightedPick(new[] { "Elimination", "HostageRescued" }, new double[] { 70, 30 });
            else
                reason = winnerSide == "T"
                    ? R.WeightedPick(new[] { "Elimination", "BombExploded" }, new double[] { 60, 40 })
                    : R.WeightedPick(new[] { "Elimination", "BombDefused", "TimeExpired" }, new double[] { 70, 22, 8 });

            // number of deaths on each side
            int loserDeaths = reason == "Elimination" ? size : R.Int(0, size - 1);
            int winnerDeaths = Math.Min(size - 1, (int)R.Triangular(0, size, size * 0.3));

            // order the deaths; if the losers were wiped, their last death ends the round
            var events = Enumerable.Repeat(loser, loserDeaths).Concat(Enumerable.Repeat(winner, winnerDeaths)).ToList();
            R.Shuffle(events);
            if (reason == "Elimination" && events[^1] != loser)
            {
                int lastLoser = events.FindLastIndex(e => e == loser);
                (events[lastLoser], events[^1]) = (events[^1], events[lastLoser]);
            }

            var alive = new[] { Enumerable.Range(0, teams[0].Count).ToList(), Enumerable.Range(0, teams[1].Count).ToList() };
            foreach (int dyingTeam in events)
            {
                int killerTeam = 1 - dyingTeam;
                int victim = R.WeightedPick(alive[dyingTeam], alive[dyingTeam].Select(i => 1 / skills[dyingTeam][i]).ToList());
                int killer = R.WeightedPick(alive[killerTeam], alive[killerTeam].Select(i => skills[killerTeam][i] * skills[killerTeam][i]).ToList());
                alive[dyingTeam].Remove(victim);

                var kp = teams[killerTeam][killer];
                var vp = teams[dyingTeam][victim];
                stats[kp.Id].Kills++;
                stats[vp.Id].Deaths++;
                stats[kp.Id].Damage += R.Uniform(80, 100);
                if (R.Next() < kp.Headshot) stats[kp.Id].Headshots++;

                var mates = Enumerable.Range(0, teams[killerTeam].Count).Where(i => i != killer).ToList();
                if (mates.Count > 0 && R.Next() < 0.30)
                {
                    var helper = teams[killerTeam][R.Pick(mates)];
                    stats[helper.Id].Assists++;
                    stats[helper.Id].Damage += R.Uniform(25, 60);
                }
            }

            // chip damage that didn't end in a kill
            for (int t = 0; t < 2; t++)
                for (int i = 0; i < teams[t].Count; i++)
                    stats[teams[t][i].Id].Damage += Math.Clamp(R.Gauss(18 * skills[t][i], 10), 0, 70);

            int duration = reason switch
            {
                "BombExploded" or "BombDefused" => R.Int(70, 155),   // 1:55 round + 40s bomb timer
                "TimeExpired" => 115,
                _ => R.Int(25, 140),
            };
            score[winner]++;
            rounds.Add((rno, winner + 1, reason, duration));
        }

        return (score, rounds, stats);
    }

    // ------------------------------------------------------------ validation
    /// <summary>Mirror of the CHECK / UNIQUE / FK constraints so bad data never reaches SQL Server.</summary>
    private static void Validate(List<PlayerRow> players, List<MatchRow> matches, List<StatRow> stats,
                                 List<RoundRow> rounds, List<RankRow> ranks, List<FriendRow> friends)
    {
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Validation failed: " + message);
        }

        var pids = players.Select(p => p.PlayerID).ToHashSet();
        Check(players.Select(p => p.SteamID).Distinct().Count() == players.Count, "duplicate SteamID");
        Check(players.All(p => p.SteamID.Length == 17 && p.SteamID.All(char.IsDigit)), "bad SteamID");
        Check(players.All(p => p.PremierRank is null or >= 0 and <= 40000), "PremierRank out of range");
        Check(players.All(p => p.CompetitiveRank is null or >= 1 and <= 18), "CompetitiveRank out of range");

        var score = matches.ToDictionary(m => m.MatchID, m => (m.Team1Score, m.Team2Score));
        Check(stats.Select(s => (s.MatchID, s.PlayerID)).Distinct().Count() == stats.Count, "player twice in one match");
        foreach (var s in stats)
        {
            Check(pids.Contains(s.PlayerID) && score.ContainsKey(s.MatchID), "stat row FK");
            Check(s.TeamNumber is 1 or 2 && s.Result is "W" or "L" or "T", "stat row team/result");
            Check(s.Kills >= 0 && s.Deaths >= 0 && s.Assists >= 0 && s.Damage >= 0, "negative stat");
            Check(s.HeadshotPercentage is >= 0 and <= 100, "HS% out of range");
        }
        foreach (var group in stats.GroupBy(s => s.MatchID))
        {
            var (t1, t2) = score[group.Key];
            foreach (var s in group)
            {
                string expect = t1 == t2 ? "T" : (t1 > t2) == (s.TeamNumber == 1) ? "W" : "L";
                Check(s.Result == expect, $"result mismatch in match {group.Key}");
            }
            int Kills(int team) => group.Where(s => s.TeamNumber == team).Sum(s => s.Kills);
            int Deaths(int team) => group.Where(s => s.TeamNumber == team).Sum(s => s.Deaths);
            Check(Kills(1) == Deaths(2) && Kills(2) == Deaths(1), $"kill/death mismatch in match {group.Key}");
        }

        if (rounds.Count > 0)
        {
            Check(rounds.Select(r => (r.MatchID, r.RoundNumber)).Distinct().Count() == rounds.Count, "duplicate round");
            Check(rounds.All(r => r.WinningTeam is 1 or 2 && r.DurationSeconds is >= 1 and <= 175), "round values");
            var wins = rounds.GroupBy(r => r.MatchID)
                .ToDictionary(g => g.Key, g => (g.Count(r => r.WinningTeam == 1), g.Count(r => r.WinningTeam == 2)));
            Check(wins.Count == score.Count, "match without rounds");
            foreach (var (mid, w) in wins) Check(w == score[mid], $"rounds don't add up for match {mid}");
        }

        foreach (var r in ranks)
            Check((r.RankType == "Premier" && r.MapID is null && r.RankValue is >= 0 and <= 40000) ||
                  (r.RankType == "Competitive" && r.MapID is not null && r.RankValue is >= 1 and <= 18), "rank row");

        var seen = new HashSet<(int, int)>();
        foreach (var f in friends)
        {
            Check(f.PlayerID != f.FriendID && pids.Contains(f.PlayerID) && pids.Contains(f.FriendID), "friend FK/self");
            Check(seen.Add((Math.Min(f.PlayerID, f.FriendID), Math.Max(f.PlayerID, f.FriendID))), "duplicate friendship");
            Check((f.Status == "Accepted") == (f.FriendsSince is not null), "FriendsSince vs Status");
            Check(f.FriendsSince is null || f.FriendsSince >= f.RequestedOn, "FriendsSince before RequestedOn");
        }
    }

    // ------------------------------------------------------------ SQL output
    private static string SqlValue(object? v) => v switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        DateTime d => "'" + d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'",
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        int or long or short => Convert.ToString(v, CultureInfo.InvariantCulture)!,
        _ => "N'" + v.ToString()!.Replace("'", "''") + "'",
    };

    private static void WriteTable(string outDir, string fileName, string table, string[] columns, IEnumerable<object?[]> rows)
    {
        var list = rows.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"/* {table}: {list.Count} rows. Generated by tools/DataGenerator (seed {Settings.Seed}). */");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine($"SET IDENTITY_INSERT {table} ON;");
        for (int i = 0; i < list.Count; i += Settings.Batch)
        {
            sb.AppendLine($"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES");
            var chunk = list.Skip(i).Take(Settings.Batch).Select(r => "(" + string.Join(", ", r.Select(SqlValue)) + ")");
            sb.AppendLine(string.Join(",\n", chunk) + ";");
        }
        sb.AppendLine($"SET IDENTITY_INSERT {table} OFF;");
        sb.AppendLine("GO");
        File.WriteAllText(Path.Combine(outDir, fileName), sb.ToString(), new UTF8Encoding(false));
    }
}
