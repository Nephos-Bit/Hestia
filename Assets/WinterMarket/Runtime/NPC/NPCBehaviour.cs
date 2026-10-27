using System.Collections.Generic;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class NPCBehaviour : MonoBehaviour
    {
        private static readonly List<NPCBehaviour> ActiveNPCs = new List<NPCBehaviour>(6);

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float baseMoveSpeed = 0.65f;
        [SerializeField, Min(30f)] private float turnSpeedDegrees = 150f;
        [SerializeField, Min(0.01f)] private float arrivalDistance = 0.08f;

        private PathSystem pathSystem;
        private NPCVisual visual;
        private Transform body;
        private Transform head;
        private Transform leftArm;
        private Transform rightArm;
        private Transform leftLeg;
        private Transform rightLeg;
        private Transform crate;

        private Vector3 bodyRestPosition;
        private Vector3 rootRestScale;
        private Quaternion bodyRestRotation;
        private Quaternion headRestRotation;
        private Quaternion leftArmRestRotation;
        private Quaternion rightArmRestRotation;
        private Quaternion leftLegRestRotation;
        private Quaternion rightLegRestRotation;

        private NPCRole role;
        private NPCState state;
        private NPCState plannedActivity;
        private string activityRoute;
        private string currentRoute;
        private int routeIndex;
        private int routeDirection;
        private bool hasSingleTarget;
        private Vector3 singleTarget;
        private float moveSpeed;
        private float stateEndsAt;
        private float walkCycle;
        private Quaternion observationHeading;
        private uint randomState;
        private bool active;
        private bool carryingCrate;
        private bool observeAfterMovement;
        private bool hasObstacleDetour;
        private Vector3 obstacleDetour;
        private NPCBehaviour guardian;
        private float followSideOffset;
        private int navigationRevision = -1;

        public NPCRole Role => role;
        public NPCState State => state;
        public bool IsActive => active;
        public bool IsCarryingCrate => carryingCrate;
        public static IReadOnlyList<NPCBehaviour> ActiveInstances => ActiveNPCs;

        internal void ConfigureRig(
            NPCVisual npcVisual,
            Transform bodyPart,
            Transform headPart,
            Transform leftArmPart,
            Transform rightArmPart,
            Transform leftLegPart,
            Transform rightLegPart,
            Transform cratePart)
        {
            visual = npcVisual;
            body = bodyPart;
            head = headPart;
            leftArm = leftArmPart;
            rightArm = rightArmPart;
            leftLeg = leftLegPart;
            rightLeg = rightLegPart;
            crate = cratePart;

            rootRestScale = transform.localScale;
            bodyRestPosition = body != null ? body.localPosition : Vector3.zero;
            bodyRestRotation = body != null ? body.localRotation : Quaternion.identity;
            headRestRotation = head != null ? head.localRotation : Quaternion.identity;
            leftArmRestRotation = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            rightArmRestRotation = rightArm != null ? rightArm.localRotation : Quaternion.identity;
            leftLegRestRotation = leftLeg != null ? leftLeg.localRotation : Quaternion.identity;
            rightLegRestRotation = rightLeg != null ? rightLeg.localRotation : Quaternion.identity;
        }

        public void Activate(
            NPCRole npcRole,
            PathSystem navigation,
            Vector3 worldPosition,
            float speedVariation,
            float startDelay,
            uint seed)
        {
            gameObject.SetActive(true);

            role = npcRole;
            pathSystem = navigation;
            transform.position = worldPosition;
            randomState = seed != 0u ? seed : 0x9e3779b9u;
            moveSpeed = baseMoveSpeed * Mathf.Clamp(speedVariation, 0.72f, 1.32f);
            plannedActivity = NPCState.Walking;
            activityRoute = null;
            currentRoute = null;
            hasSingleTarget = false;
            hasObstacleDetour = false;
            navigationRevision = navigation != null ? navigation.NavigationRevision : -1;
            guardian = null;
            observeAfterMovement = false;
            active = true;
            walkCycle = RandomRange(0f, Mathf.PI * 2f);

            float width = RandomRange(0.9f, 1.09f);
            float height = RandomRange(0.92f, 1.1f);
            transform.localScale = Vector3.Scale(rootRestScale, new Vector3(width, height, width));
            visual?.ApplyAppearance(npcRole, randomState);
            if (!ActiveNPCs.Contains(this))
            {
                ActiveNPCs.Add(this);
            }
            SetCarryingCrate(npcRole == NPCRole.Supplier);
            SetState(NPCState.Spawn, Mathf.Max(0f, startDelay));
        }

        public void ActivateChild(NPCBehaviour adult, PathSystem navigation, float startDelay, uint seed)
        {
            if (adult == null || !adult.active)
            {
                return;
            }

            Vector3 spawnPosition = adult.transform.position - adult.transform.forward * 0.3f;
            Activate(NPCRole.Child, navigation, spawnPosition, 1.08f, startDelay, seed);
            guardian = adult;
            followSideOffset = RandomRange(-0.28f, 0.28f);
            if (Mathf.Abs(followSideOffset) < 0.12f)
            {
                followSideOffset = followSideOffset < 0f ? -0.18f : 0.18f;
            }

            // The same voxel rig stays stylistically consistent while the smaller scale makes
            // children immediately recognizable without a second mesh family.
            transform.localScale *= 0.62f;
            SetCarryingCrate(false);
            SetState(NPCState.Walking, 0f);
        }

        public void BeginActivity(NPCState activity, string routeName)
        {
            if (!active)
            {
                return;
            }

            plannedActivity = activity;
            activityRoute = routeName;
            if (state != NPCState.Spawn || Time.time >= stateEndsAt)
            {
                BeginPlannedActivity();
            }
        }

        public void SetCarryingCrate(bool value)
        {
            carryingCrate = value;
            if (crate != null)
            {
                crate.gameObject.SetActive(value);
            }
        }

        public void Deactivate()
        {
            active = false;
            currentRoute = null;
            activityRoute = null;
            hasSingleTarget = false;
            hasObstacleDetour = false;
            guardian = null;
            SetCarryingCrate(false);
            ResetRig();
            transform.localScale = rootRestScale;
            ActiveNPCs.Remove(this);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            ActiveNPCs.Remove(this);
        }

        /// <summary>
        /// Both figures pause, turn towards one another and then leave independently. This is
        /// intentionally only a small chance event, not a continuous social animation.
        /// </summary>
        public bool BeginBriefEncounter(NPCBehaviour other, float duration)
        {
            if (!active || other == null || !other.active || other == this ||
                IsMovingState(state) || IsMovingState(other.state))
            {
                return false;
            }

            Vector3 toOther = other.transform.position - transform.position;
            Vector3 toThis = -toOther;
            if (toOther.sqrMagnitude < 0.04f || toOther.sqrMagnitude > 2.25f)
            {
                return false;
            }

            transform.rotation = Quaternion.LookRotation(toOther.normalized, Vector3.up);
            other.transform.rotation = Quaternion.LookRotation(toThis.normalized, Vector3.up);
            SetState(NPCState.Observing, duration);
            other.SetState(NPCState.Observing, duration + other.RandomRange(-0.45f, 0.75f));
            return true;
        }

        private void Update()
        {
            if (!active)
            {
                return;
            }

            if (guardian != null)
            {
                UpdateChildFollowing(Time.deltaTime);
                return;
            }

            bool moving = IsMovingState(state);
            if (moving)
            {
                UpdateMovement(Time.deltaTime);
            }
            else if (Time.time >= stateEndsAt)
            {
                CompleteTimedState();
            }

            UpdateRigAnimation(moving, Time.deltaTime);
        }

        private void UpdateChildFollowing(float deltaTime)
        {
            if (!guardian.active)
            {
                Deactivate();
                return;
            }

            Vector3 target = guardian.transform.position
                - guardian.transform.forward * 0.38f
                + guardian.transform.right * followSideOffset;
            target.y = transform.position.y;
            Vector3 offset = target - transform.position;
            offset.y = 0f;
            bool moving = offset.sqrMagnitude > 0.025f;
            if (moving)
            {
                Vector3 direction = offset.normalized;
                Quaternion desiredRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRotation,
                    turnSpeedDegrees * deltaTime * 1.25f);

                Vector3 proposed = transform.position + direction * (moveSpeed * deltaTime * 1.15f);
                transform.position = pathSystem != null
                    ? pathSystem.ConstrainToWalkableSpace(transform.position, proposed, 0.18f)
                    : proposed;
            }

            state = NPCState.Walking;
            UpdateRigAnimation(moving, deltaTime);
        }

        private void BeginPlannedActivity()
        {
            switch (plannedActivity)
            {
                case NPCState.Observing:
                    observeAfterMovement = true;
                    BeginMoving(NPCState.VisitingStand, activityRoute);
                    break;
                case NPCState.Waiting:
                case NPCState.Interacting:
                    SetState(plannedActivity, RandomRange(2f, 12f));
                    break;
                case NPCState.Walking:
                case NPCState.VisitingStand:
                case NPCState.Delivering:
                case NPCState.Cleaning:
                case NPCState.Leaving:
                    BeginMoving(plannedActivity, activityRoute);
                    break;
                default:
                    BeginMoving(NPCState.Walking, activityRoute);
                    break;
            }
        }

        private void BeginMoving(NPCState movingState, string preferredRoute)
        {
            state = movingState;
            hasSingleTarget = false;
            hasObstacleDetour = false;
            currentRoute = preferredRoute;

            if (pathSystem != null)
            {
                if (string.IsNullOrEmpty(currentRoute) || pathSystem.GetRouteWaypointCount(currentRoute) == 0)
                {
                    currentRoute = pathSystem.GetPlausibleRouteName(role, movingState, NextUInt());
                }

                int count = pathSystem.GetRouteWaypointCount(currentRoute);
                if (count > 0)
                {
                    SelectRouteStart(count);
                    return;
                }

                if (pathSystem.TryGetPlausibleTarget(
                    role,
                    movingState,
                    transform.position,
                    1.5f,
                    40f,
                    NextUInt(),
                    out _,
                    out singleTarget))
                {
                    hasSingleTarget = true;
                    return;
                }
            }

            CompleteMovement();
        }

        private void SelectRouteStart(int count)
        {
            int nearest = 0;
            float nearestSqr = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                if (!pathSystem.TryGetRouteWaypoint(currentRoute, i, out Vector3 point))
                {
                    continue;
                }

                float sqrDistance = (point - transform.position).sqrMagnitude;
                if (sqrDistance < nearestSqr)
                {
                    nearestSqr = sqrDistance;
                    nearest = i;
                }
            }

            routeIndex = nearest;
            routeDirection = nearest <= (count - 1) / 2 ? 1 : -1;
        }

        private void UpdateMovement(float deltaTime)
        {
            // A confirmed build edit may have inserted a prop into a route. Do not teleport the
            // figure: constrain it out of a new footprint and reacquire the closest part of the
            // existing route on the next frame.
            if (pathSystem != null && navigationRevision != pathSystem.NavigationRevision)
            {
                navigationRevision = pathSystem.NavigationRevision;
                transform.position = pathSystem.ConstrainToWalkableSpace(
                    transform.position + Vector3.left * 0.01f,
                    transform.position,
                    0.24f);
                hasObstacleDetour = false;
                if (!hasSingleTarget && !string.IsNullOrEmpty(currentRoute))
                {
                    int count = pathSystem.GetRouteWaypointCount(currentRoute);
                    if (count > 0)
                    {
                        SelectRouteStart(count);
                    }
                    else
                    {
                        BeginMoving(state, null);
                        return;
                    }
                }
            }

            Vector3 target;
            if (hasSingleTarget)
            {
                target = singleTarget;
            }
            else if (pathSystem == null || !pathSystem.TryGetRouteWaypoint(currentRoute, routeIndex, out target))
            {
                AdvanceRoute();
                return;
            }

            if (!hasObstacleDetour && pathSystem != null &&
                pathSystem.TryGetObstacleDetour(transform.position, target, 0.24f, out Vector3 detour))
            {
                obstacleDetour = detour;
                hasObstacleDetour = true;
            }

            Vector3 movementTarget = hasObstacleDetour ? obstacleDetour : target;
            Vector3 offset = movementTarget - transform.position;
            float arrivalSqr = arrivalDistance * arrivalDistance;
            if (offset.sqrMagnitude <= arrivalSqr)
            {
                if (hasObstacleDetour)
                {
                    hasObstacleDetour = false;
                    return;
                }

                if (hasSingleTarget)
                {
                    CompleteMovement();
                }
                else
                {
                    AdvanceRoute();
                }

                return;
            }

            Vector3 direction = offset.normalized;
            Vector3 avoidedDirection = (direction + CalculateCrowdAvoidance()).normalized;
            if (avoidedDirection.sqrMagnitude < 0.0001f)
            {
                avoidedDirection = direction;
            }
            if (avoidedDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(avoidedDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desiredRotation,
                    turnSpeedDegrees * deltaTime);
            }

            float step = moveSpeed * deltaTime;
            if (step >= offset.magnitude)
            {
                transform.position = movementTarget;
            }
            else
            {
                Vector3 proposed = transform.position + avoidedDirection * step;
                transform.position = pathSystem != null
                    ? pathSystem.ConstrainToWalkableSpace(transform.position, proposed, 0.24f)
                    : proposed;
            }
        }

        private Vector3 CalculateCrowdAvoidance()
        {
            Vector3 steering = Vector3.zero;
            for (int i = 0; i < ActiveNPCs.Count; i++)
            {
                NPCBehaviour other = ActiveNPCs[i];
                if (other == null || other == this || !other.active)
                {
                    continue;
                }

                Vector3 separation = transform.position - other.transform.position;
                separation.y = 0f;
                float distance = separation.magnitude;
                if (distance <= 0.001f)
                {
                    float side = ((GetInstanceID() ^ other.GetInstanceID()) & 1) == 0 ? -1f : 1f;
                    steering += transform.right * side;
                }
                else if (distance < 0.62f)
                {
                    steering += separation / distance * ((0.62f - distance) / 0.62f);
                }
            }

            steering.y = 0f;
            return Vector3.ClampMagnitude(steering * 0.65f, 0.55f);
        }

        private void AdvanceRoute()
        {
            int count = pathSystem != null ? pathSystem.GetRouteWaypointCount(currentRoute) : 0;
            routeIndex += routeDirection;

            if (routeIndex < 0 || routeIndex >= count)
            {
                CompleteMovement();
            }
        }

        private void CompleteMovement()
        {
            currentRoute = null;
            hasSingleTarget = false;
            hasObstacleDetour = false;

            switch (state)
            {
                case NPCState.VisitingStand:
                    if (observeAfterMovement)
                    {
                        observeAfterMovement = false;
                        SetState(NPCState.Observing, RandomRange(2f, 8f));
                    }
                    else
                    {
                        SetState(NPCState.Interacting, RandomRange(3f, 10f));
                    }
                    break;
                case NPCState.Delivering:
                    SetState(NPCState.Interacting, RandomRange(2f, 5f));
                    break;
                case NPCState.Leaving:
                    Deactivate();
                    break;
                case NPCState.Cleaning:
                    SetState(NPCState.Waiting, RandomRange(2f, 5f));
                    break;
                default:
                    SetState(NPCState.Waiting, RandomRange(2f, 7f));
                    break;
            }
        }

        private void CompleteTimedState()
        {
            switch (state)
            {
                case NPCState.Spawn:
                    BeginPlannedActivity();
                    break;
                case NPCState.Observing:
                    BeginLeaving();
                    break;
                case NPCState.Interacting:
                    if (role == NPCRole.Supplier)
                    {
                        SetCarryingCrate(false);
                    }

                    BeginLeaving();
                    break;
                case NPCState.Waiting:
                default:
                    BeginLeaving();
                    break;
            }
        }

        private void BeginLeaving()
        {
            string exitRoute = pathSystem != null
                ? pathSystem.GetExitRoute(transform.position)
                : null;
            BeginMoving(NPCState.Leaving, exitRoute);
        }

        private void SetState(NPCState newState, float duration)
        {
            state = newState;
            stateEndsAt = Time.time + Mathf.Clamp(duration, 0f, 12f);
            observationHeading = transform.rotation;
        }

        private void UpdateRigAnimation(bool moving, float deltaTime)
        {
            float blend = Mathf.Clamp01(deltaTime * 8f);
            float swing = 0f;
            float bob = 0f;

            if (moving)
            {
                walkCycle += deltaTime * (4.5f + moveSpeed * 2f);
                swing = Mathf.Sin(walkCycle) * 19f;
                bob = Mathf.Abs(Mathf.Sin(walkCycle * 2f)) * 0.025f;
            }

            SetLocalRotation(leftArm, leftArmRestRotation * Quaternion.Euler(swing, 0f, 0f), blend);
            SetLocalRotation(rightArm, rightArmRestRotation * Quaternion.Euler(-swing, 0f, 0f), blend);
            SetLocalRotation(leftLeg, leftLegRestRotation * Quaternion.Euler(-swing * 0.7f, 0f, 0f), blend);
            SetLocalRotation(rightLeg, rightLegRestRotation * Quaternion.Euler(swing * 0.7f, 0f, 0f), blend);

            if (body != null)
            {
                body.localPosition = Vector3.Lerp(body.localPosition, bodyRestPosition + Vector3.up * bob, blend);
                body.localRotation = Quaternion.Slerp(body.localRotation, bodyRestRotation, blend);
            }

            bool looking = state == NPCState.Waiting || state == NPCState.Observing || state == NPCState.Interacting;
            float lookYaw = looking ? Mathf.Sin(Time.time * 0.72f + (randomState & 31u)) * 24f : 0f;
            if (role == NPCRole.Cleaner && state == NPCState.Waiting)
            {
                float tidy = Mathf.Sin(Time.time * 2.2f) * 24f;
                SetLocalRotation(rightArm, rightArmRestRotation * Quaternion.Euler(tidy, 0f, -12f), blend);
            }
            if (head != null)
            {
                head.localRotation = Quaternion.Slerp(
                    head.localRotation,
                    headRestRotation * Quaternion.Euler(0f, lookYaw, 0f),
                    blend * 0.65f);
            }

            if (looking)
            {
                Quaternion bodyLook = observationHeading * Quaternion.Euler(0f, lookYaw * 0.22f, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, bodyLook, 18f * deltaTime);
            }
        }

        private void ResetRig()
        {
            if (body != null)
            {
                body.localPosition = bodyRestPosition;
                body.localRotation = bodyRestRotation;
            }

            if (head != null) head.localRotation = headRestRotation;
            if (leftArm != null) leftArm.localRotation = leftArmRestRotation;
            if (rightArm != null) rightArm.localRotation = rightArmRestRotation;
            if (leftLeg != null) leftLeg.localRotation = leftLegRestRotation;
            if (rightLeg != null) rightLeg.localRotation = rightLegRestRotation;
        }

        private static void SetLocalRotation(Transform part, Quaternion target, float blend)
        {
            if (part != null)
            {
                part.localRotation = Quaternion.Slerp(part.localRotation, target, blend);
            }
        }

        private static bool IsMovingState(NPCState value)
        {
            return value == NPCState.Walking ||
                   value == NPCState.VisitingStand ||
                   value == NPCState.Delivering ||
                   value == NPCState.Cleaning ||
                   value == NPCState.Leaving;
        }

        private uint NextUInt()
        {
            uint value = randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            randomState = value != 0u ? value : 0x9e3779b9u;
            return randomState;
        }

        private float RandomRange(float minimum, float maximum)
        {
            float unit = (NextUInt() & 0x00ffffffu) * (1f / 16777216f);
            return Mathf.Lerp(minimum, maximum, unit);
        }
    }
}
