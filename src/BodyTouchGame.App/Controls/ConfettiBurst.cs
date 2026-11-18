namespace BodyTouchGame.App.Controls;

/// <summary>
/// Confetti burst over the screen (spec 13: confetti at distance 0). Triangles and quads in the Party colours shoot
/// up from the bottom edge, spin and fall for about 2.6 s. Does not take touches. The view stays visible (it is
/// transparent and empty between bursts) so it is always laid out when a burst starts.
/// </summary>
public sealed class ConfettiBurst : GraphicsView
{
    private const double DurationSeconds = 2.6;
    private const int Count = 90;
    private static readonly Color[] Palette =
        [Color.FromArgb("#FF9A3C"), Color.FromArgb("#FF3D9A"), Color.FromArgb("#22D3EE"), Color.FromArgb("#8B5CF6"), Color.FromArgb("#F4F2FF")];

    private readonly List<Piece> _pieces = [];
    private IDispatcherTimer? _timer;
    private DateTime _start;

    public ConfettiBurst()
    {
        Drawable = new BurstDrawable(this);
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
    }

    /// <summary>Starts (or restarts) the burst.</summary>
    public void Play()
    {
        var random = new Random();
        _pieces.Clear();
        for (var i = 0; i < Count; i++)
        {
            var fromLeft = i % 2 == 0;
            _pieces.Add(new Piece(
                X: fromLeft ? random.NextDouble() * 0.25 : 0.75 + random.NextDouble() * 0.25,
                Vx: (fromLeft ? 1 : -1) * (0.15 + random.NextDouble() * 0.45),
                Vy: -(1.1 + random.NextDouble() * 0.7),
                Spin: (random.NextDouble() - 0.5) * 14,
                Size: 6 + random.NextDouble() * 8,
                Triangle: random.Next(2) == 0,
                Color: Palette[random.Next(Palette.Length)],
                Delay: random.NextDouble() * 0.35));
        }

        _start = DateTime.UtcNow;
        _timer ??= CreateTimer();
        _timer.Start();
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            if ((DateTime.UtcNow - _start).TotalSeconds > DurationSeconds)
            {
                timer.Stop();
                _pieces.Clear();
                Invalidate();
                return;
            }

            Invalidate();
        };
        return timer;
    }

    private sealed record Piece(double X, double Vx, double Vy, double Spin, double Size, bool Triangle, Color Color, double Delay);

    private sealed class BurstDrawable(ConfettiBurst owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var elapsed = (DateTime.UtcNow - owner._start).TotalSeconds;
            const double gravity = 1.6;    // screen heights per s²
            foreach (var p in owner._pieces)
            {
                var t = elapsed - p.Delay;
                if (t <= 0) continue;
                var x = (p.X + p.Vx * t) * rect.Width;
                var y = (1.0 + p.Vy * t + 0.5 * gravity * t * t) * rect.Height;
                if (y > rect.Height + 20) continue;
                var fade = Math.Clamp((DurationSeconds - elapsed) / 0.5, 0, 1);
                canvas.SaveState();
                canvas.Translate((float)x, (float)y);
                canvas.Rotate((float)(p.Spin * t * 57.3));
                canvas.FillColor = p.Color.WithAlpha((float)fade);
                var s = (float)p.Size;
                var path = new PathF();
                if (p.Triangle)
                {
                    path.MoveTo(0, -s);
                    path.LineTo(s * 0.9f, s * 0.6f);
                    path.LineTo(-s * 0.9f, s * 0.6f);
                }
                else
                {
                    path.MoveTo(-s * 0.7f, -s * 0.4f);
                    path.LineTo(s * 0.7f, -s * 0.5f);
                    path.LineTo(s * 0.6f, s * 0.45f);
                    path.LineTo(-s * 0.6f, s * 0.4f);
                }

                path.Close();
                canvas.FillPath(path);
                canvas.RestoreState();
            }
        }
    }
}
