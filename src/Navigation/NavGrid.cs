using System;
using System.Threading;
using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// A coarse map of the whole world for the wisp's route finding, sampled from the world
    /// generator's own heights on a background thread. Valheim builds terrain from the same
    /// generator on its heightmap thread, so sampling it off the main thread is safe. Player-made
    /// terrain changes and buildings are not in it; the route follows the land as it was generated.
    /// </summary>
    internal class NavGrid
    {
        // Metres per cell: fine enough that a river two cells wide still shows as water.
        public const float CellSize = 16f;
        public const float WorldRadius = 10000f;

        // Water a player can wade through before swimming (vanilla swims at about 2 m), and the
        // depth a ship needs to sail without grounding.
        private const float WadeDepth = 1.6f;
        private const float SailDepth = 2.5f;
        // Deep water counts as swimmable when land lies on both sides within this many cells along
        // one line, so rivers and narrow straits (about 30 m) can be crossed on foot.
        private const int SwimGap = 2;

        public const byte Walk = 1;
        public const byte Swim = 2;
        public const byte Sail = 4;
        // Sailable water right next to land, which routes avoid where they can.
        public const byte Coast = 8;

        public readonly int Size;
        public readonly float[] Heights;
        public readonly byte[] Flags;
        public readonly float WaterLevel;

        private volatile bool m_ready;
        private volatile bool m_cancelled;

        public bool Ready => m_ready;

        private NavGrid(float waterLevel)
        {
            WaterLevel = waterLevel;
            Size = Mathf.CeilToInt(2f * WorldRadius / CellSize);
            Heights = new float[Size * Size];
            Flags = new byte[Size * Size];
        }

        public static NavGrid StartBuild(WorldGenerator generator, float waterLevel)
        {
            var grid = new NavGrid(waterLevel);
            var thread = new Thread(() => grid.Build(generator)) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
            return grid;
        }

        public void Cancel() => m_cancelled = true;

        public int Index(int x, int z) => z * Size + x;

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Size && z < Size;

        public void ToCell(Vector3 point, out int x, out int z)
        {
            x = Mathf.Clamp(Mathf.FloorToInt((point.x + WorldRadius) / CellSize), 0, Size - 1);
            z = Mathf.Clamp(Mathf.FloorToInt((point.z + WorldRadius) / CellSize), 0, Size - 1);
        }

        public int CellOf(Vector3 point)
        {
            ToCell(point, out int x, out int z);
            return Index(x, z);
        }

        // The cell's centre at its ground height, or at the water surface for water.
        public Vector3 Centre(int index)
        {
            int x = index % Size;
            int z = index / Size;
            return new Vector3(x * CellSize - WorldRadius + CellSize * 0.5f, Mathf.Max(Heights[index], WaterLevel),
                z * CellSize - WorldRadius + CellSize * 0.5f);
        }

        private void Build(WorldGenerator generator)
        {
            try
            {
                float half = CellSize * 0.5f;
                float edgeSqr = WorldRadius * WorldRadius;
                for (int z = 0; z < Size; z++)
                {
                    if (m_cancelled) return;
                    float wz = z * CellSize - WorldRadius + half;
                    for (int x = 0; x < Size; x++)
                    {
                        float wx = x * CellSize - WorldRadius + half;
                        // Outside the world's edge counts as deep sea that no route uses.
                        Heights[Index(x, z)] = wx * wx + wz * wz > edgeSqr ? float.NegativeInfinity : generator.GetHeight(wx, wz);
                    }
                }
                Classify();
                m_ready = true;
            }
            catch (Exception e)
            {
                // The world unloading under the thread ends up here; the grid simply never becomes ready.
                if (!m_cancelled) Jotunn.Logger.LogWarning($"Building the wisp's world map failed: {e.Message}");
            }
        }

        private void Classify()
        {
            for (int i = 0; i < Heights.Length; i++)
            {
                if (float.IsNegativeInfinity(Heights[i])) continue;
                float depth = WaterLevel - Heights[i];
                if (depth < WadeDepth) Flags[i] = Walk;
                else if (depth >= SailDepth) Flags[i] = Sail;
            }

            // Deep water between two banks close together: rivers and straits a player can swim.
            for (int z = 0; z < Size; z++)
            {
                if (m_cancelled) return;
                for (int x = 0; x < Size; x++)
                {
                    int i = Index(x, z);
                    if ((Flags[i] & Walk) != 0 || float.IsNegativeInfinity(Heights[i])) continue;
                    if (IsNarrow(x, z, 1, 0) || IsNarrow(x, z, 0, 1) || IsNarrow(x, z, 1, 1) || IsNarrow(x, z, 1, -1))
                        Flags[i] |= Swim;
                    if ((Flags[i] & Sail) != 0 && TouchesLand(x, z)) Flags[i] |= Coast;
                }
            }
        }

        private bool IsNarrow(int x, int z, int dx, int dz)
        {
            int ahead = StepsToLand(x, z, dx, dz);
            int behind = StepsToLand(x, z, -dx, -dz);
            return ahead > 0 && behind > 0 && ahead + behind - 1 <= SwimGap;
        }

        private int StepsToLand(int x, int z, int dx, int dz)
        {
            for (int step = 1; step <= SwimGap; step++)
            {
                int nx = x + dx * step;
                int nz = z + dz * step;
                if (!InBounds(nx, nz)) return 0;
                if ((Flags[Index(nx, nz)] & Walk) != 0) return step;
            }
            return 0;
        }

        private bool TouchesLand(int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (InBounds(x + dx, z + dz) && (Flags[Index(x + dx, z + dz)] & Walk) != 0) return true;
            return false;
        }
    }
}
