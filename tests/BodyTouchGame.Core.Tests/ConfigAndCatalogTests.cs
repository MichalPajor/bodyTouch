using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Serialization;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Tests;

public class GameConfigTests
{
    [Fact]
    public void BuiltInPresetsMatchTheSpecification()
    {
        Assert.Equal(["Easy", "Medium", "Hard", "Hardcore"], BuiltInPresets.All.Select(p => p.Key));
        Assert.All(BuiltInPresets.All, p =>
        {
            Assert.Equal(TargetSelection.Random, p.Config.TargetSelection);
            Assert.Empty(p.Config.Validate());
            Assert.Equal(3, p.Config.RoundsPerPlayer);
        });
        Assert.Equal((ZoneLevel.Easy, TouchMethod.WholeHand, true, 6), Describe(BuiltInPresets.Easy));
        Assert.Equal((ZoneLevel.Medium, TouchMethod.TwoFingers, true, 5), Describe(BuiltInPresets.Medium));
        Assert.Equal((ZoneLevel.Hard, TouchMethod.OneFinger, true, 4), Describe(BuiltInPresets.Hard));
        Assert.Equal((ZoneLevel.Hardcore, TouchMethod.Tap, false, 2), Describe(BuiltInPresets.Hardcore));
    }

    private static (ZoneLevel, TouchMethod, bool, int) Describe(GameConfig c) =>
        (c.ZoneSet.Levels.Single(), c.TouchMethod, c.MovementAllowed, c.TouchSeconds);

    [Theory]
    [InlineData(0, null, 5, 40, 3, "TouchSeconds")]
    [InlineData(11, null, 5, 40, 3, "TouchSeconds")]
    [InlineData(5, 2, 5, 40, 3, "GuessSeconds")]
    [InlineData(5, 31, 5, 40, 3, "GuessSeconds")]
    [InlineData(5, null, -1, 40, 3, "FullPointsRadiusCm")]
    [InlineData(5, null, 40, 40, 3, "ZeroPointsDistanceCm")]
    [InlineData(5, null, 5, 40, 0, "RoundsPerPlayer")]
    public void InvalidValuesAreReported(int touch, int? guess, double r, double z, int rounds, string field)
    {
        var config = BuiltInPresets.Medium with { TouchSeconds = touch, GuessSeconds = guess, FullPointsRadiusCm = r, ZeroPointsDistanceCm = z, RoundsPerPlayer = rounds };

        Assert.Contains(config.Validate(), p => p.Contains(field));
    }

    [Fact]
    public void EmptyZoneSetIsInvalidOnlyInRandomMode()
    {
        var empty = BuiltInPresets.Easy with { ZoneSet = new ZoneSetSelection() };

        Assert.NotEmpty(empty.Validate());
        Assert.Empty((empty with { TargetSelection = TargetSelection.ChosenByModel }).Validate());
    }

    [Fact]
    public void AnyMixOfFieldsCanBeChanged()
    {
        // spec 6 example: "Easy + two fingers + 3 s"
        var custom = BuiltInPresets.Easy with { TouchMethod = TouchMethod.TwoFingers, TouchSeconds = 3 };

        Assert.Empty(custom.Validate());
        Assert.NotEqual(BuiltInPresets.Easy, custom);
        Assert.Equal(BuiltInPresets.Easy, custom with { TouchMethod = TouchMethod.WholeHand, TouchSeconds = 6 });
    }

    [Fact]
    public void ZoneSetSelectionIsNormalisedAndComparedByContent()
    {
        var a = new ZoneSetSelection([ZoneLevel.Medium, ZoneLevel.Easy, ZoneLevel.Easy], ["b", "a"], [3, 1, 3]);
        var b = new ZoneSetSelection([ZoneLevel.Easy, ZoneLevel.Medium], ["a", "b"], [1, 3]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal([ZoneLevel.Easy, ZoneLevel.Medium], a.Levels);
    }

    [Fact]
    public void ConfigSurvivesJsonRoundTripWithReadableEnums()
    {
        var config = BuiltInPresets.Hardcore with
        {
            TargetSelection = TargetSelection.ChosenByModel,
            GuessSeconds = 20,
            SpeedBonus = true,
            ZoneSet = new ZoneSetSelection([ZoneLevel.Easy, ZoneLevel.Hard], ["leg-l.knee"], [7]),
        };

        var json = JsonSerializer.Serialize(config, CoreJsonContext.Default.GameConfig);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.GameConfig);

        Assert.Equal(config, copy);
        Assert.Contains("\"targetSelection\":\"ChosenByModel\"", json);
        Assert.Contains("\"touchMethod\":\"Tap\"", json);
    }
}

public class ZoneCatalogTests
{
    private static readonly ZoneCatalog Catalog = TestData.Catalog();

    [Fact]
    public void LooksUpZonesByTriangleAndLevel()
    {
        Assert.Equal("head", Catalog.ZoneAt(2, ZoneLevel.Easy)!.Id);
        Assert.Equal("head.scalp", Catalog.ZoneAt(2, ZoneLevel.Medium)!.Id);
        Assert.Null(Catalog.ZoneAt(2, ZoneLevel.Hard));
        Assert.Equal(["head", "head.scalp"], Catalog.ZonesAt(2).Select(z => z.Id));
        Assert.Equal([ZoneLevel.Easy, ZoneLevel.Medium], Catalog.Levels);
    }

    [Fact]
    public void NavigatesTheHierarchy()
    {
        Assert.Equal(["head"], Catalog.Ancestors("head.face").Select(z => z.Id));
        Assert.Equal(["arm-l.hand", "arm-l.elbow"], Catalog.Descendants("arm-l").Select(z => z.Id));
        Assert.Empty(Catalog.Descendants("arm-l.hand"));
    }

    [Fact]
    public void InconsistentDataIsRejected()
    {
        var zones = TestData.Zones;
        Assert.Throws<ArgumentException>(() => new ZoneCatalog(zones, new Dictionary<ZoneLevel, int[]> { [ZoneLevel.Easy] = [0, 0] }, 3));
        Assert.Throws<ArgumentException>(() => new ZoneCatalog(zones, new Dictionary<ZoneLevel, int[]> { [ZoneLevel.Easy] = [4] }, 1));   // Medium zone on Easy
        Assert.Throws<ArgumentException>(() => new ZoneCatalog([new ZoneDefinition("a.b", ZoneLevel.Medium, "a", "x", ZoneSide.Center)],
            new Dictionary<ZoneLevel, int[]>(), 0));
    }

    [Fact]
    public void LoadsTheRealZoneFile()
    {
        using var stream = File.OpenRead(RealData.ZonesPath);

        var catalog = ZoneCatalog.Load(stream);

        Assert.Equal(26756, catalog.TriangleCount);
        Assert.Equal(11, catalog.Zones.Count(z => z.Level == ZoneLevel.Easy));
        Assert.Equal(38, catalog.Zones.Count(z => z.Level == ZoneLevel.Medium));
        Assert.False(catalog.Get("pelvis.all").Drawable);
        Assert.Equal(ZoneSide.Left, catalog.Get("arm-l.elbow").Side);
        Assert.Equal("Lewy łokieć", catalog.Get("arm-l.elbow").NamePl);
    }

    [Fact]
    public void InvalidZoneFileIsReported()
    {
        const string json = """{"version":1,"triangleCount":2,"zones":[{"id":"a","level":"Easy","parentId":null,"namePl":"A","side":"Center"}],"triangleZones":{"Easy":[0]}}""";

        Assert.Throws<InvalidDataException>(() => ZoneCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }
}

/// <summary>Core on the real model: catalog + graph + pools + distance calculator together.</summary>
public class RealModelTests
{
    private static readonly Lazy<(ZoneCatalog Catalog, BodyGraph Body)> Data = new(() =>
    {
        using var zones = File.OpenRead(RealData.ZonesPath);
        return (ZoneCatalog.Load(zones), BodyGraphSerializer.ReadFile(RealData.GraphPath));
    });

    [Fact]
    public void DistancesToZoneCustomAndManualTargets()
    {
        var (catalog, body) = Data.Value;
        var calculator = new GeodesicDistanceCalculator(body, catalog);
        var hand = FirstTriangle(catalog, "arm-l.hand");
        var knee = FirstTriangle(catalog, "leg-l.knee");
        var pin = new SurfacePoint(hand, 0.3f, 0.3f);

        var toKnee = calculator.Measure(pin, new ZoneTarget("leg-l.knee"));
        var toHand = calculator.Measure(pin, new ZoneTarget("arm-l.hand"));
        var toPoint = calculator.Measure(pin, new ManualPointTarget(new SurfacePoint(knee, 0.3f, 0.3f)));
        var toCustom = calculator.Measure(pin, new CustomPointTarget(1, "Kolano", new SurfacePoint(knee, 0.3f, 0.3f), 5));

        Assert.InRange(toKnee.DistanceCm, 80, 200);                 // over the shoulder
        Assert.True(toHand.IsInside);
        Assert.Equal(0, toHand.DistanceCm);
        Assert.True(toPoint.DistanceCm >= toKnee.DistanceCm);       // a point in the zone is at least as far as the zone
        Assert.Equal(toPoint.DistanceCm - 5, toCustom.DistanceCm, 3);
        Assert.NotEmpty(toKnee.Path);
    }

    [Fact]
    public void PoolsOnTheRealCatalogRespectDrawableAndExclusions()
    {
        var (catalog, _) = Data.Value;
        var builder = new TargetPoolBuilder(catalog);

        var easy = builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), []);
        var medium = builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), ["arm-l"]);

        Assert.Equal(10, easy.Count);                                // 11 Easy zones minus the pelvis
        Assert.Equal(37 - 5, medium.Count);                          // 38 Medium minus pelvis.all minus 5 left-arm zones
        Assert.DoesNotContain(medium, t => t is ZoneTarget z && z.ZoneId.StartsWith("arm-l", StringComparison.Ordinal));
    }

    private static int FirstTriangle(ZoneCatalog catalog, string zoneId)
    {
        var index = catalog.IndexOf(zoneId);
        var labels = catalog.TriangleLabels(catalog.Get(zoneId).Level);
        for (var t = 0; t < labels.Length; t++) if (labels[t] == index) return t;
        throw new InvalidOperationException();
    }
}

internal static class RealData
{
    private static string Raw([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "src", "BodyTouchGame.App", "Resources", "Raw"));

    public static string ZonesPath => Path.Combine(Raw(), "wwwroot", "models", "body_zones.json");
    public static string GraphPath => Path.Combine(Raw(), "body_graph.bin");
}
