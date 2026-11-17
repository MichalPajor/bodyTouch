// DevSeed: creates a SQLite database with a known round history for checking statistics, rankings and the heat map
// on the emulator (stage 7). The expected results are listed in the stage 7 report.
//
// Usage: dotnet run --project tools/DevSeed -- <output.db3> <body_zones.json>
// Then: tools/scripts/push_seed_db.sh <output.db3>   (Debug build on the emulator)
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Scoring;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Data;
using BodyTouchGame.Geometry;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: DevSeed <output.db3> <body_zones.json>");
    return 1;
}

var output = Path.GetFullPath(args[0]);
if (File.Exists(output)) File.Delete(output);
ZoneCatalog catalog;
await using (var zones = File.OpenRead(args[1])) catalog = await ZoneCatalog.LoadAsync(zones);

await using var db = new BodyTouchDatabase(output);
var players = new PlayerRepository(db);
var presets = new PresetRepository(db);
var sessions = new SessionRepository(db);

var ola = await players.AddAsync(new Player(0, "Ola", "#FF3D9A", null, DateTimeOffset.UtcNow));
var kuba = await players.AddAsync(new Player(0, "Kuba", "#22D3EE", "🦊", DateTimeOffset.UtcNow));
var marta = await players.AddAsync(new Player(0, "Marta", "#8B5CF6", null, DateTimeOffset.UtcNow));
var piotr = await players.AddAsync(new Player(0, "Piotr", "#FF9A3C", null, DateTimeOffset.UtcNow));

var all = await presets.GetAllAsync();
var medium = all.Single(p => p.Name == BuiltInPresets.MediumKey);
var easy = all.Single(p => p.Name == BuiltInPresets.EasyKey);
var start = new DateTimeOffset(2026, 11, 10, 19, 0, 0, TimeSpan.Zero);
List<long> ids = [ola.Id, kuba.Id, marta.Id, piotr.Id];

// One session per day; Ola's average distance falls from session to session (chart).
async Task Session(GamePreset preset, int day, Action<Action<Player, string, float, string>> rounds)
{
    var started = start.AddDays(day);
    var id = await sessions.CreateAsync(new BodyTouchGame.Core.Persistence.SessionRecord(0, preset.Id, preset.Config, RoleOrder.AllPairs, ids, started, null, []));
    var number = 0;
    var list = new List<RoundResult>();
    rounds((guesser, target, distance, guessed) =>
    {
        number++;
        var score = ScoreCalculator.Score(distance, preset.Config, 4);
        var pinTriangle = FirstTriangle(guessed);
        list.Add(new RoundResult(number, guesser.Id, guesser.Id == ola.Id ? kuba.Id : ola.Id, new ZoneTarget(target),
            new SurfacePoint(pinTriangle, 0.33f, 0.33f), distance, guessed, score.BasePoints, score.SpeedBonus, 4,
            started.AddMinutes(number), []));
    });
    foreach (var round in list) await sessions.AddRoundAsync(id, round);
    await sessions.EndAsync(id, started.AddMinutes(number + 1));
}

void Repeat(Action<Player, string, float, string> add, int times, Player p, string target, float distance, string guessed)
{
    for (var i = 0; i < times; i++) add(p, target, distance, guessed);
}

// Medium preset (r = 5 cm, z = 40 cm).
await Session(medium, 0, add =>
{
    Repeat(add, 4, ola, "arm-l.hand", 45, "arm-l.elbow");     // 0 pts, wrong zone 4× → arrow hand → elbow
    Repeat(add, 1, ola, "arm-l.hand", 45, "arm-l.forearm");
    Repeat(add, 2, kuba, "head.face", 15, "head.face");
    Repeat(add, 2, marta, "neck.front", 10, "neck.front");
    Repeat(add, 1, piotr, "chest.r", 1, "chest.r");
});
await Session(medium, 1, add =>
{
    Repeat(add, 5, ola, "chest.l", 22.5f, "chest.l");         // 50 pts → yellow
    Repeat(add, 2, ola, "leg-l.foot", 30, "leg-l.shin");      // ~29 pts; wrong zone only 2× → no arrow
    Repeat(add, 1, ola, "leg-l.foot", 30, "leg-l.foot");
    Repeat(add, 2, kuba, "head.face", 15, "head.face");
    Repeat(add, 2, marta, "neck.front", 10, "neck.front");
    Repeat(add, 1, piotr, "chest.r", 1, "chest.r");
});
await Session(medium, 2, add =>
{
    Repeat(add, 3, ola, "belly.center", 12, "belly.flank-l"); // 80 pts; exactly 3× → arrow belly.center → flank-l
    Repeat(add, 2, kuba, "head.face", 15, "head.face");
    Repeat(add, 1, marta, "neck.front", 10, "neck.front");
    Repeat(add, 1, piotr, "chest.r", 1, "chest.r");
});
await Session(medium, 3, add =>
{
    Repeat(add, 5, ola, "leg-r.knee", 1, "leg-r.knee");       // 100 pts → green
    Repeat(add, 1, piotr, "chest.r", 1, "chest.r");
});
// Easy preset (r = 10 cm): Ola's head – only in the Easy heat map.
await Session(easy, 4, add => Repeat(add, 5, ola, "head", 5, "head"));

Console.WriteLine($"Seeded {output}");
return 0;

int FirstTriangle(string zoneId)
{
    var zone = catalog.Get(zoneId);
    var labels = catalog.TriangleLabels(zone.Level);
    var index = catalog.IndexOf(zoneId);
    for (var t = 0; t < labels.Length; t++)
    {
        if (labels[t] == index) return t;
    }

    throw new InvalidOperationException($"Zone {zoneId} has no triangles.");
}
