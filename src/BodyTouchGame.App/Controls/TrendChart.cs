using BodyTouchGame.App.Services;
using System.Globalization;

namespace BodyTouchGame.App.Controls;

/// <summary>
/// Line chart of the average distance per session (player statistics, spec 10): square points, Primary line,
/// faint guide lines with the min / max values. Lower is better.
/// </summary>
public sealed class TrendChart : GraphicsView
{
    public static readonly BindableProperty ValuesProperty =
        BindableProperty.Create(nameof(Values), typeof(IReadOnlyList<double>), typeof(TrendChart), Array.Empty<double>(),
            propertyChanged: (b, _, _) => ((TrendChart)b).Invalidate());

    public TrendChart()
    {
        Drawable = new ChartDrawable(this);
        BackgroundColor = Colors.Transparent;
    }

    public IReadOnlyList<double> Values { get => (IReadOnlyList<double>)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    private sealed class ChartDrawable(TrendChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var values = owner.Values;
            if (values.Count < 2) return;
            const float left = 44, right = 8, top = 10, bottom = 10;
            var plot = new RectF(rect.X + left, rect.Y + top, rect.Width - left - right, rect.Height - top - bottom);
            var min = values.Min();
            var max = values.Max();
            if (max - min < 1) { max += 0.5; min = Math.Max(0, min - 0.5); }

            float X(int i) => plot.Left + plot.Width * i / (values.Count - 1);
            float Y(double v) => (float)(plot.Top + plot.Height * (1 - (v - min) / (max - min)));

            // Guide lines with labels (max on top, min at the bottom).
            canvas.StrokeColor = Res.Color("Border");
            canvas.StrokeSize = 1;
            canvas.FontColor = Res.Color("Muted");
            canvas.FontSize = 11;
            foreach (var v in new[] { max, min })
            {
                canvas.DrawLine(plot.Left, Y(v), plot.Right, Y(v));
                canvas.DrawString(string.Format(AppCulture.Format, BodyTouchGame.App.Resources.Strings.AppResources.CmFormat, v), rect.X, Y(v) - 8, left - 6, 16,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            var path = new PathF();
            for (var i = 0; i < values.Count; i++)
            {
                if (i == 0) path.MoveTo(X(i), Y(values[i]));
                else path.LineTo(X(i), Y(values[i]));
            }

            canvas.StrokeColor = Res.Color("Primary");
            canvas.StrokeSize = 3;
            canvas.StrokeLineJoin = LineJoin.Miter;
            canvas.DrawPath(path);

            canvas.FillColor = Res.Color("Primary");
            for (var i = 0; i < values.Count; i++) canvas.FillRectangle(X(i) - 4, Y(values[i]) - 4, 8, 8);
            // The latest session stands out.
            canvas.FillColor = Res.Color("PartyCyan");
            canvas.FillRectangle(X(values.Count - 1) - 5, Y(values[^1]) - 5, 10, 10);
        }
    }
}
