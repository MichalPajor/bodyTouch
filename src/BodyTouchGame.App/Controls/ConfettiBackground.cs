namespace BodyTouchGame.App.Controls;

/// <summary>
/// Confetti layer drawn under the content of every screen (spec 13). Uses the fixed pattern of the mockups:
/// large translucent polygons stretch with the screen, crumbs keep their size and stay near the edges.
/// </summary>
public sealed class ConfettiBackground : GraphicsView
{
    public ConfettiBackground()
    {
        Drawable = new ConfettiDrawable();
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
    }

    private sealed class ConfettiDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var sx = rect.Width / ConfettiPattern.Width;
            var sy = rect.Height / ConfettiPattern.Height;
            var crumbScale = sx;   // crumbs keep the mockup size relative to the screen width

            foreach (var polygon in ConfettiPattern.Polygons)
            {
                var points = polygon.Points;
                var path = new PathF();
                if (polygon.IsLarge)
                {
                    for (var i = 0; i < points.Length; i += 2)
                    {
                        var p = new PointF(rect.X + points[i] * sx, rect.Y + points[i + 1] * sy);
                        if (i == 0) path.MoveTo(p); else path.LineTo(p);
                    }
                }
                else
                {
                    float cx = 0, cy = 0;
                    var n = points.Length / 2;
                    for (var i = 0; i < points.Length; i += 2) { cx += points[i]; cy += points[i + 1]; }
                    cx /= n;
                    cy /= n;
                    var centre = new PointF(rect.X + cx * sx, rect.Y + cy * sy);
                    for (var i = 0; i < points.Length; i += 2)
                    {
                        var p = new PointF(centre.X + (points[i] - cx) * crumbScale, centre.Y + (points[i + 1] - cy) * crumbScale);
                        if (i == 0) path.MoveTo(p); else path.LineTo(p);
                    }
                }

                path.Close();
                canvas.FillColor = Color.FromArgb(polygon.Color).WithAlpha(polygon.Opacity);
                canvas.FillPath(path);
            }
        }
    }
}
