namespace BodyTouchGame.Geometry;

/// <summary>
/// A point on the render mesh: triangle index plus barycentric coordinates.
/// Position = (1 - U - V) * A + U * B + V * C, where A, B, C are the triangle corners
/// in index-buffer order (three.js: <c>intersection.barycoord.y</c> and <c>.z</c>).
/// </summary>
public readonly record struct SurfacePoint(int TriangleIndex, float BaryU, float BaryV);
