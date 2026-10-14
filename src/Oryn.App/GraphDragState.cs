using System.Numerics;

namespace Oryn.App;

public readonly record struct GraphBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(Vector2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}

/// <summary>Session-only positions and bounded, swept obstacle routing; no rendering-library types.</summary>
public sealed class GraphDragState
{
    public const float Clearance = 1;
    private readonly Dictionary<Guid, Vector2> _positions = [];
    private float _width, _height;
    private Vector2 _grab, _target, _original;
    private List<Vector2> _path = [];
    private readonly Dictionary<GraphBox, (bool Vertical, bool First)> _sides = [];
    public Guid? DraggedId { get; private set; }
    public bool Moving => DraggedId is not null && _path.Count > 0;
    public Vector2 Position(PreviewGraphNode node) => _positions.TryGetValue(node.Id, out var p) ? p : new(node.Column, node.Row);
    public GraphBox Bounds(Guid id) => new(_positions[id].X, _positions[id].Y, _width, _height);
    public void Configure(IEnumerable<PreviewGraphNode> nodes, PreviewGraphLayout layout)
    {
        float width = layout.CardWidth * GraphicalWorkspaceController.CellWidth;
        float height = layout.CardHeight * GraphicalWorkspaceController.CellHeight;
        if (_width != 0 && (width != _width || height != _height))
        {
            End();
            // Scale clearance as well as card size to preserve disjoint free placements on zoom/resize.
            foreach (Guid id in _positions.Keys.ToArray())
                _positions[id] *= new Vector2((width + Clearance) / (_width + Clearance), (height + Clearance) / (_height + Clearance));
        }
        _width = width; _height = height;
        foreach (PreviewGraphNode node in nodes)
            if (!_positions.ContainsKey(node.Id))
            {
                PanelBounds box = layout.Bounds(node);
                _positions[node.Id] = new(box.X * GraphicalWorkspaceController.CellWidth, box.Y * GraphicalWorkspaceController.CellHeight);
            }
    }
    public bool Begin(Guid id, Vector2 pointer)
    {
        if (!_positions.TryGetValue(id, out Vector2 position)) return false;
        End(); DraggedId = id; _original = position; _grab = pointer - position; _target = position;
        return true;
    }
    public void Target(Vector2 pointer)
    {
        if (DraggedId is not { } id || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y)) return;
        _target = Vector2.Clamp(pointer - _grab, new(-100_000, -100_000), new(100_000, 100_000));
        Plan(id);
    }
    private List<GraphBox> Obstacles(Guid id) => _positions.Where(pair => pair.Key != id)
        .Select(pair => new GraphBox(pair.Value.X - _width - Clearance, pair.Value.Y - _height - Clearance,
            _width * 2 + Clearance * 2, _height * 2 + Clearance * 2)).ToList();
    private void Plan(Guid id)
    {
        Vector2 start = _positions[id], target = _target;
        List<GraphBox> obstacles = Obstacles(id);
        float contact = 1;
        foreach (GraphBox box in obstacles)
            if (Entry(start, _target, box) is { } entry) contact = Math.Min(contact, entry);
        if (contact == 1)
        {
            // Unobstructed motion belongs to the pointer, with no attraction or spring lag.
            _positions[id] = _target; _path.Clear(); _sides.Clear(); return;
        }
        // Follow the requested displacement right up to first contact before any lateral detour.
        start = Vector2.Lerp(start, _target, contact);
        _positions[id] = start;
        // When the cursor requests overlap, aim along the nearer side of the obstacle, not through it.
        foreach (GraphBox box in obstacles)
        {
            if (!Inside(box, target)) continue;
            bool vertical = _sides.TryGetValue(box, out var side) ? side.Vertical : Math.Abs(target.Y - _original.Y) >= Math.Abs(target.X - _original.X);
            Vector2 a = vertical ? new(box.X, target.Y) : new(target.X, box.Y);
            Vector2 b = vertical ? new(box.Right, target.Y) : new(target.X, box.Bottom);
            bool validA = !obstacles.Any(other => Inside(other, a));
            bool validB = !obstacles.Any(other => Inside(other, b));
            if (validA || validB)
            {
                bool first = _sides.ContainsKey(box) ? side.First : Vector2.DistanceSquared(a, target) <= Vector2.DistanceSquared(b, target);
                if (!validA) first = false; if (!validB) first = true;
                _sides[box] = (vertical, first); target = first ? a : b;
            }
            else { _path.Clear(); return; }
        }
        _path = FindPath(start, target, obstacles);
    }
    public bool Advance(float seconds)
    {
        if (DraggedId is not { } id || _path.Count == 0) return false;
        float budget = Math.Clamp(seconds, 0, .05f) * 1200;
        Vector2 position = _positions[id], before = position;
        while (budget > 0 && _path.Count > 0)
        {
            Vector2 next = _path[0]; float distance = Vector2.Distance(position, next);
            if (distance <= budget) { position = next; budget -= distance; _path.RemoveAt(0); }
            else { position += (next - position) * (budget / distance); budget = 0; }
        }
        _positions[id] = position;
        return before != position;
    }
    public void End(bool cancel = false)
    {
        if (cancel && DraggedId is { } id) _positions[id] = _original;
        DraggedId = null; _path.Clear(); _sides.Clear();
    }
    private static bool Inside(GraphBox b, Vector2 p) => p.X > b.X + .001f && p.X < b.Right - .001f
        && p.Y > b.Y + .001f && p.Y < b.Bottom - .001f;
    private static bool Clear(Vector2 a, Vector2 b, List<GraphBox> obstacles) =>
        !obstacles.Any(box => Entry(a, b, box) is not null);

    private static float? Entry(Vector2 a, Vector2 b, GraphBox box)
    {
        // Swept rectangle interiors; return exact boundary contact, allowing boundary travel.
        float lo = 0, hi = 1;
        Vector2 d = b - a;
        for (int axis = 0; axis < 2; axis++)
        {
            float origin = axis == 0 ? a.X : a.Y, delta = axis == 0 ? d.X : d.Y;
            float min = axis == 0 ? box.X : box.Y, max = axis == 0 ? box.Right : box.Bottom;
            if (Math.Abs(delta) < .00001f)
            { if (origin <= min + .001f || origin >= max - .001f) return null; }
            else
            {
                float t1 = (min - origin) / delta, t2 = (max - origin) / delta;
                lo = Math.Max(lo, Math.Min(t1, t2)); hi = Math.Min(hi, Math.Max(t1, t2));
                if (lo >= hi - .000001f) return null;
            }
        }
        return lo;
    }
    private static List<Vector2> FindPath(Vector2 start, Vector2 target, List<GraphBox> obstacles)
    {
        if (Clear(start, target, obstacles)) return Vector2.DistanceSquared(start, target) < .001f ? [] : [target];
        List<Vector2> points = [start, target];
        foreach (GraphBox b in obstacles)
        foreach (Vector2 p in new[] { new Vector2(b.X, b.Y), new(b.Right, b.Y), new(b.X, b.Bottom), new(b.Right, b.Bottom) })
            if (!obstacles.Any(other => Inside(other, p))) points.Add(p);
        // Seven fixture nodes keep this small; deterministic shortest path gives stable ties.
        float[] distance = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray(); distance[0] = 0;
        int[] previous = Enumerable.Repeat(-1, points.Count).ToArray(); bool[] visited = new bool[points.Count];
        for (int step = 0; step < points.Count; step++)
        {
            int current = -1;
            for (int i = 0; i < points.Count; i++) if (!visited[i] && (current < 0 || distance[i] < distance[current])) current = i;
            if (current < 0 || !float.IsFinite(distance[current]) || current == 1) break;
            visited[current] = true;
            for (int next = 0; next < points.Count; next++)
            {
                if (visited[next] || !Clear(points[current], points[next], obstacles)) continue;
                float candidate = distance[current] + Vector2.Distance(points[current], points[next]);
                if (candidate < distance[next]) { distance[next] = candidate; previous[next] = current; }
            }
        }
        if (previous[1] < 0) return [];
        List<Vector2> path = [];
        for (int i = 1; i != 0; i = previous[i]) path.Add(points[i]);
        path.Reverse(); return path;
    }
}
