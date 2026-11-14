using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using BodyTouchGame.Geometry;
using Xunit.Abstractions;

namespace BodyTouchGame.Geometry.Tests;

public class AnalyticDistanceTests(ITestOutputHelper output)
{
    private const float MaxRelativeError = 0.10f;

    private static readonly ConcurrentDictionary<(string Mesh, int Ring), BodyGraph> Bodies = new();

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in AnalyticCases.All) data.Add($"{c.MeshKey}: {c.Name}");
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void SurfaceDistanceMatchesAnalyticValueWithinTenPercent(string caseName)
    {
        var c = AnalyticCases.All.Single(x => $"{x.MeshKey}: {x.Name}" == caseName);

        var (measured, error) = Measure(c, BodyGraphBuilder.DefaultRingDepth);

        output.WriteLine($"{caseName}: expected {c.ExpectedCm:F2} cm, measured {measured:F2} cm, error {error:P1}");
        Assert.True(error <= MaxRelativeError, $"Relative error {error:P1} exceeds {MaxRelativeError:P0}.");
    }

    [Fact]
    public void DistanceIsSymmetric()
    {
        foreach (var c in AnalyticCases.All.Where(x => x.MeshKey is "plane-irregular" or "sphere"))
        {
            var mesh = AnalyticCases.Meshes[c.MeshKey].Value;
            var solver = new GeodesicSolver(GetBody(c.MeshKey, BodyGraphBuilder.DefaultRingDepth));
            var there = solver.DistanceToPoint(mesh.Locate(c.From), mesh.Locate(c.To)).DistanceCm;
            var back = solver.DistanceToPoint(mesh.Locate(c.To), mesh.Locate(c.From)).DistanceCm;
            Assert.InRange(MathF.Abs(there - back), 0, 0.05f * there);
        }
    }

    /// <summary>Prints the accuracy table (graph built from mesh edges only vs. 2-ring) for the stage report.</summary>
    [Fact]
    public void AccuracyReport()
    {
        var table = new StringBuilder();
        table.AppendLine("| Siatka | Przypadek | Oczekiwana [cm] | 1-ring [cm] | błąd | 2-ring [cm] | błąd |");
        table.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        var maxError = new Dictionary<int, float> { [1] = 0, [2] = 0 };
        foreach (var c in AnalyticCases.All)
        {
            var (m1, e1) = Measure(c, 1);
            var (m2, e2) = Measure(c, 2);
            maxError[1] = MathF.Max(maxError[1], e1);
            maxError[2] = MathF.Max(maxError[2], e2);
            table.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {c.MeshKey} | {c.Name} | {c.ExpectedCm:F2} | {m1:F2} | {e1:P1} | {m2:F2} | {e2:P1} |"));
        }

        table.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| **maks.** | | | | **{maxError[1]:P1}** | | **{maxError[2]:P1}** |"));
        output.WriteLine(table.ToString());

        Assert.True(maxError[2] <= MaxRelativeError);
    }

    private static (float Measured, float RelativeError) Measure(AnalyticCase c, int ringDepth)
    {
        var mesh = AnalyticCases.Meshes[c.MeshKey].Value;
        var solver = new GeodesicSolver(GetBody(c.MeshKey, ringDepth));
        var result = solver.DistanceToPoint(mesh.Locate(c.From), mesh.Locate(c.To));
        return (result.DistanceCm, MathF.Abs(result.DistanceCm - c.ExpectedCm) / c.ExpectedCm);
    }

    private static BodyGraph GetBody(string meshKey, int ringDepth) =>
        Bodies.GetOrAdd((meshKey, ringDepth), key =>
        {
            var mesh = AnalyticCases.Meshes[key.Mesh].Value;
            return BodyGraphBuilder.Build(mesh.Positions, mesh.Indices, key.Ring);
        });
}
