using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class NPCDirector : MonoBehaviour
    {
        private const int AbsolutePoolLimit = 6;

        [Header("Runtime references")]
        [SerializeField] private NPCFactory factory;
        [SerializeField] private PathSystem pathSystem;
        [SerializeField] private TimeOfDayController timeOfDayController;

        [Header("Pool and scheduling")]
        [SerializeField, Range(1, AbsolutePoolLimit)] private int maxPoolSize = AbsolutePoolLimit;
        [SerializeField] private int deterministicSeed;
        [SerializeField, Range(0f, 0.75f)] private float quietPeriodChance = 0.16f;

        [Header("Preferred named routes")]
        [SerializeField] private string walkThroughRoute = "WalkThrough";
        [SerializeField] private string walkThroughReverseRoute = "WalkThroughReverse";
        [SerializeField] private string marketStallsRoute = "MarketStalls";
        [SerializeField] private string marketStallsReverseRoute = "MarketStallsReverse";
        [SerializeField] private string deliveryRoute = "Delivery";
        [SerializeField] private string cleaningRoute = "Cleaning";

        private readonly NPCBehaviour[] pool = new NPCBehaviour[AbsolutePoolLimit];
        private int builtPoolSize;
        private float nextActivityAt;
        private uint randomState;

        public int PoolSize => builtPoolSize;

        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < builtPoolSize; i++)
                {
                    if (pool[i] != null && pool[i].IsActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private void Awake()
        {
            int seed = deterministicSeed != 0
                ? deterministicSeed
                : unchecked(Environment.TickCount ^ GetInstanceID());
            randomState = unchecked((uint)seed);
            if (randomState == 0u)
            {
                randomState = 0x6d2b79f5u;
            }

            BuildPoolIfReady();
            ScheduleFirstActivity();
        }

        private void Update()
        {
            BuildPoolIfReady();
            if (builtPoolSize == 0 || pathSystem == null || Time.time < nextActivityAt)
            {
                return;
            }

            float hour = ReadCurrentHour();
            bool deepNight = IsDeepNight(hour);
            if (TryStartEncounter())
            {
                nextActivityAt = Time.time + RandomRange(7f, 17f);
                return;
            }

            NPCBehaviour npc = FindInactiveNPC();
            if (npc == null)
            {
                nextActivityAt = Time.time + RandomRange(3f, 7f);
                return;
            }

            NPCRole role = ChooseRole(hour, deepNight);
            StartMicroEvent(npc, role);
            ScheduleFollowingActivity(hour, deepNight);
        }

        private void OnValidate()
        {
            maxPoolSize = Mathf.Clamp(maxPoolSize, 1, AbsolutePoolLimit);
        }

        public void SetRuntimeReferences(
            NPCFactory npcFactory,
            PathSystem navigation,
            TimeOfDayController clock)
        {
            factory = npcFactory;
            pathSystem = navigation;
            timeOfDayController = clock;
            BuildPoolIfReady();
            if (ActiveCount == 0)
            {
                ScheduleFirstActivity();
            }
        }

        public void SetTimeOfDayController(TimeOfDayController clock)
        {
            timeOfDayController = clock;
            if (ActiveCount == 0)
            {
                ScheduleFirstActivity();
            }
        }

        public void RequestActivitySoon(float maximumDelay = 1f)
        {
            nextActivityAt = Mathf.Min(nextActivityAt, Time.time + Mathf.Max(0f, maximumDelay));
        }

        public void SetDeterministicSeed(int seed)
        {
            deterministicSeed = seed;
            randomState = unchecked((uint)seed);
            if (randomState == 0u)
            {
                randomState = 0x6d2b79f5u;
            }
            ScheduleFirstActivity();
        }

        private void BuildPoolIfReady()
        {
            if (factory == null)
            {
                return;
            }

            int requestedSize = Mathf.Clamp(maxPoolSize, 1, AbsolutePoolLimit);
            while (builtPoolSize < requestedSize)
            {
                pool[builtPoolSize] = factory.BuildNPC(transform);
                builtPoolSize++;
            }
        }

        private void StartMicroEvent(NPCBehaviour npc, NPCRole role)
        {
            NPCState activity;
            string preferredRoute;

            switch (role)
            {
                case NPCRole.Visitor:
                    activity = Random01() < 0.58f ? NPCState.Observing : NPCState.VisitingStand;
                    preferredRoute = Random01() < 0.5f ? marketStallsRoute : marketStallsReverseRoute;
                    break;
                case NPCRole.StallOperator:
                    activity = NPCState.VisitingStand;
                    preferredRoute = Random01() < 0.5f ? marketStallsRoute : marketStallsReverseRoute;
                    break;
                case NPCRole.Supplier:
                    activity = NPCState.Delivering;
                    preferredRoute = deliveryRoute;
                    break;
                case NPCRole.Cleaner:
                    activity = NPCState.Cleaning;
                    preferredRoute = cleaningRoute;
                    break;
                default:
                    activity = NPCState.Walking;
                    preferredRoute = Random01() < 0.5f ? walkThroughRoute : walkThroughReverseRoute;
                    break;
            }

            string route = ResolveRoute(role, activity, preferredRoute);
            Vector3 spawnPosition = transform.position;
            if (pathSystem != null)
            {
                if (!string.IsNullOrEmpty(route) &&
                    pathSystem.TryGetRouteWaypoint(route, 0, out Vector3 routeStart))
                {
                    spawnPosition = routeStart;
                }
                else if (pathSystem.TryGetPlausibleTarget(
                    role,
                    NPCState.Spawn,
                    transform.position,
                    0f,
                    0f,
                    NextUInt(),
                    out _,
                    out Vector3 plausibleSpawn))
                {
                    spawnPosition = plausibleSpawn;
                }
            }

            float speed = RandomRange(0.8f, 1.22f);
            float stagger = RandomRange(0.25f, 3.5f);
            npc.Activate(role, pathSystem, spawnPosition, speed, stagger, NextUInt());
            npc.BeginActivity(activity, route);

            if ((role == NPCRole.Visitor || role == NPCRole.Passerby) && Random01() < 0.24f)
            {
                NPCBehaviour child = FindInactiveNPC();
                if (child != null)
                {
                    child.ActivateChild(npc, pathSystem, RandomRange(0.1f, 0.8f), NextUInt());
                }
            }
        }

        private string ResolveRoute(NPCRole role, NPCState activity, string preferred)
        {
            if (pathSystem == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(preferred) && pathSystem.GetRouteWaypointCount(preferred) > 0)
            {
                return preferred;
            }

            return pathSystem.GetPlausibleRouteName(role, activity, NextUInt());
        }

        private NPCBehaviour FindInactiveNPC()
        {
            if (builtPoolSize == 0)
            {
                return null;
            }

            int start = (int)(NextUInt() % (uint)builtPoolSize);
            for (int i = 0; i < builtPoolSize; i++)
            {
                NPCBehaviour candidate = pool[(start + i) % builtPoolSize];
                if (candidate != null && !candidate.IsActive)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool TryStartEncounter()
        {
            if (Random01() > 0.12f)
            {
                return false;
            }

            IReadOnlyList<NPCBehaviour> active = NPCBehaviour.ActiveInstances;
            for (int i = 0; i < active.Count; i++)
            {
                NPCBehaviour first = active[i];
                if (first == null || first.State != NPCState.Waiting && first.State != NPCState.Observing)
                {
                    continue;
                }

                for (int j = i + 1; j < active.Count; j++)
                {
                    NPCBehaviour second = active[j];
                    if (second == null || second.State != NPCState.Waiting && second.State != NPCState.Observing)
                    {
                        continue;
                    }

                    if (first.BeginBriefEncounter(second, RandomRange(2.5f, 5.5f)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private NPCRole ChooseRole(float hour, bool deepNight)
        {
            float passerby = deepNight ? 6f : hour < 8f ? 18f : hour < 19f ? 30f : 20f;
            float visitor = deepNight ? 0f : hour >= 10f && hour < 20f ? 30f : 8f;
            float stallOperator = deepNight ? 0f : hour >= 7f && hour < 21f ? 18f : 2f;
            float supplier = deepNight ? 0f :
                hour >= 6f && hour < 11f ? 28f :
                hour >= 14f && hour < 17f ? 12f : 2f;
            float cleaner = hour >= 19f && hour < 24f ? 34f :
                hour >= 5f && hour < 7f ? 12f : deepNight ? 3f : 1f;

            float roll = RandomRange(0f, passerby + visitor + stallOperator + supplier + cleaner);
            if ((roll -= passerby) < 0f) return NPCRole.Passerby;
            if ((roll -= visitor) < 0f) return NPCRole.Visitor;
            if ((roll -= stallOperator) < 0f) return NPCRole.StallOperator;
            if ((roll -= supplier) < 0f) return NPCRole.Supplier;
            return NPCRole.Cleaner;
        }

        private void ScheduleFirstActivity()
        {
            float hour = ReadCurrentHour();
            bool deepNight = IsDeepNight(hour);
            nextActivityAt = Time.time + (deepNight
                ? RandomRange(28f, 65f)
                : RandomRange(3.5f, 16f));
        }

        private void ScheduleFollowingActivity(float hour, bool deepNight)
        {
            float delay;
            if (deepNight)
            {
                delay = RandomRange(30f, 90f);
            }
            else if (Random01() < quietPeriodChance)
            {
                delay = RandomRange(25f, 58f);
            }
            else if (hour >= 9f && hour < 21f)
            {
                delay = RandomRange(6f, 16f);
            }
            else
            {
                delay = RandomRange(12f, 30f);
            }

            nextActivityAt = Time.time + delay;
        }

        private float ReadCurrentHour()
        {
            return timeOfDayController != null
                ? Mathf.Repeat(timeOfDayController.CurrentHour, 24f)
                : 12f;
        }

        private bool IsDeepNight(float hour)
        {
            return hour < 5f ||
                   (timeOfDayController != null &&
                    timeOfDayController.CurrentPhase == TimeOfDayPhase.Night);
        }

        private uint NextUInt()
        {
            uint value = randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            randomState = value != 0u ? value : 0x6d2b79f5u;
            return randomState;
        }

        private float Random01()
        {
            return (NextUInt() & 0x00ffffffu) * (1f / 16777216f);
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, Random01());
        }
    }
}
