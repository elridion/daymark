using System.Collections.Generic;
using UnityEngine;

namespace Daymark
{
    internal enum LegKind
    {
        Walk,
        Sail,
        Portal,
        // No known way across the water: straight at the goal.
        Straight,
    }

    internal class Leg
    {
        public LegKind Kind;
        public List<Vector3> Points = new List<Vector3>();
        // For portal legs: the portal stepped through and the one it led to when planned.
        public PortalLink Link;
    }

    internal struct PortalLink
    {
        public Vector3 Entry;
        public Vector3 Exit;
        public ZDOID EntryId;
        public ZDOID ExitId;
    }

    internal class RouteRequest
    {
        public Vector3 Start;
        public bool OnShip;
        public Vector3 Goal;
        public List<Vector3> Ships = new List<Vector3>();
        public List<PortalLink> Portals = new List<PortalLink>();
    }

    /// <summary>
    /// Finds the quickest way home over the <see cref="NavGrid"/> with A* on two layers: on foot
    /// (land, wading and short swims) and under sail (water deep enough for a ship). The layers
    /// meet only where a ship lies (boarding) and along coasts (landing); connected
    /// portals join two foot cells, several per cell where portal hubs stand. Costs are rough
    /// seconds, so a long walk loses to a ship or a portal. Runs on a worker thread and reuses its
    /// buffers, one search at a time.
    /// </summary>
    internal class RouteFinder
    {
        // Seconds per metre: jogging, swimming, sailing; sailing along the coast or through shallows
        // costs more, so sea routes keep to open water.
        private const float WalkCost = 1f / 6f;
        private const float SwimCost = 1f / 1.5f;
        private const float SailCost = 1f / 12f;
        private const float CoastFactor = 1.5f;
        private const float ShallowFactor = 3f;
        // Slopes cost more the steeper they are; steeper than this rise per metre is a cliff.
        private const float SlopeFactor = 3f;
        private const float MaxGrade = 1.2f;
        // Fixed seconds for getting into or out of a ship and for stepping through a portal.
        private const float BoardCost = 10f;
        private const float LandCost = 10f;
        private const float PortalCost = 20f;
        // Cells around a ship from which it can be boarded.
        private const int BoardReach = 2;
        // Gives up on searches this large rather than stall the worker; the wisp then goes straight.
        // The heuristic barely guides a search once a portal lets out near the bed, so this bounds
        // the cost of every search, found or not.
        private const int MaxExpansions = 1500000;
        // Douglas-Peucker tolerance in metres when thinning the cell path into waypoints.
        private const float Simplify = 8f;

        private static readonly int[] s_dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] s_dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly NavGrid m_grid;
        private readonly int m_cells;
        private readonly float[] m_cost;
        private readonly int[] m_parent;
        private readonly MinHeap m_open = new MinHeap();
        private readonly Dictionary<int, List<int>> m_boarding = new Dictionary<int, List<int>>();
        // Entry cell to the portal links starting there, and the link a node was last reached by.
        private readonly Dictionary<int, List<int>> m_portalLinks = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> m_arrivedBy = new Dictionary<int, int>();
        private readonly Dictionary<int, Vector3> m_exactPoint = new Dictionary<int, Vector3>();
        private Vector3 m_goalPoint;
        private float m_exitToGoal;

        public RouteFinder(NavGrid grid)
        {
            m_grid = grid;
            m_cells = grid.Size * grid.Size;
            m_cost = new float[m_cells * 2];
            m_parent = new int[m_cells * 2];
        }

        public List<Leg> Find(RouteRequest request)
        {
            PrepareLinks(request);
            int start = m_grid.CellOf(request.Start) + (request.OnShip ? m_cells : 0);
            int goal = m_grid.CellOf(request.Goal);

            for (int i = 0; i < m_cost.Length; i++) m_cost[i] = float.PositiveInfinity;
            m_open.Clear();
            m_cost[start] = 0f;
            m_parent[start] = -1;
            m_open.Push(start, Heuristic(start % m_cells, goal, request));

            int expansions = 0;
            while (m_open.Count > 0 && expansions++ < MaxExpansions)
            {
                m_open.Pop(out int node, out float priority);
                int cell = node % m_cells;
                float cost = m_cost[node];
                // Stale heap entry from before a cheaper way here was found.
                if (priority > cost + Heuristic(cell, goal, request) + 0.01f) continue;
                if (node == goal) return BuildLegs(node, request);

                if (node < m_cells) ExpandFoot(node, cost, goal, request);
                else ExpandSail(node, cost, goal, request);
            }
            return null;
        }

        private void ExpandFoot(int cell, float cost, int goal, RouteRequest request)
        {
            int size = m_grid.Size;
            int x = cell % size;
            int z = cell / size;
            float height = Mathf.Max(m_grid.Heights[cell], m_grid.WaterLevel);
            for (int d = 0; d < 8; d++)
            {
                int nx = x + s_dx[d];
                int nz = z + s_dz[d];
                if (!m_grid.InBounds(nx, nz)) continue;
                int next = m_grid.Index(nx, nz);
                byte flags = m_grid.Flags[next];
                // The bed's cell and portal entry cells are always reachable: a bed or portal on a
                // pier or by the shore can sit in a cell the map counts as deep water or a cliff.
                bool isGoal = next == goal || m_portalLinks.ContainsKey(next);
                if ((flags & (NavGrid.Walk | NavGrid.Swim)) == 0 && !isGoal) continue;

                float step = d < 4 ? NavGrid.CellSize : NavGrid.CellSize * 1.4142f;
                float grade = Mathf.Abs(Mathf.Max(m_grid.Heights[next], m_grid.WaterLevel) - height) / step;
                if (grade > MaxGrade && !isGoal) continue;
                if (isGoal) grade = Mathf.Min(grade, MaxGrade);
                float perMetre = (flags & NavGrid.Walk) != 0 ? WalkCost : SwimCost;
                Relax(next, cell, cost + step * perMetre * (1f + SlopeFactor * grade), goal, request);
            }

            if (m_boarding.TryGetValue(cell, out var ships))
                foreach (int ship in ships)
                    Relax(ship + m_cells, cell, cost + BoardCost, goal, request);
            if (m_portalLinks.TryGetValue(cell, out var links))
                foreach (int link in links)
                    Relax(m_grid.CellOf(request.Portals[link].Exit), cell, cost + PortalCost, goal, request, link);
        }

        private void ExpandSail(int node, float cost, int goal, RouteRequest request)
        {
            int size = m_grid.Size;
            int cell = node - m_cells;
            int x = cell % size;
            int z = cell / size;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + s_dx[d];
                int nz = z + s_dz[d];
                if (!m_grid.InBounds(nx, nz)) continue;
                int next = m_grid.Index(nx, nz);
                byte flags = m_grid.Flags[next];
                float step = d < 4 ? NavGrid.CellSize : NavGrid.CellSize * 1.4142f;

                if ((flags & NavGrid.Walk) != 0 || next == goal)
                {
                    Relax(next, node, cost + LandCost + step * WalkCost, goal, request);
                    continue;
                }
                if (float.IsNegativeInfinity(m_grid.Heights[next])) continue;
                float factor = (flags & NavGrid.Sail) == 0 ? ShallowFactor : (flags & NavGrid.Coast) != 0 ? CoastFactor : 1f;
                Relax(next + m_cells, node, cost + step * SailCost * factor, goal, request);
            }
        }

        private void Relax(int node, int from, float cost, int goal, RouteRequest request, int link = -1)
        {
            if (cost >= m_cost[node]) return;
            m_cost[node] = cost;
            m_parent[node] = from;
            if (link >= 0) m_arrivedBy[node] = link;
            else if (m_arrivedBy.Count > 0) m_arrivedBy.Remove(node);
            m_open.Push(node, cost + Heuristic(node % m_cells, goal, request));
        }

        // Straight-line time at sailing speed, the cheapest way to cover ground, but no more than
        // from the portal exit nearest the goal; never overestimates, so the route is the quickest
        // found. Constant time, since busy servers have hundreds of portals.
        private float Heuristic(int cell, int goal, RouteRequest request)
        {
            return Mathf.Min(FlatDistance(m_grid.Centre(cell), m_goalPoint), m_exitToGoal) * SailCost;
        }

        private void PrepareLinks(RouteRequest request)
        {
            m_boarding.Clear();
            m_portalLinks.Clear();
            m_arrivedBy.Clear();
            m_exactPoint.Clear();
            m_goalPoint = m_grid.Centre(m_grid.CellOf(request.Goal));
            m_exitToGoal = float.PositiveInfinity;
            foreach (Vector3 ship in request.Ships)
            {
                m_grid.ToCell(ship, out int sx, out int sz);
                int shipCell = m_grid.Index(sx, sz);
                m_exactPoint[shipCell + m_cells] = ship;
                for (int dz = -BoardReach; dz <= BoardReach; dz++)
                    for (int dx = -BoardReach; dx <= BoardReach; dx++)
                    {
                        if (!m_grid.InBounds(sx + dx, sz + dz)) continue;
                        int cell = m_grid.Index(sx + dx, sz + dz);
                        if (!m_boarding.TryGetValue(cell, out var list)) m_boarding[cell] = list = new List<int>();
                        list.Add(shipCell);
                    }
            }
            for (int i = 0; i < request.Portals.Count; i++)
            {
                PortalLink link = request.Portals[i];
                int entry = m_grid.CellOf(link.Entry);
                if (entry == m_grid.CellOf(link.Exit)) continue;
                if (!m_portalLinks.TryGetValue(entry, out var list)) m_portalLinks[entry] = list = new List<int>();
                list.Add(i);
                m_exitToGoal = Mathf.Min(m_exitToGoal, FlatDistance(link.Exit, m_goalPoint));
            }
        }

        private List<Leg> BuildLegs(int goal, RouteRequest request)
        {
            var nodes = new List<int>();
            for (int node = goal; node != -1; node = m_parent[node]) nodes.Add(node);
            nodes.Reverse();

            var legs = new List<Leg>();
            Leg leg = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                int node = nodes[i];
                LegKind kind = node < m_cells ? LegKind.Walk : LegKind.Sail;
                Vector3 point = PointOf(node);
                if (i > 0 && m_arrivedBy.TryGetValue(node, out int via))
                {
                    // Walk right up to the portal, step through, and go on from where it lets out.
                    PortalLink link = request.Portals[via];
                    if (leg != null && leg.Kind == LegKind.Walk) leg.Points[leg.Points.Count - 1] = link.Entry;
                    var portal = new Leg { Kind = LegKind.Portal, Link = link };
                    portal.Points.Add(link.Entry);
                    portal.Points.Add(link.Exit);
                    legs.Add(portal);
                    leg = null;
                    point = link.Exit;
                }
                if (leg == null || leg.Kind != kind)
                {
                    leg = new Leg { Kind = kind };
                    legs.Add(leg);
                }
                leg.Points.Add(point);
            }

            if (legs.Count > 0 && legs[0].Kind == LegKind.Walk) legs[0].Points[0] = request.Start;
            Leg last = legs[legs.Count - 1];
            if (last.Kind == LegKind.Walk) last.Points[last.Points.Count - 1] = request.Goal;
            foreach (var each in legs)
                if (each.Kind != LegKind.Portal) each.Points = Thin(each.Points);
            return legs;
        }

        private Vector3 PointOf(int node)
        {
            if (m_exactPoint.TryGetValue(node, out Vector3 exact)) return exact;
            Vector3 centre = m_grid.Centre(node % m_cells);
            if (node >= m_cells) centre.y = m_grid.WaterLevel;
            return centre;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static List<Vector3> Thin(List<Vector3> points)
        {
            if (points.Count < 3) return points;
            var keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            ThinRange(points, keep, 0, points.Count - 1);
            var result = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
                if (keep[i]) result.Add(points[i]);
            return result;
        }

        private static void ThinRange(List<Vector3> points, bool[] keep, int first, int last)
        {
            if (last - first < 2) return;
            float worst = 0f;
            int index = -1;
            for (int i = first + 1; i < last; i++)
            {
                float distance = DistanceToSegment(points[i], points[first], points[last]);
                if (distance > worst)
                {
                    worst = distance;
                    index = i;
                }
            }
            if (worst <= Simplify) return;
            keep[index] = true;
            ThinRange(points, keep, first, index);
            ThinRange(points, keep, index, last);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 s = new Vector2(a.x, a.z);
            Vector2 e = new Vector2(b.x, b.z);
            Vector2 along = e - s;
            float t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - s, along) / along.sqrMagnitude) : 0f;
            return Vector2.Distance(p, s + along * t);
        }

        /// <summary>Binary min-heap of node ids by priority, allowing stale duplicates.</summary>
        private class MinHeap
        {
            private int[] m_nodes = new int[1024];
            private float[] m_keys = new float[1024];

            public int Count { get; private set; }

            public void Clear() => Count = 0;

            public void Push(int node, float key)
            {
                if (Count == m_nodes.Length)
                {
                    System.Array.Resize(ref m_nodes, Count * 2);
                    System.Array.Resize(ref m_keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (m_keys[parent] <= key) break;
                    m_nodes[i] = m_nodes[parent];
                    m_keys[i] = m_keys[parent];
                    i = parent;
                }
                m_nodes[i] = node;
                m_keys[i] = key;
            }

            public void Pop(out int node, out float key)
            {
                node = m_nodes[0];
                key = m_keys[0];
                int lastNode = m_nodes[--Count];
                float lastKey = m_keys[Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && m_keys[child + 1] < m_keys[child]) child++;
                    if (m_keys[child] >= lastKey) break;
                    m_nodes[i] = m_nodes[child];
                    m_keys[i] = m_keys[child];
                    i = child;
                }
                m_nodes[i] = lastNode;
                m_keys[i] = lastKey;
            }
        }
    }
}
