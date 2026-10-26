using System;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class PathSystem : MonoBehaviour
    {
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

        public void Configure(NamedWaypoint[] namedWaypoints, NamedRoute[] namedRoutes)
        {
            waypoints = namedWaypoints ?? Array.Empty<NamedWaypoint>();
            routes = namedRoutes ?? Array.Empty<NamedRoute>();
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

            return selected;
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
            }

            return null;
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
