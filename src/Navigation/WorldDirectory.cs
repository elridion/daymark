using System.Collections;
using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Every ship and portal in the world, for the wisp's routes: the wisp is magic and simply knows
    /// them. The server (or a host, or single player) holds every ZDO, but a client on a dedicated
    /// server only receives the ones near it, so a client asks the server for the full list every
    /// <see cref="RequestInterval"/> seconds. A server without Daymark never answers, and the client
    /// then knows only what its own game has received. Portal connections are read fresh for every
    /// route, since retagging a portal makes vanilla reconnect it within seconds.
    /// </summary>
    internal class WorldDirectory
    {
        private const float RequestInterval = 20f;
        // A server without Daymark never answers; stop asking after this many tries until the next login.
        private const int MaxUnanswered = 3;
        // Ship ZDOs carry no index of their own, so they are found by walking the sectors a few at a
        // time (vanilla's iterative prefab search), restarting this many seconds after each pass.
        private const float ShipScanInterval = 20f;
        // A server scans for ships only while some client asked within this many seconds.
        private const float ServerScanWindow = 120f;
        // A ship found missing where the list placed it is left out for this long, until the list
        // has caught up.
        private const float MissingShipTime = 60f;
        private const float MissingShipRange = 25f;
        private const string RpcName = "DaymarkDirectory";

        private struct Portal
        {
            public ZDOID Id;
            public Vector3 Position;
            public Quaternion Rotation;
            public ZDOID Target;
        }

        private struct Missing
        {
            public Vector3 Position;
            public float Until;
        }

        private static CustomRPC s_rpc;
        private static WorldDirectory s_instance;

        // The server's last answer; merged under what this game holds itself.
        private readonly Dictionary<ZDOID, Portal> m_serverPortals = new Dictionary<ZDOID, Portal>();
        private readonly Dictionary<ZDOID, Vector3> m_serverShips = new Dictionary<ZDOID, Vector3>();
        private readonly Dictionary<ZDOID, Portal> m_portals = new Dictionary<ZDOID, Portal>();
        private readonly Dictionary<ZDOID, Vector3> m_ships = new Dictionary<ZDOID, Vector3>();
        private readonly List<Missing> m_missing = new List<Missing>();
        private float m_requestTimer = RequestInterval;
        private int m_unanswered;
        private ZNet m_session;
        private float m_lastAsked = float.NegativeInfinity;

        // The ship scan: prefab names with a Ship, the pass in progress and the last complete one.
        // A finished pass keeps ZDOIDs, not ZDOs: vanilla pools ZDO objects, so a destroyed ship's
        // ZDO comes back as the next object created, with another prefab and position.
        private readonly List<string> m_shipPrefabs = new List<string>();
        private readonly HashSet<int> m_shipHashes = new HashSet<int>();
        private ZNetScene m_prefabSource;
        private readonly List<ZDO> m_scanning = new List<ZDO>();
        private readonly List<ZDOID> m_scanned = new List<ZDOID>();
        private int m_scanPrefab;
        private int m_scanIndex;
        private float m_scanTimer = ShipScanInterval;

        public WorldDirectory()
        {
            s_instance = this;
        }

        // Jotunn wants custom RPCs registered from the plugin's Awake.
        public static void Register()
        {
            s_rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerRequest, OnClientAnswer);
        }

        public void Update()
        {
            if (!ZNet.instance || ZDOMan.instance == null || !ZNetScene.instance) return;
            // A new world or connection starts with nothing: rows from the last one would point at
            // ships and portals that are not in this world.
            if (m_session != ZNet.instance)
            {
                m_session = ZNet.instance;
                m_serverPortals.Clear();
                m_serverShips.Clear();
                m_missing.Clear();
                m_unanswered = 0;
                m_requestTimer = RequestInterval;
            }
            bool server = ZNet.instance.IsServer();
            bool playing = Player.m_localPlayer && Game.m_noMap && DaymarkPlugin.Wisp.Value;
            // A server keeps its ship list current in a world without a map, so the first answer
            // already has ships; players who turned the map off themselves start it by asking.
            bool serving = server && ((ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoMap))
                || Time.time - m_lastAsked < ServerScanWindow);
            if (playing || serving) ScanShips();
            if (playing && !server) Ask();
        }

        // Adds every ship and every connected portal pair, the portal exits where vanilla puts a
        // player: a metre out of the partner's front.
        public void FillRequest(RouteRequest request)
        {
            Merge();
            foreach (var ship in m_ships.Values)
                if (!IsMissing(ship)) request.Ships.Add(ship);
            foreach (var portal in m_portals.Values)
            {
                if (portal.Target.IsNone() || !m_portals.TryGetValue(portal.Target, out var target)) continue;
                request.Portals.Add(new PortalLink
                {
                    Entry = portal.Position,
                    Exit = target.Position + target.Rotation * Vector3.forward,
                    EntryId = portal.Id,
                    ExitId = target.Id,
                });
            }
        }

        public void MarkShipMissing(Vector3 position)
        {
            m_missing.Add(new Missing { Position = position, Until = Time.time + MissingShipTime });
        }

        private bool IsMissing(Vector3 ship)
        {
            for (int i = m_missing.Count - 1; i >= 0; i--)
            {
                if (m_missing[i].Until < Time.time)
                {
                    m_missing.RemoveAt(i);
                    continue;
                }
                if ((m_missing[i].Position - ship).sqrMagnitude < MissingShipRange * MissingShipRange) return true;
            }
            return false;
        }

        // The server's rows, overwritten by this game's own copies where those are current: a client
        // keeps every ZDO it ever received, but the server updates only those near the player, so a
        // far copy may predate a retag or a voyage, and the server's row wins there.
        private void Merge()
        {
            bool host = ZNet.instance && ZNet.instance.IsServer();
            m_portals.Clear();
            foreach (var portal in m_serverPortals.Values) m_portals[portal.Id] = portal;
            if (ZDOMan.instance != null)
                foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
                    if (Current(zdo, host, m_serverPortals.ContainsKey(zdo.m_uid))) m_portals[zdo.m_uid] = ReadPortal(zdo);

            m_ships.Clear();
            foreach (var ship in m_serverShips) m_ships[ship.Key] = ship.Value;
            foreach (ZDOID id in m_scanned)
            {
                ZDO zdo = Ship(id);
                if (Current(zdo, host, m_serverShips.ContainsKey(id))) m_ships[id] = zdo.GetPosition();
            }
        }

        // The ship with this ZDOID if it still exists and is still a ship.
        private ZDO Ship(ZDOID id)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            return zdo != null && zdo.IsValid() && zdo.m_uid == id && m_shipHashes.Contains(zdo.GetPrefab()) ? zdo : null;
        }

        private static bool Current(ZDO zdo, bool host, bool serverKnows)
        {
            if (zdo == null || !zdo.IsValid()) return false;
            return host || !serverKnows || (ZNetScene.instance && ZNetScene.instance.IsAreaReady(zdo.GetPosition()));
        }

        private void Ask()
        {
            m_requestTimer += Time.deltaTime;
            if (m_requestTimer < RequestInterval) return;
            m_requestTimer = 0f;
            if (s_rpc == null || m_unanswered >= MaxUnanswered) return;
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null) return;
            m_unanswered++;
            s_rpc.SendPackage(server.m_uid, new ZPackage());
        }

        // One step of vanilla's iterative prefab search per frame, prefab after prefab; the finished
        // pass replaces the last one.
        private void ScanShips()
        {
            if (m_prefabSource != ZNetScene.instance) FindShipPrefabs();
            if (m_shipPrefabs.Count == 0) return;
            if (m_scanPrefab >= m_shipPrefabs.Count)
            {
                m_scanTimer += Time.deltaTime;
                if (m_scanTimer < ShipScanInterval) return;
                m_scanTimer = 0f;
                m_scanPrefab = 0;
                m_scanIndex = 0;
                m_scanning.Clear();
            }
            if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(m_shipPrefabs[m_scanPrefab], m_scanning, ref m_scanIndex)) return;
            m_scanPrefab++;
            m_scanIndex = 0;
            if (m_scanPrefab < m_shipPrefabs.Count) return;
            m_scanned.Clear();
            foreach (ZDO zdo in m_scanning)
                if (zdo != null && zdo.IsValid() && !m_scanned.Contains(zdo.m_uid)) m_scanned.Add(zdo.m_uid);
            m_scanning.Clear();
        }

        private void FindShipPrefabs()
        {
            m_prefabSource = ZNetScene.instance;
            m_shipPrefabs.Clear();
            m_shipHashes.Clear();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (!prefab || !prefab.GetComponent<Ship>()) continue;
                m_shipPrefabs.Add(prefab.name);
                m_shipHashes.Add(prefab.name.GetStableHashCode());
            }
            m_scanPrefab = m_shipPrefabs.Count;
            m_scanTimer = ShipScanInterval;
            m_scanned.Clear();
        }

        private static Portal ReadPortal(ZDO zdo) => new Portal
        {
            Id = zdo.m_uid,
            Position = zdo.GetPosition(),
            Rotation = zdo.GetRotation(),
            Target = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal),
        };

        // Answer layout: portal count, then per portal ZDOID, position, rotation and connection;
        // ship count, then per ship ZDOID and position.
        private static IEnumerator OnServerRequest(long sender, ZPackage request)
        {
            if (s_instance == null || ZDOMan.instance == null) yield break;
            s_instance.m_lastAsked = Time.time;
            var portals = new List<ZDO>();
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
                if (zdo != null && zdo.IsValid()) portals.Add(zdo);
            var ships = new List<ZDO>();
            foreach (ZDOID id in s_instance.m_scanned)
            {
                ZDO zdo = s_instance.Ship(id);
                if (zdo != null) ships.Add(zdo);
            }

            var answer = new ZPackage();
            answer.Write(portals.Count);
            foreach (ZDO zdo in portals)
            {
                Portal portal = ReadPortal(zdo);
                answer.Write(portal.Id);
                answer.Write(portal.Position);
                answer.Write(portal.Rotation);
                answer.Write(portal.Target);
            }
            answer.Write(ships.Count);
            foreach (ZDO zdo in ships)
            {
                answer.Write(zdo.m_uid);
                answer.Write(zdo.GetPosition());
            }
            s_rpc.SendPackage(sender, answer);
        }

        private static IEnumerator OnClientAnswer(long sender, ZPackage answer)
        {
            if (s_instance == null) yield break;
            s_instance.m_unanswered = 0;
            var portals = s_instance.m_serverPortals;
            portals.Clear();
            int count = answer.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var portal = new Portal
                {
                    Id = answer.ReadZDOID(),
                    Position = answer.ReadVector3(),
                    Rotation = answer.ReadQuaternion(),
                    Target = answer.ReadZDOID(),
                };
                portals[portal.Id] = portal;
            }
            var ships = s_instance.m_serverShips;
            ships.Clear();
            count = answer.ReadInt();
            for (int i = 0; i < count; i++)
            {
                ZDOID id = answer.ReadZDOID();
                ships[id] = answer.ReadVector3();
            }
        }
    }
}
