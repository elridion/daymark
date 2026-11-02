using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Daymark
{
    /// <summary>
    /// The night wisp that leads a tired player home along a route from <see cref="RouteFinder"/>:
    /// ahead of them over land, to a ship (circling its mast until they board), along a
    /// sea route, and to a portal (tracing a ring in the portal's own plane on the player's side).
    /// It waits until the player looks down from the sky, re-plans when they stray or change between
    /// foot and ship, floats over the bed once the player is home (leading again if they leave)
    /// and fades only at dawn. The wisp itself is a local copy of the
    /// Wisplight's light ball stripped to its lights, particles and meshes: the original is a
    /// networked object that also clears Mistlands mist, so it is copied with ZNetView init
    /// disabled and those parts removed.
    /// </summary>
    internal class WispGuide
    {
        // The wisp waits until the camera looks no higher than this (sine of about 20 degrees), at
        // most WaitTime seconds, since the player is still looking at the sky when it appears.
        private const float LookDownLimit = 0.35f;
        private const float WaitTime = 10f;
        // How far ahead of the player it flies on foot and at sea, and how fast it may move.
        private const float WalkLead = 12f;
        private const float SailLead = 35f;
        private const float WalkSpeed = 10f;
        private const float SailSpeed = 25f;
        // Height above the ground or the sea it floats at, and how quickly it follows the ground.
        private const float HoverHeight = 2f;
        private const float SeaHoverHeight = 4f;
        private const float HeightFollow = 3f;
        private const float BobDistance = 0.15f;
        // Off the route by more than this (on foot, at sea) means the player went their own way.
        private const float WalkStray = 40f;
        private const float SailStray = 120f;
        // Seconds between route searches, however often the player strays.
        private const float ReplanInterval = 5f;
        // Close enough to the bed to be home, and far enough from it while the wisp hovers there
        // to need leading back.
        private const float ArriveDistance = 15f;
        private const float LeaveDistance = 60f;
        // Home: resting low over the bed, rising and sinking between 0.5 and 1 m (about 16 s a
        // cycle) while it wanders slowly over the bed's surface, this far either way along its
        // length and across its width. The three speeds (radians per second) share no common
        // beat, so the swirl never repeats. It drifts to that point at HomeDrift.
        private const float HomeHeight = 0.75f;
        private const float HomeRise = 0.25f;
        private const float HomeRiseSpeed = 0.4f;
        private const float HomeLength = 0.6f;
        private const float HomeLengthSpeed = 0.13f;
        private const float HomeWidth = 0.3f;
        private const float HomeWidthSpeed = 0.21f;
        private const float HomeDrift = 3f;
        // Further than this from the player (or, at home, from the bed) the wisp was left behind by
        // a teleport. Well beyond the farthest it ever flies from them: a ship or portal it waits at.
        private const float LostDistance = 150f;
        // Near the end of a leg leading to a ship or portal, the wisp goes there and circles.
        private const float HandOverDistance = 25f;
        // Rings: around the mast, and in the portal's plane in front of it.
        private const float MastRing = 1.5f;
        private const float MastHeight = 4f;
        private const float PortalRing = 1.1f;
        private const float PortalRingHeight = 1.6f;
        private const float PortalRingOffset = 0.8f;
        private const float RingSpeed = 2.5f;
        // A ship or portal more than this far from where the route placed it is another one.
        private const float FindRange = 25f;
        // A jump this far in one frame is a teleport.
        private const float TeleportJump = 100f;
        // Lights fade over FadeTime; emitted particles get LingerTime to die out before removal.
        private const float FadeTime = 1.5f;
        private const float LingerTime = 2f;
        // The Wisplight lights up a wide area; a guide should only glow.
        private const float LightScale = 0.6f;

        private const string WisplightItem = "Demister";

        // Everything else on the light ball (Demister, its force field, ZNetView, sounds) goes.
        private static readonly Type[] s_keptTypes =
        {
            typeof(Transform), typeof(ParticleSystem), typeof(ParticleSystemRenderer), typeof(Light),
            typeof(MeshFilter), typeof(MeshRenderer), typeof(TrailRenderer),
        };

        private enum State
        {
            Waiting,
            Planning,
            Leading,
            AtShip,
            AtPortal,
            Home,
            Fading,
        }

        private readonly WorldDirectory m_directory;
        private NavGrid m_grid;
        private WorldGenerator m_gridSource;
        private RouteFinder m_finder;

        private GameObject m_template;
        // The ObjectDB that had no light ball, so the warning and the search happen once per world.
        private ObjectDB m_templateMissingIn;
        private GameObject m_wisp;
        private Light[] m_lights;
        private float[] m_baseIntensities;
        private ParticleSystem[] m_particles;

        private State m_state;
        private float m_timer;
        private float m_replanTimer;
        private Vector3 m_goal;
        private Vector3 m_lastPlayerPosition;
        private List<Leg> m_route;
        private int m_leg;
        private int m_segment;
        private Vector3 m_anchor;
        private float m_ringAngle;
        private Ship m_ship;
        private TeleportWorld m_portal;
        private PortalLink m_portalLink;
        private float m_lookupTimer;
        private bool m_particlesStopped;
        // The bed's length, flat, once its piece is found near the goal; zero until then.
        private Vector3 m_bedAxis;
        // The current route is only a straight line because the world map was not built yet.
        private bool m_provisional;
        private bool m_wasTeleporting;
        // Numbers searches, so only the newest one's result is taken.
        private int m_search;
        private bool m_pendingPlan;
        // Written by the search thread, read on the main thread.
        private volatile Found m_found;
        private volatile bool m_searching;

        private class Found
        {
            public int Search;
            public List<Leg> Route;
        }

        public WispGuide(WorldDirectory directory)
        {
            m_directory = directory;
        }

        public bool Active => m_wisp;

        public Vector3 Goal => m_goal;

        public void Start(Player player, Vector3 goal)
        {
            Remove();
            GameObject template = Template();
            if (!template) return;

            m_goal = goal;
            m_bedAxis = Vector3.zero;
            m_wasTeleporting = player.IsTeleporting();
            // Night falling on a player at home finds the wisp already over the bed.
            bool home = !player.InInterior() && Flat(goal - player.transform.position).magnitude < LeaveDistance;
            Vector3 spawn = home ? HomePoint() : player.transform.position + Vector3.up * HoverHeight;
            m_wisp = Object.Instantiate(template, spawn, Quaternion.identity);
            m_wisp.name = "DaymarkWisp";
            m_wisp.SetActive(true);
            m_lights = m_wisp.GetComponentsInChildren<Light>();
            m_baseIntensities = new float[m_lights.Length];
            for (int i = 0; i < m_lights.Length; i++)
            {
                m_lights[i].intensity *= LightScale;
                m_baseIntensities[i] = m_lights[i].intensity;
            }
            m_particles = m_wisp.GetComponentsInChildren<ParticleSystem>();
            m_route = null;
            m_lastPlayerPosition = player.transform.position;
            if (home)
            {
                SetState(State.Home);
                return;
            }
            SetState(State.Waiting);
            Plan(player);
        }

        // Turning the Wisp setting off sends the wisp away, fading as at dawn.
        public void Dismiss()
        {
            if (m_wisp && m_state != State.Fading) SetState(State.Fading);
        }

        // A bed claimed during the night becomes the new goal at once.
        public void SetGoal(Player player, Vector3 goal)
        {
            m_goal = goal;
            m_bedAxis = Vector3.zero;
            if (m_state != State.Fading) Restart(player);
        }

        public void Update()
        {
            KeepGrid();
            if (!m_wisp) return;
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead())
            {
                Remove();
                return;
            }

            // Dungeons sit 5 km up, outside the world map: the wisp waits hidden until the player
            // comes out, then plans from wherever that is.
            Vector3 playerPosition = player.transform.position;
            if (player.InInterior())
            {
                if (m_wisp.activeSelf) m_wisp.SetActive(false);
                m_lastPlayerPosition = playerPosition;
                return;
            }
            if (!m_wisp.activeSelf)
            {
                m_wisp.SetActive(true);
                m_wisp.transform.position = Beside(player);
                if (m_state != State.Fading && m_state != State.Home) Restart(player);
            }

            float dt = Time.deltaTime;
            m_timer += dt;
            m_replanTimer += dt;
            // A portal trip ends when vanilla's teleport does; the jump test also catches other
            // teleports (beds, console), however short the distance.
            bool teleporting = player.IsTeleporting();
            bool teleported = (m_wasTeleporting && !teleporting)
                || (playerPosition - m_lastPlayerPosition).sqrMagnitude > TeleportJump * TeleportJump;
            m_wasTeleporting = teleporting;
            m_lastPlayerPosition = playerPosition;
            if (m_pendingPlan && !m_searching) Plan(player);

            if (m_state != State.Fading && !EnvMan.IsNight()) SetState(State.Fading);
            float fromHome = Flat(m_goal - playerPosition).magnitude;
            if (m_state != State.Fading && m_state != State.Home && fromHome < ArriveDistance) SetState(State.Home);
            // Leaving home again at night: the wisp leads the way back.
            if (m_state == State.Home && fromHome > LeaveDistance) Restart(player);
            if (m_provisional && m_grid != null && m_grid.Ready) Replan(player, true);
            TakeRoute();

            Vector3 position = m_wisp.transform.position;
            // A teleport, through a portal most often, leaves the wisp where the player was, often
            // kilometres away; it would trail after them at flying speed, or at home pace when the
            // portal led home. It jumps to them instead, whatever state it is in.
            if (m_state != State.Fading)
            {
                bool home = m_state == State.Home;
                Vector3 near = home ? HomePoint() : playerPosition;
                if ((position - near).sqrMagnitude > LostDistance * LostDistance)
                    position = home ? HomePoint() : Beside(player);
            }
            switch (m_state)
            {
                case State.Waiting:
                case State.Planning:
                    position = Vector3.MoveTowards(position, Beside(player), WalkSpeed * 2f * dt);
                    if (m_state == State.Waiting && (LookingDown() || m_timer >= WaitTime)) SetState(State.Planning);
                    if (m_state == State.Planning && m_route != null) FollowRoute(player);
                    break;
                case State.Leading:
                    position = Lead(player, position, dt, teleported);
                    break;
                case State.AtShip:
                    position = CircleShip(player, position, dt);
                    break;
                case State.AtPortal:
                    position = CirclePortal(player, position, dt, teleported);
                    break;
                case State.Home:
                    // Rests over the bed until dawn.
                    if (m_bedAxis == Vector3.zero && Lookup()) FindBed();
                    position = Vector3.MoveTowards(position, HomePoint() + Swirl(), HomeDrift * dt);
                    break;
                case State.Fading:
                    Fade(Mathf.Clamp01(1f - m_timer / FadeTime));
                    if (m_timer >= FadeTime + LingerTime)
                    {
                        Remove();
                        return;
                    }
                    break;
            }

            m_wisp.transform.position = position + Bob();
        }

        // In flight the wisp wobbles quickly in every direction; over the bed Swirl moves it instead.
        private Vector3 Bob()
        {
            if (m_state == State.Home) return Vector3.zero;
            float bob = Time.time * 2f;
            return new Vector3(Mathf.Sin(bob * 1.3f), Mathf.Sin(bob), Mathf.Cos(bob * 1.7f)) * BobDistance;
        }

        // The slow wander over the bed's surface, relative to HomePoint. Until the bed piece is found
        // its length is unknown, so the wisp keeps within the width both ways.
        private Vector3 Swirl()
        {
            float time = Time.time;
            Vector3 length = m_bedAxis == Vector3.zero ? Vector3.forward : m_bedAxis;
            Vector3 width = Vector3.Cross(Vector3.up, length);
            float reach = m_bedAxis == Vector3.zero ? HomeWidth : HomeLength;
            return length * (Mathf.Sin(time * HomeLengthSpeed) * reach)
                + width * (Mathf.Sin(time * HomeWidthSpeed) * HomeWidth)
                + Vector3.up * (Mathf.Sin(time * HomeRiseSpeed) * HomeRise);
        }

        private Vector3 HomePoint() => m_goal + Vector3.up * HomeHeight;

        // The goal is the bed's spawn point, where the sleeper lies facing along the bed. Searched
        // once a second until found, since the bed's area may not be loaded yet; without it the
        // wisp only bobs in place.
        private void FindBed()
        {
            foreach (Collider collider in Physics.OverlapSphere(m_goal, 3f))
            {
                var bed = collider.GetComponentInParent<Bed>();
                if (!bed || !bed.m_spawnPoint) continue;
                if ((bed.m_spawnPoint.position - m_goal).sqrMagnitude > 1f) continue;
                m_bedAxis = Flat(bed.m_spawnPoint.forward).normalized;
                return;
            }
        }

        // On foot or at sea: fly ahead of the player along the current leg, handing over to the ship
        // or portal circling near the leg's end.
        private Vector3 Lead(Player player, Vector3 position, float dt, bool teleported)
        {
            if (m_leg >= m_route.Count)
            {
                Restart(player);
                return position;
            }
            Leg leg = m_route[m_leg];
            bool sailing = Sailing(player);
            bool atSea = leg.Kind == LegKind.Sail;
            // Boarding a ship or a portal trip changes the whole way home: search again at once.
            if (teleported || (leg.Kind == LegKind.Walk && sailing))
            {
                Restart(player);
                return position;
            }
            if (atSea && !sailing)
            {
                // Stepped off at the landing: on to the next leg. Anywhere else: they left the ship.
                if (NearEnd(leg, player, HandOverDistance)) NextLeg(player);
                else Restart(player);
                return position;
            }

            float stray = Closest(leg.Points, player.transform.position, out float along);
            if (leg.Kind != LegKind.Straight && stray > (atSea ? SailStray : WalkStray)) Replan(player, false);

            // On foot the next leg (a ship or a portal) takes over near the end; at sea the wisp
            // waits over the landing until the player steps off.
            if (!atSea && NearEnd(leg, player, HandOverDistance) && m_leg + 1 < m_route.Count)
            {
                NextLeg(player);
                return position;
            }
            Vector3 target = PointAlong(leg.Points, along + (atSea ? SailLead : WalkLead));

            Vector3 next = Vector3.MoveTowards(Flat(position), Flat(target), (atSea ? SailSpeed : WalkSpeed) * dt);
            float floor = atSea ? WaterLevel() + SeaHoverHeight - HoverHeight : FloorHeight(next);
            float y = Mathf.Lerp(position.y, floor + HoverHeight, Mathf.Clamp01(HeightFollow * dt));
            return new Vector3(next.x, y, next.z);
        }

        private Vector3 CircleShip(Player player, Vector3 position, float dt)
        {
            // Aboard any ship, this one or another, the way home is by sea now.
            if (Sailing(player))
            {
                Restart(player);
                return position;
            }
            if (Lookup())
            {
                m_ship = FindShip(m_anchor);
                if (!m_ship && NearAndLoaded(player, m_anchor))
                {
                    m_directory.MarkShipMissing(m_anchor);
                    Restart(player);
                    return position;
                }
            }
            if (WalkedOff(player)) Replan(player, false);
            Ship ship = m_ship;
            if (!ship) return Vector3.MoveTowards(position, m_anchor + Vector3.up * MastHeight, WalkSpeed * dt);

            Transform mast = ship.m_mastObject ? ship.m_mastObject.transform : ship.transform;
            Vector3 centre = mast.position + Vector3.up * MastHeight;
            m_ringAngle += RingSpeed * dt;
            Vector3 ring = centre + new Vector3(Mathf.Cos(m_ringAngle), 0f, Mathf.Sin(m_ringAngle)) * MastRing;
            return Vector3.MoveTowards(position, ring, WalkSpeed * 2f * dt);
        }

        // A vertical ring in the portal's own plane, a little in front of it on the player's side,
        // as if outlining the doorway to step through.
        private Vector3 CirclePortal(Player player, Vector3 position, float dt, bool teleported)
        {
            if (teleported)
            {
                // The wisp follows through the portal and waits beside the player for the new route.
                Restart(player);
                return Beside(player);
            }
            if (Lookup())
            {
                m_portal = FindPortal(m_anchor, m_portalLink.EntryId);
                // Gone, or retagged so it leads somewhere else now: find another way.
                if ((!m_portal && NearAndLoaded(player, m_anchor)) || (m_portal && LeadsElsewhere(m_portal)))
                {
                    Restart(player);
                    return position;
                }
            }
            if (WalkedOff(player)) Replan(player, false);
            TeleportWorld portal = m_portal;
            if (!portal) return Vector3.MoveTowards(position, m_anchor + Vector3.up * PortalRingHeight, WalkSpeed * dt);

            Transform frame = portal.transform;
            float side = Vector3.Dot(player.transform.position - frame.position, frame.forward) >= 0f ? 1f : -1f;
            Vector3 centre = frame.position + Vector3.up * PortalRingHeight + frame.forward * (side * PortalRingOffset);
            m_ringAngle += RingSpeed * dt;
            Vector3 ring = centre + (frame.right * Mathf.Cos(m_ringAngle) + frame.up * Mathf.Sin(m_ringAngle)) * PortalRing;
            return Vector3.MoveTowards(position, ring, WalkSpeed * 2f * dt);
        }

        private void NextLeg(Player player)
        {
            m_leg++;
            m_segment = 0;
            if (m_leg >= m_route.Count)
            {
                Restart(player);
                return;
            }
            EnterLeg(player);
        }

        // Drops the current route and waits beside the player for a new one: after boarding, a
        // portal trip or anything else that makes the old route wrong from here on.
        private void Restart(Player player)
        {
            m_route = null;
            Replan(player, true);
            SetState(State.Planning);
        }

        // Circling a ship or portal while the player walks away from it: they chose another way.
        private bool WalkedOff(Player player) =>
            Flat(m_anchor - player.transform.position).magnitude > HandOverDistance + WalkStray;

        // A new route begins with its first leg, or with circling its ship or portal.
        private void FollowRoute(Player player)
        {
            m_leg = 0;
            m_segment = 0;
            EnterLeg(player);
        }

        private void EnterLeg(Player player)
        {
            Leg leg = m_route[m_leg];
            m_ship = null;
            m_portal = null;
            m_lookupTimer = float.MaxValue;
            if (leg.Kind == LegKind.Sail && !Sailing(player))
            {
                m_anchor = leg.Points[0];
                SetState(State.AtShip);
            }
            else if (leg.Kind == LegKind.Portal)
            {
                m_anchor = leg.Points[0];
                m_portalLink = leg.Link;
                SetState(State.AtPortal);
            }
            else
            {
                SetState(State.Leading);
            }
        }

        // Starts a search unless (when not forced) one ran moments ago; the wisp carries on with the
        // old route until the new one arrives.
        private void Replan(Player player, bool force)
        {
            if (!force && m_replanTimer < ReplanInterval) return;
            Plan(player);
        }

        // One search runs at a time; asking during one queues a fresh search for when it ends, and
        // the running one's result is dropped as stale.
        private void Plan(Player player)
        {
            int search = ++m_search;
            m_found = null;
            if (m_searching)
            {
                m_pendingPlan = true;
                return;
            }
            m_pendingPlan = false;
            m_replanTimer = 0f;
            var request = new RouteRequest
            {
                Start = player.transform.position,
                OnShip = Sailing(player),
                Goal = m_goal,
            };
            m_directory.FillRequest(request);

            m_provisional = m_grid == null || !m_grid.Ready;
            if (m_provisional)
            {
                m_found = new Found { Search = search, Route = Straight(request) };
                return;
            }
            if (m_finder == null) m_finder = new RouteFinder(m_grid);
            RouteFinder finder = m_finder;
            m_searching = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<Leg> route = null;
                try
                {
                    route = finder.Find(request);
                }
                catch (Exception e)
                {
                    Jotunn.Logger.LogWarning($"The wisp's route search failed: {e.Message}");
                }
                m_found = new Found { Search = search, Route = route ?? Straight(request) };
                m_searching = false;
            });
        }

        // The newest search's result replaces the route; results of searches asked before a newer
        // one (another death, world or situation) are dropped. While waiting for the look down the
        // route is only kept.
        private void TakeRoute()
        {
            Found found = m_found;
            if (found == null) return;
            m_found = null;
            if (found.Search != m_search) return;
            m_route = found.Route;
            if (m_state == State.Leading || m_state == State.AtShip || m_state == State.AtPortal)
                FollowRoute(Player.m_localPlayer);
        }

        private static bool Sailing(Player player)
        {
            Ship ship = Ship.GetLocalShip();
            return ship && ship.IsPlayerInBoat(player);
        }

        // Ship and portal lookups run once a second, not every frame.
        private bool Lookup()
        {
            m_lookupTimer += Time.deltaTime;
            if (m_lookupTimer < 1f) return false;
            m_lookupTimer = 0f;
            return true;
        }

        private static List<Leg> Straight(RouteRequest request)
        {
            var leg = new Leg { Kind = LegKind.Straight };
            leg.Points.Add(request.Start);
            leg.Points.Add(request.Goal);
            return new List<Leg> { leg };
        }

        private void SetState(State state)
        {
            m_state = state;
            m_timer = 0f;
            m_particlesStopped = false;
        }

        // The world map for route finding is built once per world in the background, as soon as a
        // player is in a world without a map, so it is ready by nightfall.
        private void KeepGrid()
        {
            WorldGenerator generator = WorldGenerator.instance;
            bool wanted = generator != null && Game.m_noMap && Player.m_localPlayer && ZoneSystem.instance
                && DaymarkPlugin.Wisp.Value;
            if (!wanted || generator == m_gridSource) return;
            m_grid?.Cancel();
            m_gridSource = generator;
            m_finder = null;
            m_grid = NavGrid.StartBuild(generator, ZoneSystem.instance.m_waterLevel);
        }

        private bool LookingDown()
        {
            Camera camera = Utils.GetMainCamera();
            return camera && camera.transform.forward.y < LookDownLimit;
        }

        private Vector3 Beside(Player player)
        {
            Vector3 toGoal = Flat(m_goal - player.transform.position).normalized;
            return player.transform.position + toGoal * 0.8f + Vector3.up * HoverHeight;
        }

        private bool NearEnd(Leg leg, Player player, float distance) =>
            Flat(leg.Points[leg.Points.Count - 1] - player.transform.position).magnitude < distance;

        // Distance from the point to the polyline, and how far along the polyline the closest point
        // lies. Searches from the last known segment onwards, so loops in the route don't confuse it.
        private float Closest(List<Vector3> points, Vector3 point, out float along)
        {
            float best = float.MaxValue;
            along = 0f;
            float travelled = 0f;
            int bestSegment = m_segment;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = Flat(points[i]);
                Vector3 b = Flat(points[i + 1]);
                Vector3 ab = b - a;
                float length = ab.magnitude;
                if (i >= m_segment)
                {
                    float t = length > 0f ? Mathf.Clamp01(Vector3.Dot(Flat(point) - a, ab) / (length * length)) : 0f;
                    float distance = Vector3.Distance(Flat(point), a + ab * t);
                    if (distance < best)
                    {
                        best = distance;
                        along = travelled + length * t;
                        bestSegment = i;
                    }
                }
                travelled += length;
            }
            m_segment = bestSegment;
            return points.Count < 2 ? Flat(points[0] - point).magnitude : best;
        }

        private static Vector3 PointAlong(List<Vector3> points, float distance)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                float length = Flat(points[i + 1] - points[i]).magnitude;
                if (distance <= length) return Vector3.Lerp(points[i], points[i + 1], length > 0f ? distance / length : 0f);
                distance -= length;
            }
            return points[points.Count - 1];
        }

        private static Ship FindShip(Vector3 near)
        {
            Ship best = null;
            float bestDistance = FindRange * FindRange;
            foreach (var updater in Ship.Instances)
            {
                var ship = updater as Ship;
                if (!ship) continue;
                float distance = (ship.transform.position - near).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = ship;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // The planned portal by its ZDO, since a hub has several standing side by side; it runs only
        // once a second while the wisp waits at one.
        private static TeleportWorld FindPortal(Vector3 near, ZDOID id)
        {
            foreach (Collider collider in Physics.OverlapSphere(near, 5f))
            {
                var portal = collider.GetComponentInParent<TeleportWorld>();
                if (portal && ZdoOf(portal)?.m_uid == id) return portal;
            }
            return null;
        }

        // Retagging first disconnects a portal and the server reconnects it within seconds, so no
        // connection yet means wait, not elsewhere.
        private bool LeadsElsewhere(TeleportWorld portal)
        {
            ZDO zdo = ZdoOf(portal);
            if (zdo == null) return false;
            ZDOID target = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
            return !target.IsNone() && target != m_portalLink.ExitId;
        }

        private static ZDO ZdoOf(Component component)
        {
            var view = component.GetComponent<ZNetView>();
            return view && view.IsValid() ? view.GetZDO() : null;
        }

        private static bool NearAndLoaded(Player player, Vector3 point) =>
            Flat(point - player.transform.position).magnitude < FindRange * 2f && ZNetScene.instance && ZNetScene.instance.IsAreaReady(point);

        private void Fade(float amount)
        {
            for (int i = 0; i < m_lights.Length; i++)
                if (m_lights[i]) m_lights[i].intensity = m_baseIntensities[i] * amount;
            if (m_particlesStopped) return;
            m_particlesStopped = true;
            foreach (var particles in m_particles)
                if (particles) particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Remove()
        {
            if (m_wisp) Object.Destroy(m_wisp);
            m_wisp = null;
            m_route = null;
            // Whatever search is still running belongs to this wisp; its result must not reach the next.
            m_search++;
            m_found = null;
            m_pendingPlan = false;
        }

        private static float WaterLevel() => ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : 30f;

        // Terrain height, but never below the sea, so the wisp skims the waves instead of the seabed.
        private static float FloorHeight(Vector3 point)
        {
            if (!ZoneSystem.instance) return point.y;
            return Mathf.Max(ZoneSystem.instance.GetGroundHeight(point), ZoneSystem.instance.m_waterLevel);
        }

        private static Vector3 Flat(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);

        // An inactive, stripped copy of the light ball, kept for the session's scene. Logging out
        // unloads the scene and destroys it, so it is rebuilt on demand.
        private GameObject Template()
        {
            if (m_template) return m_template;
            if (!ObjectDB.instance || m_templateMissingIn == ObjectDB.instance) return null;
            GameObject ball = FindLightBall();
            if (!ball)
            {
                m_templateMissingIn = ObjectDB.instance;
                Jotunn.Logger.LogWarning("Wisplight light ball not found; no wisp will appear.");
                return null;
            }

            ZNetView.m_forceDisableInit = true;
            try
            {
                m_template = Object.Instantiate(ball);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
            }
            m_template.name = "DaymarkWispTemplate";
            m_template.SetActive(false);
            // Unity refuses to destroy a component another one requires, so strip in a few passes.
            for (int pass = 0; pass < 3; pass++)
                foreach (var component in m_template.GetComponentsInChildren<Component>(true))
                    if (component && Array.IndexOf(s_keptTypes, component.GetType()) < 0)
                        Object.DestroyImmediate(component);
            return m_template;
        }

        private static GameObject FindLightBall()
        {
            if (!ObjectDB.instance) return null;
            GameObject item = ObjectDB.instance.GetItemPrefab(WisplightItem);
            ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
            var demister = drop ? drop.m_itemData.m_shared.m_equipStatusEffect as SE_Demister : null;
            return demister ? demister.m_ballPrefab : null;
        }
    }
}
