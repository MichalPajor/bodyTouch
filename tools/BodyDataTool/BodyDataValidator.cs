using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyDataTool;

/// <summary>
/// Checks body_zones.json and body_graph.bin against each other and against spec 4.1-4.3.
/// Returns a list of problems (empty = valid) plus statistics for the report.
/// </summary>
public static class BodyDataValidator
{
    public const int MinTriangles = 15_000;
    public const int MaxTriangles = 30_000;
    public const float ExpectedHeightCm = 170f;

    /// <summary>Distal arm zones must never share a graph edge with the trunk or the legs (spec 4.1:
    /// hands and arms do not touch the torso or thighs).</summary>
    private static readonly string[] ArmSuffixes = [".hand", ".forearm", ".elbow"];
    private static readonly string[] TrunkAndLegPrefixes = ["chest", "belly", "upper-back", "lower-back", "pelvis", "leg-"];

    public sealed record Result(List<string> Problems, Dictionary<string, object> Statistics)
    {
        public bool IsValid => Problems.Count == 0;
    }

    public static Result Validate(BodyZonesFile zones, BodyGraph body)
    {
        var problems = new List<string>();
        var stats = new Dictionary<string, object>();

        // ---- zone list ----
        var index = new Dictionary<string, int>();
        for (var i = 0; i < zones.Zones.Count; i++)
        {
            var z = zones.Zones[i];
            if (!index.TryAdd(z.Id, i)) problems.Add($"Duplicate zone id '{z.Id}'.");
            if (Array.IndexOf(BodyZonesFile.LevelOrder, z.Level) < 0) problems.Add($"Zone '{z.Id}': unknown level '{z.Level}'.");
            if (z.Side is not ("Left" or "Right" or "Center")) problems.Add($"Zone '{z.Id}': unknown side '{z.Side}'.");
            if (string.IsNullOrWhiteSpace(z.NamePl)) problems.Add($"Zone '{z.Id}': missing Polish name.");
        }

        foreach (var z in zones.Zones)
        {
            var level = Array.IndexOf(BodyZonesFile.LevelOrder, z.Level);
            if (level == 0 && z.ParentId is not null) problems.Add($"Easy zone '{z.Id}' must not have a parent.");
            if (level > 0)
            {
                if (z.ParentId is null || !index.TryGetValue(z.ParentId, out var parent))
                    problems.Add($"Zone '{z.Id}': parent '{z.ParentId}' not found.");
                else if (Array.IndexOf(BodyZonesFile.LevelOrder, zones.Zones[parent].Level) != level - 1)
                    problems.Add($"Zone '{z.Id}': parent '{z.ParentId}' is not on the previous level.");
            }
        }

        // ---- triangle assignment ----
        if (zones.TriangleCount != body.TriangleCount)
            problems.Add($"triangleCount {zones.TriangleCount} differs from the graph's render mesh ({body.TriangleCount}).");
        if (body.TriangleCount is < MinTriangles or > MaxTriangles)
            problems.Add($"Render mesh has {body.TriangleCount} triangles, expected {MinTriangles}-{MaxTriangles} (spec 4.1).");

        foreach (var required in new[] { "Easy", "Medium" })
            if (!zones.TriangleZones.ContainsKey(required)) problems.Add($"triangleZones has no '{required}' level.");

        foreach (var (level, labels) in zones.TriangleZones)
        {
            if (labels.Length != body.TriangleCount)
            {
                problems.Add($"triangleZones[{level}] has {labels.Length} entries, expected {body.TriangleCount}.");
                continue;
            }

            var used = new HashSet<int>();
            for (var t = 0; t < labels.Length; t++)
            {
                if ((uint)labels[t] >= (uint)zones.Zones.Count) { problems.Add($"triangleZones[{level}][{t}] out of range."); break; }
                if (zones.Zones[labels[t]].Level != level) { problems.Add($"Triangle {t}: zone '{zones.Zones[labels[t]].Id}' is not a {level} zone."); break; }
                used.Add(labels[t]);
            }

            foreach (var (z, i) in zones.Zones.Select((z, i) => (z, i)))
                if (z.Level == level && !used.Contains(i)) problems.Add($"Zone '{z.Id}' ({level}) has no triangles.");
            stats[$"zones_{level}"] = used.Count;
        }

        // Hierarchy: the zone of a triangle on level n must be a child of its zone on level n-1.
        for (var level = 1; level < BodyZonesFile.LevelOrder.Length; level++)
        {
            if (!zones.TriangleZones.TryGetValue(BodyZonesFile.LevelOrder[level], out var child) ||
                !zones.TriangleZones.TryGetValue(BodyZonesFile.LevelOrder[level - 1], out var parent) ||
                child.Length != parent.Length) continue;
            var mismatches = 0;
            for (var t = 0; t < child.Length; t++)
                if ((uint)child[t] < (uint)zones.Zones.Count && zones.Zones[child[t]].ParentId != zones.Zones[parent[t]].Id) mismatches++;
            if (mismatches > 0) problems.Add($"{mismatches} triangles break the hierarchy between {BodyZonesFile.LevelOrder[level - 1]} and {BodyZonesFile.LevelOrder[level]}.");
        }

        // ---- geometry ----
        var positions = body.MeshPositions;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in positions) { minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y); }
        stats["height_cm"] = Math.Round(maxY - minY, 2);
        if (MathF.Abs(maxY - minY - ExpectedHeightCm) > 0.5f) problems.Add($"Model height {maxY - minY:F2} cm, expected {ExpectedHeightCm} cm.");

        var graph = body.Graph;
        stats["graph_vertices"] = graph.VertexCount;
        stats["graph_edges"] = graph.EdgeCount / 2;
        stats["render_triangles"] = body.TriangleCount;

        var reached = ConnectedFrom(graph, 0);
        stats["graph_connected"] = reached == graph.VertexCount;
        if (reached != graph.VertexCount) problems.Add($"Graph is not connected: {reached} of {graph.VertexCount} vertices reachable.");

        float maxEdge = 0;
        foreach (var length in graph.EdgeLengths) maxEdge = MathF.Max(maxEdge, length);
        stats["max_edge_cm"] = Math.Round(maxEdge, 2);

        if (zones.TriangleZones.TryGetValue("Medium", out var medium) && medium.Length == body.TriangleCount)
        {
            string ZoneOfVertex(int v) => zones.Zones[medium[body.VertexTriangles[v]]].Id;
            var bad = 0;
            for (var v = 0; v < graph.VertexCount; v++)
            {
                if (!IsDistalArm(ZoneOfVertex(v))) continue;
                foreach (var w in graph.GetNeighbors(v))
                    if (IsTrunkOrLeg(ZoneOfVertex(w))) bad++;
            }

            stats["arm_to_trunk_edges"] = bad;
            if (bad > 0) problems.Add($"{bad} graph edges connect hand/forearm/elbow with the trunk or legs.");

            foreach (var (z, i) in zones.Zones.Select((z, i) => (z, i)))
            {
                foreach (var (level, labels) in zones.TriangleZones)
                {
                    if (z.Level != level || labels.Length != body.TriangleCount || !labels.Contains(i)) continue;
                    var region = ZoneRegion.FromTriangleLabels(body, labels, i);
                    if (region.Vertices.Length == 0) problems.Add($"Zone '{z.Id}' has no graph vertices.");
                }
            }
        }

        return new Result(problems, stats);
    }

    private static bool IsDistalArm(string id) => id.StartsWith("arm-", StringComparison.Ordinal) && ArmSuffixes.Any(id.EndsWith);
    private static bool IsTrunkOrLeg(string id) => TrunkAndLegPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal));

    private static int ConnectedFrom(SurfaceGraph graph, int start)
    {
        if (graph.VertexCount == 0) return 0;
        var seen = new bool[graph.VertexCount];
        var stack = new Stack<int>();
        stack.Push(start);
        seen[start] = true;
        var count = 0;
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            count++;
            foreach (var w in graph.GetNeighbors(v))
            {
                if (seen[w]) continue;
                seen[w] = true;
                stack.Push(w);
            }
        }

        return count;
    }
}
