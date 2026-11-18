using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Data.Tests;

/// <summary>Each test gets its own temporary database file.</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"btg-test-{Guid.NewGuid():N}.db3");
    public BodyTouchDatabase Database { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Database = new BodyTouchDatabase(Path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Database.DisposeAsync();
        File.Delete(Path);
    }

    public async Task<BodyTouchDatabase> ReopenAsync()
    {
        await Database.DisposeAsync();
        Database = new BodyTouchDatabase(Path);
        return Database;
    }
}

public class SchemaTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db = new();
    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task NewDatabaseIsMigratedToTheCurrentVersionAndSeeded()
    {
        Assert.Equal(BodyTouchDatabase.SchemaVersion, await _db.Database.GetSchemaVersionAsync());

        var presets = await new PresetRepository(_db.Database).GetAllAsync();

        Assert.Equal(["Easy", "Medium", "Hard", "Hardcore"], presets.Select(p => p.Name));
        Assert.All(presets, p => Assert.True(p.IsBuiltIn));
        Assert.Equal(BuiltInPresets.Medium, presets[1].Config);
    }

    [Fact]
    public async Task ReopeningKeepsDataAndDoesNotDuplicateBuiltInPresets()
    {
        await new PlayerRepository(_db.Database).AddAsync(Player("Ola"));

        var reopened = await _db.ReopenAsync();

        Assert.Single(await new PlayerRepository(reopened).GetAllAsync());
        Assert.Equal(4, (await new PresetRepository(reopened).GetAllAsync()).Count);
        Assert.Equal(BodyTouchDatabase.SchemaVersion, await reopened.GetSchemaVersionAsync());
    }

    internal static Player Player(string name) => new(0, name, "#FF3D9A", null, new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
}

public class PlayerAndExclusionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db = new();
    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task PlayersCanBeAddedUpdatedAndDeleted()
    {
        var players = new PlayerRepository(_db.Database);

        var ola = await players.AddAsync(SchemaTests.Player(" Ola "));
        var kuba = await players.AddAsync(SchemaTests.Player("Kuba") with { AvatarEmoji = "🦊" });
        await players.UpdateAsync(ola with { AvatarColor = "#22D3EE" });

        Assert.True(ola.Id > 0);
        Assert.Equal("Ola", ola.Name);                                    // trimmed
        Assert.Equal(["Kuba", "Ola"], (await players.GetAllAsync()).Select(p => p.Name));
        Assert.Equal("#22D3EE", (await players.GetAsync(ola.Id))!.AvatarColor);
        Assert.Equal("🦊", (await players.GetAsync(kuba.Id))!.AvatarEmoji);
        Assert.Equal(ola.CreatedAt, (await players.GetAsync(ola.Id))!.CreatedAt);

        await players.DeleteAsync(kuba.Id);
        Assert.Null(await players.GetAsync(kuba.Id));
    }

    [Fact]
    public async Task PlayerNamesAreUniqueIgnoringCase()
    {
        var players = new PlayerRepository(_db.Database);
        var ola = await players.AddAsync(SchemaTests.Player("Łucja"));
        var kuba = await players.AddAsync(SchemaTests.Player("Kuba"));

        await Assert.ThrowsAsync<DuplicateNameException>(() => players.AddAsync(SchemaTests.Player("ŁUCJA")));
        await Assert.ThrowsAsync<DuplicateNameException>(() => players.UpdateAsync(kuba with { Name = "łucja" }));
        await players.UpdateAsync(ola with { Name = "łucja" });             // renaming yourself is fine
        Assert.True(await players.NameExistsAsync("KUBA"));
        await Assert.ThrowsAsync<ArgumentException>(() => players.AddAsync(SchemaTests.Player("  ")));
    }

    [Fact]
    public async Task ExclusionProfilesAreStoredPerPlayerAndRemovedWithThePlayer()
    {
        var players = new PlayerRepository(_db.Database);
        var exclusions = new ExclusionRepository(_db.Database);
        var ola = await players.AddAsync(SchemaTests.Player("Ola"));

        Assert.Empty((await exclusions.GetAsync(ola.Id)).ZoneIds);
        await exclusions.SaveAsync(new ExclusionProfile(ola.Id, ["head.face", "arm-l", "head.face"]));
        Assert.Equal(["arm-l", "head.face"], (await exclusions.GetAsync(ola.Id)).ZoneIds);
        await exclusions.SaveAsync(new ExclusionProfile(ola.Id, ["leg-r.foot"]));
        Assert.Equal(["leg-r.foot"], (await exclusions.GetAsync(ola.Id)).ZoneIds);

        await players.DeleteAsync(ola.Id);
        Assert.Empty((await exclusions.GetAsync(ola.Id)).ZoneIds);
    }
}

public class PresetPointAndOverrideTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db = new();
    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task CustomPresetsAreSavedAfterBuiltInOnes()
    {
        var presets = new PresetRepository(_db.Database);
        var party = BuiltInPresets.Easy with { TouchMethod = TouchMethod.TwoFingers, TouchSeconds = 12 };

        var saved = await presets.SaveCustomAsync(new GamePreset(0, "Impreza 3 s", false, party));
        await presets.SaveCustomAsync(new GamePreset(0, "Antek", false, BuiltInPresets.Hard));

        var all = await presets.GetAllAsync();
        Assert.Equal(["Easy", "Medium", "Hard", "Hardcore", "Antek", "Impreza 3 s"], all.Select(p => p.Name));
        Assert.Equal(party, (await presets.GetAsync(saved.Id))!.Config);

        await presets.SaveCustomAsync(saved with { Config = party with { TouchSeconds = 20 } });
        Assert.Equal(20, (await presets.GetAsync(saved.Id))!.Config.TouchSeconds);

        await presets.DeleteCustomAsync(saved.Id);
        Assert.Null(await presets.GetAsync(saved.Id));
    }

    [Fact]
    public async Task BuiltInPresetsCannotBeChangedOrDeleted()
    {
        var presets = new PresetRepository(_db.Database);
        var easy = (await presets.GetAllAsync())[0];

        await Assert.ThrowsAsync<InvalidOperationException>(() => presets.SaveCustomAsync(easy));
        await Assert.ThrowsAsync<InvalidOperationException>(() => presets.SaveCustomAsync(easy with { IsBuiltIn = false }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => presets.DeleteCustomAsync(easy.Id));
        await presets.SaveCustomAsync(new GamePreset(0, "Moja", false, BuiltInPresets.Easy));
        await Assert.ThrowsAsync<DuplicateNameException>(() => presets.SaveCustomAsync(new GamePreset(0, "MOJA", false, BuiltInPresets.Easy)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            presets.SaveCustomAsync(new GamePreset(0, "Zła", false, BuiltInPresets.Easy with { TouchSeconds = 99 })));
    }

    [Fact]
    public async Task CustomPointsRoundTrip()
    {
        var points = new CustomPointRepository(_db.Database);
        var point = new CustomPoint(0, "Pieprzyk", new SurfacePoint(1234, 0.25f, 0.5f), 3.5f, [ZoneLevel.Hard, ZoneLevel.Easy], [5, 6, 7]);

        var saved = await points.SaveAsync(point);
        await points.SaveAsync(saved with { Name = "Pieprzyk 2", RadiusCm = 4 });

        var loaded = Assert.Single(await points.GetAllAsync());
        Assert.Equal("Pieprzyk 2", loaded.Name);
        Assert.Equal(new SurfacePoint(1234, 0.25f, 0.5f), loaded.Point);
        Assert.Equal(4, loaded.RadiusCm);
        Assert.Equal([ZoneLevel.Easy, ZoneLevel.Hard], loaded.Levels);
        Assert.Equal([5, 6, 7], loaded.VertexIds);
        await Assert.ThrowsAsync<ArgumentException>(() => points.SaveAsync(point with { RadiusCm = 20 }));

        await points.DeleteAsync(saved.Id);
        Assert.Empty(await points.GetAllAsync());
    }

    [Fact]
    public async Task LevelOverridesAreUpsertedAndCleared()
    {
        var overrides = new LevelOverrideRepository(_db.Database);

        await overrides.SetAsync(new LevelOverride("leg-l.knee", ZoneLevel.Easy, true));
        await overrides.SetAsync(new LevelOverride("head", ZoneLevel.Easy, false));
        await overrides.SetAsync(new LevelOverride("leg-l.knee", ZoneLevel.Easy, false));   // same zone + level: updated

        Assert.Equal([new LevelOverride("head", ZoneLevel.Easy, false), new LevelOverride("leg-l.knee", ZoneLevel.Easy, false)],
            await overrides.GetAllAsync());

        await overrides.ClearAsync();
        Assert.Empty(await overrides.GetAllAsync());
    }
}

public class SessionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db = new();
    public Task InitializeAsync() => _db.InitializeAsync();
    public Task DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task PlayedSessionIsSavedAndLoadedCompletely()
    {
        var players = new PlayerRepository(_db.Database);
        var sessions = new SessionRepository(_db.Database);
        var ola = await players.AddAsync(SchemaTests.Player("Ola"));
        var kuba = await players.AddAsync(SchemaTests.Player("Kuba"));
        var preset = (await new PresetRepository(_db.Database).GetAllAsync())[1];
        var config = preset.Config with { RoundsPerPlayer = 2, SpeedBonus = true };

        // Play a full session with the Core engine (synthetic body, fixed distances and clock).
        var catalog = MiniCatalog();
        var pools = new[] { ola, kuba }.ToDictionary(p => p.Id, p => new TargetPoolBuilder(catalog).Build(config.ZoneSet, []));
        var clock = new StepClock();
        var game = new GameSession(new SessionSetup([ola, kuba], config, RoleOrder.AllPairs, preset.Id, pools),
            new FixedDistances(), catalog, new SystemRandomSource(new Random(1)), clock);
        game.Start();
        var sessionId = await sessions.CreateAsync(new SessionRecord(0, preset.Id, config, RoleOrder.AllPairs, [ola.Id, kuba.Id], game.StartedAt!.Value, null, []));
        while (game.State != GameState.SessionSummary)
        {
            game.Continue();
            game.StartTouch();
            game.TouchFinished();
            game.ReadyToGuess();
            clock.Advance(7);
            RoundResult result;
            if (game.RoundIndex == 3) result = game.GuessTimeExpired();   // last round: time out without a pin
            else
            {
                game.PlacePin(new SurfacePoint(game.RoundIndex, 0.2f, 0.3f));
                result = game.Submit();
            }

            await sessions.AddRoundAsync(sessionId, result);
            game.NextRound();
        }

        await sessions.EndAsync(sessionId, game.EndedAt!.Value);

        var loaded = await sessions.GetAsync(sessionId);
        Assert.NotNull(loaded);
        Assert.Equal(preset.Id, loaded.PresetId);
        Assert.Equal(config, loaded.Config);
        Assert.Equal(RoleOrder.AllPairs, loaded.RoleOrder);
        Assert.Equal([ola.Id, kuba.Id], loaded.PlayerIds);
        Assert.Equal(game.StartedAt, loaded.StartedAt);
        Assert.Equal(game.EndedAt, loaded.EndedAt);
        Assert.Equal(game.Results.Count, loaded.Rounds.Count);
        for (var i = 0; i < game.Results.Count; i++)
            Assert.Equal(game.Results[i] with { Path = [] }, loaded.Rounds[i] with { Path = [] });
        Assert.Null(loaded.Rounds[3].Pin);
        Assert.Null(loaded.Rounds[3].DistanceCm);

        // The same summary can be computed from the stored rounds.
        Assert.Equal(game.Summary.Standings, SessionStatistics.Compute([ola, kuba], loaded.Rounds).Standings);
        Assert.Equal(sessionId, (await sessions.GetLatestEndedAsync())!.Id);
    }

    [Fact]
    public async Task AllTargetKindsAndNullableValuesRoundTrip()
    {
        var sessions = new SessionRepository(_db.Database);
        var at = new DateTimeOffset(2026, 11, 16, 20, 15, 30, TimeSpan.Zero);
        RoundResult[] rounds =
        [
            new(1, 1, 2, new ZoneTarget("leg-l.knee"), new SurfacePoint(10, 0.1f, 0.2f), 4.5f, "leg-l.thigh", 98, 20, 3.2, at, []),
            new(2, 2, 1, new CustomPointTarget(7, "Pieprzyk", new SurfacePoint(20, 0.3f, 0.3f), 2.5f), new SurfacePoint(21, 0.5f, 0.1f), 0, "head.face", 100, 0, 6, at.AddMinutes(1), []),
            new(3, 1, 2, new ManualPointTarget(new SurfacePoint(30, 0.4f, 0.4f)), null, null, null, 0, 0, 30, at.AddMinutes(2), []),
            new(4, 2, 1, new ZoneTarget("head"), new SurfacePoint(40, 0, 0), float.PositiveInfinity, "head", 0, 0, 9, at.AddMinutes(3), []),
        ];

        var id = await sessions.CreateAsync(new SessionRecord(0, null, BuiltInPresets.Easy, RoleOrder.Circle, [1, 2], at, at.AddMinutes(5), rounds));

        var loaded = (await sessions.GetAsync(id))!;
        Assert.Equal(rounds.Length, loaded.Rounds.Count);
        for (var i = 0; i < rounds.Length; i++) Assert.Equal(rounds[i] with { Path = [] }, loaded.Rounds[i] with { Path = [] });
        Assert.Null(loaded.PresetId);
        Assert.Equal(RoleOrder.Circle, loaded.RoleOrder);
    }

    [Fact]
    public async Task LatestEndedSessionAndAllRounds()
    {
        var sessions = new SessionRepository(_db.Database);
        var t0 = new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);
        RoundResult Round(int n, DateTimeOffset at) => new(n, 1, 2, new ZoneTarget("head"), new SurfacePoint(0, 0, 0), 1, "head", 100, 0, 2, at, []);

        var first = await sessions.CreateAsync(new SessionRecord(0, 5, BuiltInPresets.Easy, RoleOrder.AllPairs, [1, 2], t0, t0.AddHours(1), [Round(1, t0)]));
        var second = await sessions.CreateAsync(new SessionRecord(0, 6, BuiltInPresets.Medium, RoleOrder.AllPairs, [1, 2], t0.AddDays(1), t0.AddDays(1).AddHours(1), [Round(1, t0.AddDays(1))]));
        await sessions.CreateAsync(new SessionRecord(0, 6, BuiltInPresets.Medium, RoleOrder.AllPairs, [1, 2], t0.AddDays(2), null, []));   // unfinished

        Assert.Equal(second, (await sessions.GetLatestEndedAsync())!.Id);
        var all = await sessions.GetAllRoundsAsync();
        Assert.Equal([(first, (long?)5), (second, (long?)6)], all.Select(r => (r.SessionId, r.PresetId)));
        Assert.Null(await sessions.GetAsync(999));
    }

    private static ZoneCatalog MiniCatalog()
    {
        ZoneDefinition[] zones =
        [
            new("head", ZoneLevel.Easy, null, "Głowa", ZoneSide.Center),
            new("leg-l", ZoneLevel.Easy, null, "Lewa noga", ZoneSide.Left),
            new("head.face", ZoneLevel.Medium, "head", "Twarz", ZoneSide.Center),
            new("leg-l.knee", ZoneLevel.Medium, "leg-l", "Lewe kolano", ZoneSide.Left),
            new("leg-l.foot", ZoneLevel.Medium, "leg-l", "Lewa stopa", ZoneSide.Left),
        ];
        return new ZoneCatalog(zones, new Dictionary<ZoneLevel, int[]>
        {
            [ZoneLevel.Easy] = [0, 0, 1, 1],
            [ZoneLevel.Medium] = [2, 2, 3, 4],
        }, 4);
    }

    private sealed class FixedDistances : IDistanceCalculator
    {
        private int _call;

        public DistanceResult Measure(SurfacePoint pin, Target target) => new(new[] { 3f, 12.5f, 41f }[_call++ % 3], false, []);
    }

    private sealed class StepClock : IClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 16, 19, 0, 0, TimeSpan.Zero);

        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }
}
