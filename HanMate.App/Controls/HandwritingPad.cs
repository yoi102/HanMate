using HanMate.Core.Search;

namespace HanMate.App.Controls;

/// <summary>One character per pad. Leaving the writing square preserves the visible stroke.</summary>
public sealed class HandwritingPad : GraphicsView, IDrawable
{
    private readonly List<InkPoint[]> _strokes = [];
    private List<InkPoint>? _current;
    private RectF _square;
    public event EventHandler? StrokeStarted;
    public event EventHandler? StrokesChanged;
    public int StrokeCount => _strokes.Count;
    public InkPoint[][] Snapshot() => _strokes.Select(s => s.ToArray()).ToArray();

    public HandwritingPad()
    {
        AutomationId = "Handwriting.Pad"; Drawable = this;
        BackgroundColor = Colors.Transparent;
        StartInteraction += (_, e) =>
        {
            CancelStroke();
            if (e.Touches.Length != 1 || _strokes.Count >= HandwritingRecognizer.MaximumStrokes || !_square.Contains(e.Touches[0])) return;
            _current = []; Add(e.Touches[0]); StrokeStarted?.Invoke(this, EventArgs.Empty);
        };
        DragInteraction += (_, e) =>
        {
            if (_current is null) return;
            if (e.Touches.Length != 1) { CancelStroke(); StrokesChanged?.Invoke(this, EventArgs.Empty); return; }
            Add(e.Touches[0]);
        };
        EndInteraction += (_, e) =>
        {
            if (_current is null) return;
            if (e.Touches.Length == 1) Add(e.Touches[0]);
            CommitStroke();
        };
        // Some native views send Cancel instead of Up once the pointer leaves
        // their bounds. Keep the ink already shown; navigation explicitly cancels.
        CancelInteraction += (_, _) => CommitStroke();
        SizeChanged += (_, _) => { CancelStroke(); Invalidate(); };
    }
    private void Add(PointF point)
    {
        var next = new InkPoint(Math.Clamp((point.X - _square.X) / _square.Width, 0, 1),
            Math.Clamp((point.Y - _square.Y) / _square.Height, 0, 1));
        if (_current is null) return;
        if (_current.Count > 0 && Math.Abs(next.X - _current[^1].X) + Math.Abs(next.Y - _current[^1].Y) < .002f) return;
        // Decimate an unusually long stroke without splitting it or allocating indefinitely.
        if (_current.Count >= 512) _current = _current.Where((_, i) => i % 2 == 0).ToList();
        _current.Add(next); Invalidate();
    }
    private void CommitStroke()
    {
        if (_current is not { Count: > 0 }) return;
        _strokes.Add(_current.ToArray());
        CancelStroke(); StrokesChanged?.Invoke(this, EventArgs.Empty);
    }
    public void CancelStroke() { _current = null; Invalidate(); }
    public void Undo() { CancelStroke(); if (_strokes.Count > 0) _strokes.RemoveAt(_strokes.Count - 1); StrokesChanged?.Invoke(this, EventArgs.Empty); }
    public void Clear() { CancelStroke(); _strokes.Clear(); StrokesChanged?.Invoke(this, EventArgs.Empty); }
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var size = Math.Max(0, Math.Min((float)Width, (float)Height) - 8);
        _square = new(((float)Width - size) / 2, ((float)Height - size) / 2, size, size);
        if (size <= 0) return;
        canvas.FillColor = Color.FromArgb("#FCFAFF"); canvas.FillRectangle(_square);
        canvas.StrokeColor = Color.FromArgb("#A598B8"); canvas.StrokeSize = 1.5f; canvas.DrawRectangle(_square);
        canvas.StrokeDashPattern = [5, 5];
        canvas.DrawLine(_square.Center.X, _square.Top, _square.Center.X, _square.Bottom);
        canvas.DrawLine(_square.Left, _square.Center.Y, _square.Right, _square.Center.Y);
        canvas.StrokeDashPattern = null; canvas.StrokeColor = Color.FromArgb("#21152E"); canvas.StrokeSize = 3.5f;
        canvas.StrokeLineCap = LineCap.Round; canvas.StrokeLineJoin = LineJoin.Round;
        foreach (var stroke in _strokes) DrawStroke(stroke);
        if (_current is not null) DrawStroke(_current);
        void DrawStroke(IReadOnlyList<InkPoint> points)
        {
            if (points.Count == 0) return;
            var first = Screen(points[0]);
            if (points.Count == 1) { canvas.FillColor = Color.FromArgb("#21152E"); canvas.FillCircle(first.X, first.Y, 2); return; }
            using var path = new PathF(); path.MoveTo(first);
            for (var i = 1; i < points.Count; i++) path.LineTo(Screen(points[i]));
            canvas.DrawPath(path);
        }
        PointF Screen(InkPoint p) => new(_square.X + p.X * size, _square.Y + p.Y * size);
    }
}
