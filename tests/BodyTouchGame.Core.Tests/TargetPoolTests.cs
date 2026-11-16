using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.Core.Tests;

public class TargetPoolTests
{
    private static readonly TargetPoolBuilder Builder = new(TestData.Catalog());

    private static string[] ZoneIds(IEnumerable<Target> pool) => pool.OfType<ZoneTarget>().Select(t => t.ZoneId).ToArray();

    [Fact]
    public void LevelSelectionGivesDrawableZonesOfThatLevel()
    {
        Assert.Equal(["head", "arm-l", "leg-l"], ZoneIds(Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), [])));
        Assert.Equal(["head.face", "head.scalp", "arm-l.hand", "arm-l.elbow", "leg-l.knee", "leg-l.foot"],
            ZoneIds(Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), [])));
    }

    [Fact]
    public void NonDrawableZonesAreNeverInThePool()
    {
        var pool = Builder.Build(new ZoneSetSelection([ZoneLevel.Easy, ZoneLevel.Medium], ["pelvis", "pelvis.all"]), []);

        Assert.DoesNotContain("pelvis", ZoneIds(pool));
        Assert.DoesNotContain("pelvis.all", ZoneIds(pool));
    }

    [Fact]
    public void MixedLevelsAndExplicitZonesAreCombined()
    {
        var pool = Builder.Build(new ZoneSetSelection([ZoneLevel.Easy], ["leg-l.knee"]), []);

        Assert.Equal(["head", "arm-l", "leg-l", "leg-l.knee"], ZoneIds(pool));
    }

    [Fact]
    public void ExcludedZoneBlocksItselfItsChildrenAndItsParent()
    {
        var both = new ZoneSetSelection([ZoneLevel.Easy, ZoneLevel.Medium]);

        // Excluding a Medium zone blocks it and its Easy parent (they overlap); siblings stay.
        Assert.Equal(["arm-l", "leg-l", "head.scalp", "arm-l.hand", "arm-l.elbow", "leg-l.knee", "leg-l.foot"],
            ZoneIds(Builder.Build(both, ["head.face"])));
        // Excluding an Easy zone blocks all its children.
        Assert.Equal(["head", "leg-l", "head.face", "head.scalp", "leg-l.knee", "leg-l.foot"],
            ZoneIds(Builder.Build(both, ["arm-l"])));
    }

    [Fact]
    public void UnknownExcludedIdsAreIgnored() =>
        Assert.Equal(3, Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), ["no-such-zone"]).Count);

    [Fact]
    public void LevelOverridesAddAndRemoveZones()
    {
        LevelOverride[] overrides =
        [
            new("leg-l.knee", ZoneLevel.Easy, true),   // knee is also an Easy target
            new("head", ZoneLevel.Easy, false),        // head is no longer an Easy target
        ];

        Assert.Equal(["arm-l", "leg-l", "leg-l.knee"], ZoneIds(Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), [], overrides)));
    }

    [Fact]
    public void CustomPointsJoinBySelectedLevelOrExplicitly()
    {
        var onFace = Point(10, triangle: 0, ZoneLevel.Hard);
        var onKnee = Point(11, triangle: 6, ZoneLevel.Medium);
        var inPelvis = Point(12, triangle: 9, ZoneLevel.Medium);

        var medium = Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), [], customPoints: [onFace, onKnee, inPelvis]);
        var explicitFace = Builder.Build(new ZoneSetSelection(customPointIds: [10]), [], customPoints: [onFace, onKnee]);

        Assert.Equal([11L, 12L], medium.OfType<CustomPointTarget>().Select(p => p.PointId));   // custom point in a hidden zone is drawn (D12)
        Assert.Equal([10L], explicitFace.OfType<CustomPointTarget>().Select(p => p.PointId));
        Assert.Empty(ZoneIds(explicitFace));
    }

    [Fact]
    public void CustomPointsInExcludedZonesAreNotDrawn()
    {
        var onFace = Point(10, triangle: 0, ZoneLevel.Medium);
        var onScalp = Point(11, triangle: 2, ZoneLevel.Medium);

        var noFace = Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), ["head.face"], customPoints: [onFace, onScalp]);
        var noHead = Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), ["head"], customPoints: [onFace, onScalp]);

        Assert.Equal([11L], noFace.OfType<CustomPointTarget>().Select(p => p.PointId));
        Assert.Empty(noHead.OfType<CustomPointTarget>());
    }

    [Fact]
    public void EverythingExcludedGivesAnEmptyPool() =>
        Assert.Empty(Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), ["head", "arm-l", "leg-l"]));

    [Fact]
    public void LevelWithoutDataGivesAnEmptyPoolUntilStage8() =>
        Assert.Empty(Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Hard), []));

    [Fact]
    public void DrawNeverRepeatsThePreviousTargetOfTheGuesser()
    {
        var pool = Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Easy), []);
        var previous = pool[0];

        for (var i = 0; i < 20; i++)
            Assert.NotEqual(previous, TargetDrawer.Draw(pool, previous, new SequenceRandom(i)));
    }

    [Fact]
    public void DrawCoversTheWholePool()
    {
        var pool = Builder.Build(ZoneSetSelection.ForLevels(ZoneLevel.Medium), []);
        var random = new Targets.SystemRandomSource(new Random(5));

        var drawn = Enumerable.Range(0, 300).Select(_ => TargetDrawer.Draw(pool, null, random)).Distinct().Count();

        Assert.Equal(pool.Count, drawn);
    }

    [Fact]
    public void DrawFromASingleTargetPoolMayRepeatAndEmptyPoolGivesNull()
    {
        Target only = new ZoneTarget("head");

        Assert.Equal(only, TargetDrawer.Draw([only], only, new SequenceRandom(0)));
        Assert.Null(TargetDrawer.Draw([], null, new SequenceRandom(0)));
    }

    private static CustomPoint Point(long id, int triangle, params ZoneLevel[] levels) =>
        new(id, $"P{id}", TestData.OnTriangle(triangle), 2, levels, []);
}
