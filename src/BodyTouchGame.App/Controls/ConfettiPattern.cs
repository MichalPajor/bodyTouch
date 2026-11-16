namespace BodyTouchGame.App.Controls;

/// <summary>
/// The confetti pattern of the approved mockups (spec 13): 3 large translucent polygons and crumbs near the edges,
/// generated once with a fixed seed. Coordinates in mockup pixels (390 × 844), extracted from
/// "Body Touch Game – propozycje UI.html" by tools/scripts (see ConfettiBackground for scaling).
/// </summary>
internal static class ConfettiPattern
{
    public const float Width = 390;
    public const float Height = 844;

    /// <summary>Polygons drawn in order: colour, opacity, flat [x0, y0, x1, y1, ...].</summary>
    public static readonly IReadOnlyList<ConfettiPolygon> Polygons =
    [
        new("#8B5CF6", 0.16f, [-40f, 120f, 210f, 40f, 300f, 260f, 120f, 380f, -60f, 300f]),
        new("#FF3D9A", 0.13f, [250f, 520f, 430f, 470f, 450f, 760f, 300f, 830f, 200f, 690f]),
        new("#22D3EE", 0.1f, [-30f, 600f, 110f, 560f, 140f, 720f, 20f, 800f]),
        new("#FF3D9A", 0.7f, [60.5f, 59.2f, 58.3f, 60.3f, 56.4f, 58.7f, 60.1f, 57.1f]),
        new("#FF9A3C", 0.55f, [248f, 87.8f, 243.1f, 88.5f, 242.3f, 79.9f]),
        new("#FF3D9A", 0.85f, [223.9f, 54.8f, 230.7f, 57.4f, 224.7f, 60.4f]),
        new("#8B5CF6", 0.85f, [72f, 69f, 69.7f, 72.1f, 67.1f, 69.6f]),
        new("#8B5CF6", 0.7f, [187f, 8.6f, 186.9f, 2.8f, 193f, -2f, 196.7f, 4.7f]),
        new("#FF9A3C", 0.85f, [228.7f, 61.7f, 224.6f, 64.2f, 225f, 58.1f]),
        new("#FF9A3C", 0.7f, [152.9f, 80.9f, 149.6f, 77.8f, 154.3f, 76.1f, 154.6f, 78.5f]),
        new("#22D3EE", 0.7f, [61.1f, 59.2f, 57.3f, 61.7f, 58.3f, 57f]),
        new("#FF9A3C", 0.7f, [10.3f, 134.4f, 11.2f, 123.2f, 19.3f, 132.2f]),
        new("#8B5CF6", 0.85f, [6.5f, 518f, -3.2f, 520.1f, 2.3f, 512.8f]),
        new("#8B5CF6", 0.55f, [193.6f, 89.7f, 186.9f, 88.3f, 188.3f, 82.6f]),
        new("#8B5CF6", 0.85f, [2f, 497.7f, 6f, 499.1f, 1.3f, 501.6f]),
        new("#22D3EE", 0.55f, [84.7f, 66.7f, 91.3f, 74.4f, 84.2f, 78f]),
        new("#8B5CF6", 0.7f, [271.6f, 89.1f, 265.5f, 85.9f, 275.1f, 84.5f]),
        new("#FF3D9A", 0.7f, [14.3f, 690.9f, 16.6f, 693.6f, 13.4f, 696.2f, 11.9f, 693.2f]),
        new("#22D3EE", 0.85f, [383.9f, 214.7f, 384.6f, 221.2f, 381.2f, 219.1f]),
        new("#22D3EE", 0.7f, [6.4f, 83.1f, 8.6f, 90.4f, 1.2f, 87.2f]),
        new("#FF9A3C", 0.85f, [53f, 77.6f, 53.2f, 83.9f, 50.9f, 85.8f, 47.9f, 78.7f]),
        new("#FF3D9A", 0.85f, [15.7f, 205.6f, 16.5f, 200.9f, 19.5f, 203f]),
        new("#FF3D9A", 0.7f, [297.5f, 42.6f, 305.8f, 45f, 300.2f, 50.3f]),
        new("#8B5CF6", 0.85f, [384.3f, 746.2f, 385.8f, 754.9f, 378.9f, 751.9f]),
        new("#8B5CF6", 0.55f, [19.6f, 765.7f, 16.6f, 769f, 13.5f, 765.2f]),
        new("#8B5CF6", 0.7f, [370.5f, 419.4f, 369.3f, 411.6f, 375.5f, 416.8f]),
        new("#22D3EE", 0.55f, [7.7f, 817.5f, 4.6f, 813.6f, 9.6f, 812.7f]),
        new("#FF9A3C", 0.85f, [380.8f, 203.5f, 369.8f, 205.1f, 376.9f, 196.4f]),
        new("#FF3D9A", 0.7f, [368f, 738.3f, 368.6f, 734.2f, 371.9f, 735.7f]),
        new("#8B5CF6", 0.85f, [59.9f, 4.8f, 65f, 2.8f, 63.1f, 8.7f]),
        new("#FF9A3C", 0.55f, [10.7f, 50.8f, -0.3f, 48.6f, 7.4f, 38.8f]),
        new("#22D3EE", 0.7f, [25.6f, 349.3f, 17.8f, 354.9f, 18.9f, 345.2f]),
        new("#22D3EE", 0.85f, [14f, 471.3f, 6.5f, 477.2f, 4.9f, 463.5f]),
        new("#22D3EE", 0.55f, [11.1f, 81.7f, 11.8f, 76.6f, 16.1f, 78.9f]),
        new("#8B5CF6", 0.85f, [157.4f, 6.2f, 153.9f, 6f, 156.5f, 1.4f]),
        new("#22D3EE", 0.55f, [387.3f, 460.5f, 384.8f, 465.1f, 381.1f, 464.8f, 382.4f, 458.6f]),
        new("#22D3EE", 0.55f, [387.3f, 765.9f, 377.7f, 767.3f, 378.5f, 760.5f]),
        new("#FF3D9A", 0.85f, [7.5f, 285.1f, 7f, 292.8f, 0.4f, 290.7f, 1.4f, 285.8f]),
        new("#FF3D9A", 0.7f, [254.9f, 47.9f, 247f, 41.9f, 253.9f, 34.8f, 259.4f, 44.1f]),
        new("#FF3D9A", 0.85f, [183.9f, 38.5f, 194.6f, 43.8f, 186.5f, 51.9f]),
        new("#FF9A3C", 0.55f, [383.4f, 83.8f, 382.9f, 86.9f, 379.3f, 85.4f, 380.2f, 82.3f]),
        new("#FF3D9A", 0.85f, [50.5f, 840.4f, 48.7f, 836.3f, 55.2f, 833.4f]),
        new("#8B5CF6", 0.55f, [55.8f, 28.4f, 57.3f, 34.4f, 51.7f, 31.3f]),
    ];
}

internal sealed record ConfettiPolygon(string Color, float Opacity, float[] Points)
{
    /// <summary>Large background shapes are stretched with the screen; crumbs keep their size.</summary>
    public bool IsLarge => Points.Length >= 8 && Size > 40;

    private float Size
    {
        get
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < Points.Length; i += 2)
            {
                minX = MathF.Min(minX, Points[i]); maxX = MathF.Max(maxX, Points[i]);
                minY = MathF.Min(minY, Points[i + 1]); maxY = MathF.Max(maxY, Points[i + 1]);
            }

            return MathF.Max(maxX - minX, maxY - minY);
        }
    }
}
