"""
Generates Database/Data.sql: realistic, internally consistent seed data
for the CS Pub Match Stat Tracker.

    python generate_data.py            # writes ../Data.sql

The output is deterministic (fixed random seed), so re-running it gives
the same Data.sql.

How the data is made
- 150 players with a hidden skill value. Rank follows skill.
- Players belong to friend groups ("parties") that queue together.
  Friendships are every pair inside a group plus some random pairs.
- Each match is simulated round by round using the mode's real win
  condition (Competitive/Premier first to 13 with MR12, Premier MR3
  overtime, Wingman first to 9, Casual first to 8). In every round the
  losing side mostly dies, the winning side loses a few players, and
  each death is credited to a killer on the other team, weighted by
  skill. Assists, headshots and damage come from those kills.
  This keeps the stats consistent: a team's kills equal the other
  team's deaths, nobody dies more than once a round, headshots never
  exceed kills, and every Result agrees with the score.
- Player 1 (ManhattanMid) is a hand-made demo account who plays a lot
  and gets better over time, so the monthly trend shows improvement.
"""

import math
import random
from datetime import date, datetime, timedelta
from pathlib import Path

random.seed(560)

OUT = Path(__file__).resolve().parent.parent / "Data.sql"

START = datetime(2025, 1, 1)
END = datetime(2026, 11, 15, 23, 0)
TODAY = date(2026, 11, 18)
NUM_PLAYERS = 150
NUM_MATCHES = 1000
DEMO_MATCHES = 300

# ---------------------------------------------------------------- lookups
MAPS = [
    ("Dust2", "Defusal"), ("Mirage", "Defusal"), ("Inferno", "Defusal"),
    ("Nuke", "Defusal"), ("Ancient", "Defusal"), ("Anubis", "Defusal"),
    ("Train", "Defusal"), ("Overpass", "Defusal"), ("Vertigo", "Defusal"),
    ("Cache", "Defusal"), ("Poseidon", "Defusal"), ("Office", "Hostage"),
    ("Italy", "Hostage"), ("Agency", "Hostage"),
]
MAP_ID = {name: i + 1 for i, (name, _) in enumerate(MAPS)}

# name, players per team, max regulation rounds
MODES = [("Competitive", 5, 24), ("Premier", 5, 24), ("Wingman", 2, 16), ("Casual", 10, 15)]
MODE_ID = {name: i + 1 for i, (name, _, _) in enumerate(MODES)}
MODE_WEIGHTS = {"Competitive": 0.38, "Premier": 0.35, "Wingman": 0.17, "Casual": 0.10}

MAP_POOLS = {
    "Premier": {"Dust2": 5, "Mirage": 6, "Inferno": 5, "Nuke": 4, "Ancient": 4,
                "Anubis": 3, "Train": 3, "Overpass": 2},
    "Competitive": {"Dust2": 5, "Mirage": 6, "Inferno": 5, "Nuke": 3, "Ancient": 3,
                    "Anubis": 3, "Train": 2, "Overpass": 2, "Vertigo": 2, "Cache": 2,
                    "Office": 2, "Italy": 1},
    "Wingman": {"Inferno": 4, "Nuke": 3, "Vertigo": 3, "Overpass": 3, "Poseidon": 3},
    "Casual": {"Dust2": 4, "Mirage": 3, "Inferno": 3, "Office": 3, "Italy": 2,
               "Agency": 2, "Vertigo": 1, "Cache": 1, "Train": 1, "Overpass": 1},
}

# ---------------------------------------------------------------- players
ADJ = ["Silent", "Frosty", "Lucky", "Rapid", "Toxic", "Calm", "Rusty", "Neon", "Shadow",
       "Turbo", "Lazy", "Crispy", "Feral", "Golden", "Salty", "Sneaky", "Clutch", "Spicy",
       "Quiet", "Hyper", "Mellow", "Wild", "Brave", "Cosmic", "Pixel", "Grumpy", "Swift"]
NOUN = ["Falcon", "Badger", "Ninja", "Pickle", "Viper", "Otter", "Ghost", "Panda", "Raven",
        "Mango", "Wombat", "Comet", "Bison", "Lynx", "Taco", "Moose", "Gecko", "Kraken",
        "Walrus", "Coyote", "Waffle", "Hornet", "Yeti", "Noodle"]
DOMAINS = ["example.com", "example.net", "example.org", "mail.example.com"]


def make_username(taken):
    while True:
        a, n = random.choice(ADJ), random.choice(NOUN)
        style = random.random()
        if style < 0.35:
            name = a + n
        elif style < 0.6:
            name = f"{a.lower()}_{n.lower()}"
        elif style < 0.85:
            name = f"{a}{n}{random.randint(1, 99)}"
        else:
            name = f"x{a}{n}x"
        if name.lower() not in taken:
            taken.add(name.lower())
            return name


def rank_for(skill_pct):
    if skill_pct < 0.15:
        return "Silver"
    if skill_pct < 0.40:
        return "Gold Nova"
    if skill_pct < 0.65:
        return "Master Guardian"
    if skill_pct < 0.80:
        return "Legendary Eagle"
    if skill_pct < 0.95:
        return "Supreme"
    return "Global Elite"


def rand_date(lo: date, hi: date) -> date:
    return lo + timedelta(days=random.randint(0, (hi - lo).days))


players = []  # dicts: id, username, email, join, rank, skill, hs
taken = {"manhattanmid"}
players.append({"id": 1, "username": "ManhattanMid", "email": "manhattanmid@example.com",
                "join": date(2024, 9, 1), "skill": 0.0, "hs": 0.42})
for pid in range(2, NUM_PLAYERS + 1):
    u = make_username(taken)
    players.append({
        "id": pid,
        "username": u,
        "email": f"{u.lower()}@{random.choice(DOMAINS)}",
        "join": rand_date(date(2024, 6, 1), date(2026, 9, 30)),
        "skill": random.gauss(0, 1),
        "hs": 0.0,
    })

order = sorted(players[1:], key=lambda p: p["skill"])
for i, p in enumerate(order):
    p["rank"] = "Unranked" if random.random() < 0.05 else rank_for(i / len(order))
    p["hs"] = min(0.65, max(0.25, 0.42 + 0.06 * p["skill"] + random.gauss(0, 0.04)))
players[0]["rank"] = "Master Guardian"

P = {p["id"]: p for p in players}


def skill_at(pid, when: datetime) -> float:
    """Player 1 improves from below average to well above it over the period."""
    if pid != 1:
        return P[pid]["skill"]
    frac = (when - START).total_seconds() / (END - START).total_seconds()
    return -0.4 + 1.4 * frac


# ---------------------------------------------------------------- friend groups
groups = []
pool_ids = [p["id"] for p in players[1:]]
random.shuffle(pool_ids)
idx = 0
while idx < len(pool_ids):
    size = random.randint(3, 6)
    groups.append(pool_ids[idx: idx + size])
    idx += size
# some players also belong to a second group
for pid in random.sample(range(2, NUM_PLAYERS + 1), 25):
    g = random.choice(groups)
    if pid not in g:
        g.append(pid)
# the demo player's two groups
demo_groups = [groups[0], groups[1]]
for g in demo_groups:
    g.append(1)

pairs = {}


def add_pair(a, b):
    if a == b:
        return
    lo, hi = min(a, b), max(a, b)
    if (lo, hi) in pairs:
        return
    start = max(P[lo]["join"], P[hi]["join"])
    if start > date(2026, 11, 1):
        return
    pairs[(lo, hi)] = rand_date(start, min(TODAY, start + timedelta(days=200)))


for g in groups:
    for i in range(len(g)):
        for j in range(i + 1, len(g)):
            add_pair(g[i], g[j])
while len(pairs) < 320:
    add_pair(random.randint(1, NUM_PLAYERS), random.randint(1, NUM_PLAYERS))
for pid in random.sample(range(2, NUM_PLAYERS + 1), 6):
    add_pair(1, pid)

# ---------------------------------------------------------------- matches
def rand_match_time() -> datetime:
    day = START.date() + timedelta(days=random.randint(0, (END.date() - START.date()).days))
    hour = random.choices(range(24), weights=[3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2,
                                               2, 2, 3, 3, 4, 5, 7, 8, 8, 8, 6, 4])[0]
    return datetime(day.year, day.month, day.day, hour, random.randint(0, 59), random.choice([0, 15, 30, 45]))


def play_rounds(mode, d):
    """Simulates round winners. d = team 1 strength minus team 2 strength.
    Re-plays the rare Premier match whose overtimes pass 24 rounds a side."""
    while True:
        t1, t2, winners = _play_rounds(mode, d)
        if max(t1, t2) <= 24:
            return t1, t2, winners


def _play_rounds(mode, d):
    # a per-match swing (form, economy, who tilts) spreads the scores out
    p1 = 0.5 + 0.22 * math.tanh(d + random.gauss(0, 0.9))
    t1 = t2 = 0
    winners = []

    def rnd():
        nonlocal t1, t2
        if random.random() < p1:
            t1 += 1
            winners.append(1)
        else:
            t2 += 1
            winners.append(2)

    if mode in ("Competitive", "Premier"):
        while t1 < 13 and t2 < 13 and not (t1 == 12 and t2 == 12):
            rnd()
        if mode == "Premier" and t1 == 12 and t2 == 12:
            target = 16
            while True:  # MR3 overtime: first to 4 of 6
                while t1 < target and t2 < target and not (t1 == target - 1 and t2 == target - 1):
                    rnd()
                if t1 == target or t2 == target:
                    break
                target += 3
    elif mode == "Wingman":
        while t1 < 9 and t2 < 9 and not (t1 == 8 and t2 == 8):
            rnd()
    else:  # Casual
        while t1 < 8 and t2 < 8:
            rnd()
    return t1, t2, winners


def weighted_pick(items, weights, k):
    items, weights = list(items), list(weights)
    chosen = []
    for _ in range(min(k, len(items))):
        i = random.choices(range(len(items)), weights=weights)[0]
        chosen.append(items.pop(i))
        weights.pop(i)
    return chosen


def simulate_match(team1, team2, mode, when):
    sk = {pid: skill_at(pid, when) for pid in team1 + team2}
    d = (sum(sk[p] for p in team1) / len(team1)) - (sum(sk[p] for p in team2) / len(team2))
    s1, s2, winners = play_rounds(mode, d)
    stats = {pid: {"K": 0, "D": 0, "A": 0, "HS": 0, "DMG": 0} for pid in team1 + team2}
    teams = {1: team1, 2: team2}

    for w in winners:
        lose = 2 if w == 1 else 1
        win_team, lose_team = teams[w], teams[lose]
        n_l, n_w = len(lose_team), len(win_team)
        lose_deaths = random.choices([n_l, max(n_l - 1, 0), max(n_l - 2, 0)], weights=[70, 22, 8])[0]
        if n_l <= 2:
            lose_deaths = random.choices([n_l, n_l - 1], weights=[80, 20])[0]
        win_deaths = min(n_w - 1, sum(random.random() < 0.38 for _ in range(n_w)))
        for victims_team, killers_team, n in ((lose_team, win_team, lose_deaths),
                                              (win_team, lose_team, win_deaths)):
            victims = weighted_pick(victims_team, [math.exp(-0.35 * sk[v]) for v in victims_team], n)
            for v in victims:
                stats[v]["D"] += 1
                killer = random.choices(killers_team, weights=[math.exp(0.55 * sk[k]) for k in killers_team])[0]
                stats[killer]["K"] += 1
                if random.random() < P[killer]["hs"] + (0.03 * sk[killer] if killer == 1 else 0):
                    stats[killer]["HS"] += 1
                mates = [m for m in killers_team if m != killer]
                if mates and random.random() < 0.28:
                    helper = random.choice(mates)
                    chip = random.randint(41, 90)
                    stats[helper]["A"] += 1
                    stats[helper]["DMG"] += chip
                    stats[killer]["DMG"] += 100 - chip
                else:
                    stats[killer]["DMG"] += 100
        # damage on players who survived the round
        for pid in win_team + lose_team:
            if random.random() < 0.35:
                stats[pid]["DMG"] += random.randint(5, 60)
    return s1, s2, stats


matches = []  # dicts
pms_rows = []  # tuples
demo_slots = set(random.sample(range(NUM_MATCHES), DEMO_MATCHES))
times = sorted(rand_match_time() for _ in range(NUM_MATCHES))

for mi, when in enumerate(times):
    is_demo = mi in demo_slots
    mode = random.choices(list(MODE_WEIGHTS), weights=list(MODE_WEIGHTS.values()))[0]
    if is_demo and mode == "Casual" and random.random() < 0.6:
        mode = "Competitive"
    _, ppt, _ = MODES[MODE_ID[mode] - 1]
    pool = MAP_POOLS[mode]
    map_name = random.choices(list(pool), weights=list(pool.values()))[0]

    eligible = [p["id"] for p in players if p["join"] <= when.date()]
    if is_demo and 1 not in eligible:
        is_demo = False
    per_team = ppt if mode != "Casual" else random.randint(7, 10)

    # team 1 starts with a party from a friend group
    if is_demo:
        g = random.choice(demo_groups)
        party_pool = [m for m in g if m != 1 and m in eligible]
        party = [1] + random.sample(party_pool, min(len(party_pool), random.randint(0, per_team - 1)))
    else:
        g = random.choice(groups)
        party_pool = [m for m in g if m in eligible and m != 1]
        party = random.sample(party_pool, min(len(party_pool), random.randint(1, per_team)))
    used = set(party)
    anchor = sum(skill_at(p, when) for p in party) / max(len(party), 1)

    def fill(team, n):
        cands = [p for p in eligible if p not in used and p != 1]
        weights = [math.exp(-abs(P[c]["skill"] - anchor) * (1.2 if mode != "Casual" else 0.3)) for c in cands]
        for pick in weighted_pick(cands, weights, n - len(team)):
            team.append(pick)
            used.add(pick)
        return team

    team1 = fill(list(party), per_team)
    team2 = fill([], per_team)
    if random.random() < 0.5:
        team1, team2 = team2, team1

    s1, s2, stats = simulate_match(team1, team2, mode, when)
    match_id = mi + 1
    matches.append({"id": match_id, "map": MAP_ID[map_name], "mode": MODE_ID[mode],
                    "date": when, "s1": s1, "s2": s2, "removed": 0})
    for team_no, team in ((1, team1), (2, team2)):
        for pid in team:
            st = stats[pid]
            if s1 == s2:
                result = "Tie"
            elif (team_no == 1) == (s1 > s2):
                result = "Win"
            else:
                result = "Loss"
            pms_rows.append((match_id, pid, st["K"], st["D"], st["A"], st["HS"], st["DMG"], result))

# a handful of matches were logged by mistake and soft-deleted
for m in random.sample([m for m in matches if m["id"] - 1 not in demo_slots], 6):
    m["removed"] = 1

# ---------------------------------------------------------------- checks
seen = set()
by_match = {}
for r in pms_rows:
    assert (r[0], r[1]) not in seen
    seen.add((r[0], r[1]))
    by_match.setdefault(r[0], []).append(r)
for m in matches:
    rows = by_match[m["id"]]
    rounds = m["s1"] + m["s2"]
    assert rounds > 0
    assert sum(r[2] for r in rows) == sum(r[3] for r in rows), "kills must equal deaths"
    for r in rows:
        assert 0 <= r[5] <= r[2] and r[3] <= rounds and r[6] >= 0
        assert P[r[1]]["join"] <= m["date"].date()
    results = {r[7] for r in rows}
    assert results == ({"Tie"} if m["s1"] == m["s2"] else {"Win", "Loss"})
    max_rounds = MODES[m["mode"] - 1][2]
    assert m["s1"] <= max_rounds and m["s2"] <= max_rounds

# ---------------------------------------------------------------- SQL output
def q(s: str) -> str:
    return "'" + s.replace("'", "''") + "'"


def insert_blocks(table, cols, rows, chunk=900):
    out = []
    for i in range(0, len(rows), chunk):
        part = rows[i: i + chunk]
        out.append(f"INSERT dbo.{table} ({', '.join(cols)}) VALUES\n" +
                   ",\n".join("(" + ", ".join(v) + ")" for v in part) + ";\n")
    return "".join(out)


def ident(table, body):
    return f"SET IDENTITY_INSERT dbo.{table} ON;\n{body}SET IDENTITY_INSERT dbo.{table} OFF;\n\n"


sql = []
sql.append(f"""/* =====================================================================
   CS Pub Match Stat Tracker - Data.sql
   Seed data generated by DataGenerator/generate_data.py (fixed seed, so
   re-running the generator gives this same file). Do not edit by hand;
   change the generator and re-run it instead.

   Run order:  Tables.sql -> Procedures.sql -> Data.sql
   Rows:  Maps {len(MAPS)}, GameModes {len(MODES)}, Players {len(players)},
          Friendships {len(pairs)}, Matches {len(matches)},
          PlayerMatchStats {len(pms_rows)}
   Player 1 (ManhattanMid) is the demo account: {sum(1 for r in pms_rows if r[1] == 1)} matches.
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- Clear existing rows so the script can be re-run.
DELETE dbo.PlayerMatchStats;
DELETE dbo.Friendships;
DELETE dbo.Matches;
DELETE dbo.Players;
DELETE dbo.GameModes;
DELETE dbo.Maps;

""")

sql.append(ident("Maps", insert_blocks("Maps", ["MapID", "MapName", "MapType"],
                                       [(str(i + 1), q(n), q(t)) for i, (n, t) in enumerate(MAPS)])))
sql.append(ident("GameModes", insert_blocks("GameModes", ["GameModeID", "ModeName", "PlayersPerTeam", "MaxRounds"],
                                            [(str(i + 1), q(n), str(ppt), str(mr)) for i, (n, ppt, mr) in enumerate(MODES)])))
sql.append(ident("Players", insert_blocks("Players", ["PlayerID", "Username", "Email", "JoinDate", "[Rank]"],
                                          [(str(p["id"]), q(p["username"]), q(p["email"]),
                                            q(p["join"].isoformat()), q(p["rank"])) for p in players])))
sql.append(ident("Friendships", insert_blocks("Friendships", ["FriendshipID", "PlayerID", "FriendID", "FriendsSince"],
                                              [(str(i + 1), str(a), str(b), q(d.isoformat()))
                                               for i, ((a, b), d) in enumerate(sorted(pairs.items()))])))
sql.append(ident("Matches", insert_blocks("Matches", ["MatchID", "MapID", "GameModeID", "MatchDate",
                                                      "Team1Score", "Team2Score", "IsRemoved"],
                                          [(str(m["id"]), str(m["map"]), str(m["mode"]),
                                            q(m["date"].strftime("%Y-%m-%dT%H:%M:%S")),
                                            str(m["s1"]), str(m["s2"]), str(m["removed"])) for m in matches])))
sql.append(ident("PlayerMatchStats", insert_blocks(
    "PlayerMatchStats",
    ["PlayerMatchStatID", "MatchID", "PlayerID", "Kills", "Deaths", "Assists", "HeadshotCount", "Damage", "Result"],
    [(str(i + 1), str(r[0]), str(r[1]), str(r[2]), str(r[3]), str(r[4]), str(r[5]), str(r[6]), q(r[7]))
     for i, r in enumerate(pms_rows)])))
sql.append("COMMIT TRANSACTION;\nGO\n")

OUT.write_text("".join(sql), encoding="utf-8")

# summary for the person running it
demo = [r for r in pms_rows if r[1] == 1]
k, d_, dmg = sum(r[2] for r in demo), sum(r[3] for r in demo), sum(r[6] for r in demo)
rounds = {m["id"]: m["s1"] + m["s2"] for m in matches}
print(f"Wrote {OUT}")
print(f"players={len(players)} friendships={len(pairs)} matches={len(matches)} stats rows={len(pms_rows)}")
print(f"demo: games={len(demo)} K/D={k / d_:.2f} ADR={dmg / sum(rounds[r[0]] for r in demo):.1f} "
      f"friends={sum(1 for a, b in pairs if 1 in (a, b))}")
