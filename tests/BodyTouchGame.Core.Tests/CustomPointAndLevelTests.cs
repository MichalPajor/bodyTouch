using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Tests;

/// <summary>Stage 8: custom points in a game, level overrides, the four zone levels of the real model.</summary>
public class CustomPointAndLevelTests
{
    private static readonly Lazy<(ZoneCatalog Catalog, BodyGraph Body)> Real = new(() =>
    {
        using var zones = File.OpenRead(RealData.ZonesPath);
        return (ZoneCatalog.Load(zones), BodyGraphSerializer.ReadFile(RealData.GraphPath));
    });

    [Fact]
    public void ACustomPointCanBeDrawnAndHitInASession()
    {
        var catalog = TestData.Catalog();
        var point = new CustomPoint(7, "Pieprzyk", TestData.OnTriangle(6), 3, [ZoneLevel.Hard], []);
        var selection = new ZoneSetSelection(customPointIds: [7]);
        var config = TestData.Config() with { ZoneSet = selection };
        var builder = new TargetPoolBuilder(catalog);
        var players = new[] { TestData.Ola, TestData.Kuba };
        var pools = players.ToDictionary(p => p.Id, _ => builder.Build(selection, [], [], [point]));
        var distances = new FakeDistances { Distance = 0 };
        var session = new GameSession(new SessionSetup(players, config, RoleOrder.AllPairs, null, pools), distances, catalog,
            new SequenceRandom(0), new FakeClock());

        session.Start();
        session.Continue();
        var target = Assert.IsType<CustomPointTarget>(session.VisibleTarget);
        Assert.Equal("Pieprzyk", target.Name);
        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();
        session.PlacePin(TestData.OnTriangle(6));
        var result = session.Submit();

        Assert.Equal(100, result.BasePoints);
        Assert.Equal(target, distances.Calls.Single().Target);
        Assert.Equal("leg-l.knee", result.GuessedZoneId);       // point targets: zone on the finest level
    }

    [Fact]
    public void AnEmptyLevelSelectionWithOnlyCustomPointsOrZonesIsValid()
    {
        Assert.Empty((TestData.Config() with { ZoneSet = new ZoneSetSelection(customPointIds: [1]) }).Validate());
        Assert.Empty((TestData.Config() with { ZoneSet = new ZoneSetSelection(zoneIds: ["head.face"]) }).Validate());
        Assert.NotEmpty((TestData.Config() with { ZoneSet = new ZoneSetSelection() }).Validate());
    }

    [Fact]
    public void MixedSelectionCombinesLevelsZonesAndPointsWithoutDuplicates()
    {
        var catalog = TestData.Catalog();
        var builder = new TargetPoolBuilder(catalog);
        var selection = new ZoneSetSelection([ZoneLevel.Easy], ["head.face", "head"], [3]);
        CustomPoint[] points = [new(3, "A", TestData.OnTriangle(0), 2, [], []), new(4, "B", TestData.OnTriangle(3), 2, [ZoneLevel.Medium], [])];

        var pool = builder.Build(selection, [], [], points);

        Assert.Equal(["head", "arm-l", "leg-l", "head.face"], pool.OfType<ZoneTarget>().Select(z => z.ZoneId));   // 'head' once
        Assert.Equal([3L], pool.OfType<CustomPointTarget>().Select(p => p.PointId));                      // B: not selected
    }

    [Fact]
    public void LevelOverrideMovesAZoneBetweenLevelsWithoutTouchingTheCatalog()
    {
        var catalog = TestData.Catalog();
        var builder = new TargetPoolBuilder(catalog);
        LevelOverride[] overrides = [new("head.face", ZoneLevel.Easy, true), new("head", ZoneLevel.Easy, false)];

        var easy = builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), [], overrides);

        Assert.Contains(new ZoneTarget("head.face"), easy);
        Assert.DoesNotContain(new ZoneTarget("head"), easy);
        Assert.Equal(ZoneLevel.Medium, catalog.Get("head.face").Level);   // built-in data unchanged
    }

    [Fact]
    public void RealModelHasFourNestedLevels()
    {
        var (catalog, _) = Real.Value;

        Assert.Equal([ZoneLevel.Easy, ZoneLevel.Medium, ZoneLevel.Hard, ZoneLevel.Hardcore], catalog.Levels);
        var hard = catalog.Zones.Count(z => z.Level == ZoneLevel.Hard);
        var hardcore = catalog.Zones.Count(z => z.Level == ZoneLevel.Hardcore);
        Assert.InRange(hard, 100, 160);           // spec 4.2: about 120
        Assert.InRange(hardcore, 250, 350);       // spec 4.2: about 300
        for (var t = 0; t < catalog.TriangleCount; t++)
        {
            var hc = catalog.ZoneAt(t, ZoneLevel.Hardcore)!;
            var h = catalog.ZoneAt(t, ZoneLevel.Hard)!;
            Assert.Equal(h.Id, hc.ParentId);
            Assert.Equal(catalog.ZoneAt(t, ZoneLevel.Medium)!.Id, h.ParentId);
        }

        Assert.All(catalog.Zones.Where(z => z.Level >= ZoneLevel.Hard), z => Assert.False(string.IsNullOrWhiteSpace(z.NamePl)));
        Assert.Equal(catalog.Zones.Count, catalog.Zones.Select(z => z.Id).Distinct().Count());
    }

    [Fact]
    public void HardAndHardcorePresetsDrawOnlyDrawableZonesOfTheirLevel()
    {
        var (catalog, _) = Real.Value;
        var builder = new TargetPoolBuilder(catalog);

        var hard = builder.Build(BuiltInPresets.Hard.ZoneSet, []);
        var hardcore = builder.Build(BuiltInPresets.Hardcore.ZoneSet, ["arm-l.hand"]);

        Assert.All(hard, t => Assert.Equal(ZoneLevel.Hard, catalog.Get(((ZoneTarget)t).ZoneId).Level));
        Assert.DoesNotContain(hard, t => ((ZoneTarget)t).ZoneId.StartsWith("pelvis", StringComparison.Ordinal));
        Assert.DoesNotContain(hardcore, t => ((ZoneTarget)t).ZoneId.StartsWith("arm-l.hand", StringComparison.Ordinal));
        Assert.Contains(hardcore, t => ((ZoneTarget)t).ZoneId == "arm-r.hand.finger2.distal");
    }

    [Fact]
    public void CustomPointZoneIsTheGraphNeighbourhoodWithinTheRadius()
    {
        var (catalog, body) = Real.Value;
        var calculator = new GeodesicDistanceCalculator(body, catalog);
        var knee = Enumerable.Range(0, catalog.TriangleCount).First(t => catalog.ZoneAt(t, ZoneLevel.Medium)!.Id == "leg-l.knee");
        var centre = new SurfacePoint(knee, 0.3f, 0.3f);

        var small = calculator.VerticesWithin(centre, 2);
        var large = calculator.VerticesWithin(centre, 8);

        Assert.NotEmpty(small);
        Assert.True(large.Count > small.Count * 3);
        Assert.Subset(large.ToHashSet(), small.ToHashSet());
        var inside = calculator.Measure(centre, new CustomPointTarget(1, "Kolano", centre, 2));
        Assert.True(inside.IsInside);
        Assert.Equal(0, inside.DistanceCm);
    }
}
