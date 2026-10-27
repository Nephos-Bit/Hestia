using System;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class PathSystem : MonoBehaviour
    {
        [Serializable]
        public sealed class NavigationObstacle
        {
            [SerializeField] private Vector3 center;
            [SerializeField] private Vector2 halfExtents;

            public Vector3 Center => center;
            public Vector2 HalfExtents => halfExtents;

            public NavigationObstacle(Vector3 position, Vector2 extents)
            {
                center = position;
                halfExtents = new Vector2(Mathf.Max(0.01f, extents.x), Mathf.Max(0.01f, extents.y));
            }
        }

        [Serializable]
        public sealed class NamedWaypoint
        {
            [SerializeField] private string waypointName;
            [SerializeField] private Transform anchor;
            [SerializeField] private Vector3 worldPosition;
            [SerializeField] private NPCRole[] plausibleRoles = Array.Empty<NPCRole>();
            [SerializeField] private NPCState[] plausibleStates = Array.Empty<NPCState>();

            public string Name => waypointName;
            public Vector3 Position => anchor != null ? anchor.position : worldPosition;

            public NamedWaypoint(string name, Vector3 position)
            {
                waypointName = name;
                worldPosition = position;
            }

            public NamedWaypoint(
                string name,
                Vector3 position,
                NPCRole[] roles,
                NPCState[] states)
            {
                waypointName = name;
                worldPosition = position;
                plausibleRoles = roles ?? Array.Empty<NPCRole>();
                plausibleStates = states ?? Array.Empty<NPCState>();
            }

            public void SetAnchor(Transform value)
            {
                anchor = value;
            }

            internal bool IsPlausible(NPCRole role, NPCState state)
            {
                return Contains(plausibleRoles, role) && Contains(plausibleStates, state);
            }
        }

        [Serializable]
        public sealed class NamedRoute
        {
            [SerializeField] private string routeName;
            [SerializeField] private string[] waypointNames = Array.Empty<string>();
            [SerializeField] private bool loop;
            [SerializeField] private NPCRole[] plausibleRoles = Array.Empty<NPCRole>();
            [SerializeField] private NPCState[] plausibleStates = Array.Empty<NPCState>();

            public string Name => routeName;
            public int WaypointCount => waypointNames != null ? waypointNames.Length : 0;
            public bool Loop => loop;

            public NamedRoute(string name, string[] points, bool shouldLoop = false)
            {
                routeName = name;
                waypointNames = points ?? Array.Empty<string>();
                loop = shouldLoop;
            }

            public NamedRoute(
                string name,
                string[] points,
                NPCRole[] roles,
                NPCState[] states,
                bool shouldLoop = false)
            {
                routeName = name;
                waypointNames = points ?? Array.Empty<string>();
                plausibleRoles = roles ?? Array.Empty<NPCRole>();
                plausibleStates = states ?? Array.Empty<NPCState>();
                loop = shouldLoop;
            }

            internal string GetWaypointName(int index)
            {
                return waypointNames[index];
            }

            internal bool IsPlausible(NPCRole role, NPCState state)
            {
                return Contains(plausibleRoles, role) && Contains(plausibleStates, state);
            }
        }

        [SerializeField] private NamedWaypoint[] waypoints = Array.Empty<NamedWaypoint>();
        [SerializeField] private NamedRoute[] routes = Array.Empty<NamedRoute>();
        [SerializeField] private NavigationObstacle[] navigationObstacles = Array.Empty<NavigationObstacle>();

        private NamedWaypoint[] dynamicWaypoints = Array.Empty<NamedWaypoint>();
        private NamedRoute[] dynamicRoutes = Array.Empty<NamedRoute>();
        private NavigationObstacle[] staticNavigationObstacles = Array.Empty<NavigationObstacle>();

        /// <summary>Raised after a confirmed build change modifies walkable space.</summary>
        public int NavigationRevision { get; private set; }

        public void Configure(NamedWaypoint[] namedWaypoints, NamedRoute[] namedRoutes)
        {
            waypoints = namedWaypoints ?? Array.Empty<NamedWaypoint>();
            routes = namedRoutes ?? Array.Empty<NamedRoute>();
            navigationObstacles = Array.Empty<NavigationObstacle>();
            staticNavigationObstacles = navigationObstacles;
            dynamicWaypoints = Array.Empty<NamedWaypoint>();
            dynamicRoutes = Array.Empty<NamedRoute>();
            NavigationRevision++;
        }

        public void Configure(
            NamedWaypoint[] namedWaypoints,
            NamedRoute[] namedRoutes,
            NavigationObstacle[] obstacles)
        {
            waypoints = namedWaypoints ?? Array.Empty<NamedWaypoint>();
            routes = namedRoutes ?? Array.Empty<NamedRoute>();
            navigationObstacles = obstacles ?? Array.Empty<NavigationObstacle>();
            staticNavigationObstacles = navigationObstacles;
            dynamicWaypoints = Array.Empty<NamedWaypoint>();
            dynamicRoutes = Array.Empty<NamedRoute>();
            NavigationRevision++;
        }

        /// <summary>
        /// Applies the current editable layout. Dynamic routes are intentionally short: the
        /// existing detour solver handles the last metres around props and lets the original
        /// market paths remain the primary public flow.
        /// </summary>
        public void SetDynamicLayout(
            NamedWaypoint[] interests,
            NamedRoute[] interestRoutes,
            NavigationObstacle[] dynamicObstacles)
        {
            dynamicWaypoints = interests ?? Array.Empty<NamedWaypoint>();
            dynamicRoutes = interestRoutes ?? Array.Empty<NamedRoute>();
            NavigationObstacle[] safeDynamic = dynamicObstacles ?? Array.Empty<NavigationObstacle>();
            navigationObstacles = new NavigationObstacle[staticNavigationObstacles.Length + safeDynamic.Length];
            Array.Copy(staticNavigationObstacles, navigationObstacles, staticNavigationObstacles.Length);
            Array.Copy(safeDynamic, 0, navigationObstacles, staticNavigationObstacles.Length, safeDynamic.Length);
            NavigationRevision++;
        }

        /// <summary>
        /// Keeps walking figures outside the simplified footprint of every static prop. Routes
        /// provide the large detours; this is the final contact guard against numerical drift.
        /// </summary>
        public Vector3 ConstrainToWalkableSpace(Vector3 previous, Vector3 proposed, float radius)
        {
            if (navigationObstacles == null || navigationObstacles.Length == 0)
            {
                return proposed;
            }

            float padding = Mathf.Max(0f, radius);
            Vector3 constrained = proposed;
            for (int i = 0; i < navigationObstacles.Length; i++)
            {
                NavigationObstacle obstacle = navigationObstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                float minX = obstacle.Center.x - obstacle.HalfExtents.x - padding;
                float maxX = obstacle.Center.x + obstacle.HalfExtents.x + padding;
                float minZ = obstacle.Center.z - obstacle.HalfExtents.y - padding;
                float maxZ = obstacle.Center.z + obstacle.HalfExtents.y + padding;
                if (constrained.x <= minX || constrained.x >= maxX || constrained.z <= minZ || constrained.z >= maxZ)
                {
                    continue;
                }

                float left = constrained.x - minX;
                float right = maxX - constrained.x;
                float front = constrained.z - minZ;
                float back = maxZ - constrained.z;
                float smallest = Mathf.Min(left, right, front, back);
                if (smallest == left) constrained.x = minX;
                else if (smallest == right) constrained.x = maxX;
                else if (smallest == front) constrained.z = minZ;
                else constrained.z = maxZ;
            }

            return constrained;
        }

        public Vector3 GetObstacleAvoidance(Vector3 position, float radius)
        {
            Vector3 steering = Vector3.zero;
            float range = Mathf.Max(0.05f, radius + 0.38f);
            if (navigationObstacles == null)
            {
                return steering;
            }

            for (int i = 0; i < navigationObstacles.Length; i++)
            {
                NavigationObstacle obstacle = navigationObstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                float minX = obstacle.Center.x - obstacle.HalfExtents.x;
                float maxX = obstacle.Center.x + obstacle.HalfExtents.x;
                float minZ = obstacle.Center.z - obstacle.HalfExtents.y;
                float maxZ = obstacle.Center.z + obstacle.HalfExtents.y;
                Vector3 closest = new Vector3(
                    Mathf.Clamp(position.x, minX, maxX),
                    position.y,
                    Mathf.Clamp(position.z, minZ, maxZ));
                Vector3 separation = position - closest;
                separation.y = 0f;
                float distance = separation.magnitude;
                if (distance > 0.001f && distance < range)
                {
                    steering += separation / distance * ((range - distance) / range);
                }
                else if (distance <= 0.001f)
                {
                    float left = position.x - minX;
                    float right = maxX - position.x;
                    float front = position.z - minZ;
                    float back = maxZ - position.z;
                    float smallest = Mathf.Min(left, right, front, back);
                    if (smallest == left) steering += Vector3.left;
                    else if (smallest == right) steering += Vector3.right;
                    else if (smallest == front) steering += Vector3.back;
                    else steering += Vector3.forward;
                }
            }

            return Vector3.ClampMagnitude(steering, 0.8f);
        }

        /// <summary>
        /// Finds one clear corner around the first footprint intersected by a route segment.
        /// NPCBehaviour treats the result as a temporary waypoint, preventing edge-sticking
        /// without requiring NavMesh data for this very small, procedural scene.
        /// </summary>
        public bool TryGetObstacleDetour(
            Vector3 origin,
            Vector3 target,
            float radius,
            out Vector3 detour)
        {
            float padding = Mathf.Max(0f, radius);
            const float CornerClearance = 0.08f;
            for (int i = 0; i < navigationObstacles.Length; i++)
            {
                NavigationObstacle obstacle = navigationObstacles[i];
                if (obstacle == null || !IntersectsExpandedObstacle(origin, target, obstacle, padding))
                {
                    continue;
                }

                float minX = obstacle.Center.x - obstacle.HalfExtents.x - padding - CornerClearance;
                float maxX = obstacle.Center.x + obstacle.HalfExtents.x + padding + CornerClearance;
                float minZ = obstacle.Center.z - obstacle.HalfExtents.y - padding - CornerClearance;
                float maxZ = obstacle.Center.z + obstacle.HalfExtents.y + padding + CornerClearance;
                Vector3[] corners =
                {
                    new Vector3(minX, origin.y, minZ),
                    new Vector3(minX, origin.y, maxZ),
                    new Vector3(maxX, origin.y, minZ),
                    new Vector3(maxX, origin.y, maxZ)
                };

                float bestCost = float.PositiveInfinity;
                Vector3 best = default;
                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    Vector3 candidate = corners[cornerIndex];
                    if ((candidate - origin).sqrMagnitude < 0.01f ||
                        SegmentIntersectsAnyObstacle(origin, candidate, padding))
                    {
                        continue;
                    }

                    float cost = Vector3.Distance(origin, candidate) + Vector3.Distance(candidate, target);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = candidate;
                    }
                }

                if (bestCost < float.PositiveInfinity)
                {
                    detour = best;
                    return true;
                }
            }

            detour = default;
            return false;
        }

        public bool TryGetWaypoint(string waypointName, out Vector3 position)
        {
            if (!string.IsNullOrEmpty(waypointName) && waypoints != null)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    NamedWaypoint waypoint = waypoints[i];
                    if (waypoint != null && string.Equals(waypoint.Name, waypointName, StringComparison.Ordinal))
                    {
                        position = waypoint.Position;
                        return true;
                    }
                }

                for (int i = 0; i < dynamicWaypoints.Length; i++)
                {
                    NamedWaypoint waypoint = dynamicWaypoints[i];
                    if (waypoint != null && string.Equals(waypoint.Name, waypointName, StringComparison.Ordinal))
                    {
                        position = waypoint.Position;
                        return true;
                    }
                }
            }

            position = default;
            return false;
        }

        public bool TryGetNearestWaypoint(Vector3 origin, float maximumDistance, out string waypointName, out Vector3 position)
        {
            float bestSqrDistance = maximumDistance > 0f
                ? maximumDistance * maximumDistance
                : float.PositiveInfinity;
            NamedWaypoint best = null;

            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    NamedWaypoint candidate = waypoints[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.Name))
                    {
                        continue;
                    }

                    float sqrDistance = (candidate.Position - origin).sqrMagnitude;
                    if (sqrDistance < bestSqrDistance)
                    {
                        bestSqrDistance = sqrDistance;
                        best = candidate;
                    }
                }
            }

            for (int i = 0; i < dynamicWaypoints.Length; i++)
            {
                NamedWaypoint candidate = dynamicWaypoints[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.Name)) continue;
                float sqrDistance = (candidate.Position - origin).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    best = candidate;
                }
            }

            if (best != null)
            {
                waypointName = best.Name;
                position = best.Position;
                return true;
            }

            waypointName = null;
            position = default;
            return false;
        }

        public bool TryGetPlausibleTarget(
            NPCRole role,
            NPCState state,
            Vector3 origin,
            float minimumDistance,
            float maximumDistance,
            uint variation,
            out string waypointName,
            out Vector3 position)
        {
            float minimumSqr = Mathf.Max(0f, minimumDistance * minimumDistance);
            float maximumSqr = maximumDistance > 0f
                ? maximumDistance * maximumDistance
                : float.PositiveInfinity;
            float bestScore = float.PositiveInfinity;
            NamedWaypoint best = null;

            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    NamedWaypoint candidate = waypoints[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.Name) || !candidate.IsPlausible(role, state))
                    {
                        continue;
                    }

                    float sqrDistance = (candidate.Position - origin).sqrMagnitude;
                    if (sqrDistance < minimumSqr || sqrDistance > maximumSqr)
                    {
                        continue;
                    }

                    uint hash = Mix(variation + (uint)(i + 1) * 747796405u);
                    float jitter = (hash & 1023u) * (0.00025f * Mathf.Max(1f, sqrDistance));
                    float score = sqrDistance + jitter;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            for (int i = 0; i < dynamicWaypoints.Length; i++)
            {
                NamedWaypoint candidate = dynamicWaypoints[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.Name) || !candidate.IsPlausible(role, state))
                {
                    continue;
                }

                float sqrDistance = (candidate.Position - origin).sqrMagnitude;
                if (sqrDistance < minimumSqr || sqrDistance > maximumSqr) continue;
                uint hash = Mix(variation + (uint)(i + 101) * 747796405u);
                float jitter = (hash & 1023u) * (0.00025f * Mathf.Max(1f, sqrDistance));
                float score = sqrDistance + jitter;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
            {
                waypointName = best.Name;
                position = best.Position;
                return true;
            }

            waypointName = null;
            position = default;
            return false;
        }

        public int GetRouteWaypointCount(string routeName)
        {
            NamedRoute route = FindRoute(routeName);
            return route != null ? route.WaypointCount : 0;
        }

        public bool GetRouteLoops(string routeName)
        {
            NamedRoute route = FindRoute(routeName);
            return route != null && route.Loop;
        }

        public bool TryGetRouteWaypoint(string routeName, int index, out Vector3 position)
        {
            NamedRoute route = FindRoute(routeName);
            if (route != null && index >= 0 && index < route.WaypointCount)
            {
                return TryGetWaypoint(route.GetWaypointName(index), out position);
            }

            position = default;
            return false;
        }

        public string GetPlausibleRouteName(NPCRole role, NPCState state, uint variation)
        {
            string selected = null;
            uint best = uint.MaxValue;

            if (routes != null)
            {
                for (int i = 0; i < routes.Length; i++)
                {
                    NamedRoute route = routes[i];
                    if (route == null || route.WaypointCount == 0 || string.IsNullOrEmpty(route.Name) ||
                        !route.IsPlausible(role, state))
                    {
                        continue;
                    }

                    uint score = Mix(variation ^ ((uint)(i + 1) * 277803737u));
                    if (score < best)
                    {
                        best = score;
                        selected = route.Name;
                    }
                }
            }

            for (int i = 0; i < dynamicRoutes.Length; i++)
            {
                NamedRoute route = dynamicRoutes[i];
                if (route == null || route.WaypointCount == 0 || string.IsNullOrEmpty(route.Name) ||
                    !route.IsPlausible(role, state))
                {
                    continue;
                }

                uint score = Mix(variation ^ ((uint)(i + 101) * 277803737u));
                if (score < best)
                {
                    best = score;
                    selected = route.Name;
                }
            }

            return selected;
        }

        /// <summary>Chooses only layout-provided activity routes, or null when none fit.</summary>
        public string GetDynamicPlausibleRouteName(NPCRole role, NPCState state, uint variation)
        {
            string selected = null;
            uint best = uint.MaxValue;
            for (int i = 0; i < dynamicRoutes.Length; i++)
            {
                NamedRoute route = dynamicRoutes[i];
                if (route == null || route.WaypointCount == 0 || string.IsNullOrEmpty(route.Name) ||
                    !route.IsPlausible(role, state))
                {
                    continue;
                }
                uint score = Mix(variation ^ ((uint)(i + 401) * 277803737u));
                if (score < best)
                {
                    best = score;
                    selected = route.Name;
                }
            }
            return selected;
        }

        public string GetExitRoute(Vector3 origin)
        {
            return origin.x <= 0f ? "WalkThrough" : "WalkThroughReverse";
        }

        private NamedRoute FindRoute(string routeName)
        {
            if (!string.IsNullOrEmpty(routeName) && routes != null)
            {
                for (int i = 0; i < routes.Length; i++)
                {
                    NamedRoute route = routes[i];
                    if (route != null && string.Equals(route.Name, routeName, StringComparison.Ordinal))
                    {
                        return route;
                    }
                }

                for (int i = 0; i < dynamicRoutes.Length; i++)
                {
                    NamedRoute route = dynamicRoutes[i];
                    if (route != null && string.Equals(route.Name, routeName, StringComparison.Ordinal))
                    {
                        return route;
                    }
                }
            }

            return null;
        }

        private bool SegmentIntersectsAnyObstacle(Vector3 origin, Vector3 target, float padding)
        {
            for (int i = 0; i < navigationObstacles.Length; i++)
            {
                NavigationObstacle obstacle = navigationObstacles[i];
                if (obstacle != null && IntersectsExpandedObstacle(origin, target, obstacle, padding))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsExpandedObstacle(
            Vector3 origin,
            Vector3 target,
            NavigationObstacle obstacle,
            float padding)
        {
            float minX = obstacle.Center.x - obstacle.HalfExtents.x - padding;
            float maxX = obstacle.Center.x + obstacle.HalfExtents.x + padding;
            float minZ = obstacle.Center.z - obstacle.HalfExtents.y - padding;
            float maxZ = obstacle.Center.z + obstacle.HalfExtents.y + padding;
            return IntersectsAxisAlignedRectangle(origin, target, minX, maxX, minZ, maxZ);
        }

        private static bool IntersectsAxisAlignedRectangle(
            Vector3 origin,
            Vector3 target,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            float enter = 0f;
            float exit = 1f;
            if (!ClipAxis(origin.x, target.x - origin.x, minX, maxX, ref enter, ref exit) ||
                !ClipAxis(origin.z, target.z - origin.z, minZ, maxZ, ref enter, ref exit))
            {
                return false;
            }

            // Contact at the starting point is not a new obstruction; it permits an NPC that
            // has just rounded a corner to continue towards its original target.
            return exit > 0.001f && enter < 0.999f;
        }

        private static bool ClipAxis(
            float origin,
            float direction,
            float minimum,
            float maximum,
            ref float enter,
            ref float exit)
        {
            if (Mathf.Abs(direction) < 0.0001f)
            {
                return origin >= minimum && origin <= maximum;
            }

            float first = (minimum - origin) / direction;
            float second = (maximum - origin) / direction;
            if (first > second)
            {
                float temporary = first;
                first = second;
                second = temporary;
            }

            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return enter <= exit;
        }

        private static bool Contains(NPCRole[] values, NPCRole value)
        {
            if (values == null || values.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(NPCState[] values, NPCState value)
        {
            if (values == null || values.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
