using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMarket
{
    [DisallowMultipleComponent]
    public sealed class BuildModeController : MonoBehaviour
    {
        public const float GridSize = 0.60f;
        private const int HistoryLimit = 40;
        // The whole visible snowfield remains constructible. Coordinates are grid-aligned,
        // while generated trees stay decorative and are never treated as occupied build cells.
        internal const float BuildMinX = -72f;
        internal const float BuildMaxX = 72f;
        internal const float BuildMinZ = -72f;
        internal const float BuildMaxZ = 72f;
        private const int RoadHalfWidthCells = 1;
        private const int PathSnapRangeCells = 4;
        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left,
            Vector2Int.down
        };

        private readonly Dictionary<string, MarketBuildObject> instances = new Dictionary<string, MarketBuildObject>();
        private readonly List<BuildLayoutAction> undo = new List<BuildLayoutAction>(HistoryLimit);
        private readonly List<BuildLayoutAction> redo = new List<BuildLayoutAction>(HistoryLimit);
        private readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> pathCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> roadBlockedScratch = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> roadVisitedScratch = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, Vector2Int> roadPreviousScratch = new Dictionary<Vector2Int, Vector2Int>();
        private readonly Queue<Vector2Int> roadQueueScratch = new Queue<Vector2Int>();
        private readonly List<Vector2Int> roadRouteScratch = new List<Vector2Int>(128);
        private readonly HashSet<Vector2Int> roadFootprintScratch = new HashSet<Vector2Int>();

        private SceneController scene;
        private PathSystem paths;
        private LightingController lighting;
        private Camera dioramaCamera;
        private Transform layoutRoot;
        private BuildLayoutData layout;
        private BuildGridOverlay gridOverlay;
        private RoadPreviewOverlay roadPreview;
        private StructureSafetyOverlay safetyOverlay;
        private ChunkedDeepSnowController deepSnow;
        private ChunkedForestController forest;
        private bool configured;
        private bool buildMode;
        private bool buildButtonVisible;
        private float hideBuildButtonAt;
        private BuildCategory category = BuildCategory.Decoration;
        private BuildObjectDefinition selectedDefinition;
        private MarketBuildObject preview;
        private PlacedBuildObjectData original;
        private bool previewIsValid;
        private string validationMessage;
        private int palettePage;
        private bool draggingPreview;
        private bool roadPlacementActive;
        private bool hasRoadStart;
        private Vector3 roadStart;
        private bool hasRoadPreviewEnd;
        private Vector3 roadPreviewEnd;
        private readonly List<Vector2Int> roadPreviewCells = new List<Vector2Int>();
        private bool deleteToolActive;
        private PlacedBuildObjectData deleteStructureSelection;
        private readonly HashSet<Vector2Int> deleteRoadSelection = new HashSet<Vector2Int>();
        private MarketBuildObject deleteGhost;
        private string deleteMessage;

        public bool IsBuildMode => buildMode;
        public bool HasPreview => preview != null;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
        public BuildLayoutData CurrentLayout => layout != null ? layout.Clone() : null;

        public void Configure(SceneController controller, PathSystem navigation, LightingController lightingController)
        {
            if (configured) return;
            configured = true;
            scene = controller;
            paths = navigation;
            lighting = lightingController;
            dioramaCamera = controller != null ? controller.DioramaCamera : Camera.main;
            layoutRoot = new GameObject("Gestaltbarer Markt").transform;
            layoutRoot.SetParent(controller != null ? controller.WorldRoot : transform, false);
            safetyOverlay = StructureSafetyOverlay.Create(layoutRoot);
            deepSnow = new ChunkedDeepSnowController(layoutRoot, scene.Palette);
            forest = new ChunkedForestController(layoutRoot, scene.Palette);
            layout = BuildLayoutStore.LoadOrDefault();
            // Persist migrations and coordinate normalization before any object is rendered.
            BuildLayoutStore.Save(layout);
            if (!Enum.TryParse(layout.preferences.lastCategory, out category)) category = BuildCategory.Decoration;
            if (scene != null && scene.Quality != null) scene.Quality.QualityChanged += OnQualityChanged;
            RebuildLayout();
        }

        private void Update()
        {
            if (!configured) return;

            bool pointerDown = TryGetPointerDown(out Vector2 pointer);
            if (!buildMode)
            {
                if (pointerDown || Input.anyKeyDown)
                {
                    buildButtonVisible = true;
                    hideBuildButtonAt = Time.unscaledTime + 4.5f;
                }
                else if (buildButtonVisible && Time.unscaledTime >= hideBuildButtonAt)
                {
                    buildButtonVisible = false;
                }
                return;
            }

            if (deleteToolActive)
            {
                if (!pointerDown || IsOverBuildUi(pointer) || !TryGetGroundPoint(pointer, out Vector3 deletePoint)) return;
                HandleDeleteClick(pointer, Snap(deletePoint));
                return;
            }

            if (roadPlacementActive)
            {
                if (hasRoadStart && TryGetRoadHoverPoint(out Vector3 hoverPoint))
                {
                    UpdateRoadPreview(Snap(hoverPoint));
                }
                if (!pointerDown || IsOverBuildUi(pointer) || !TryGetGroundPoint(pointer, out Vector3 roadPoint)) return;
                HandleRoadClick(Snap(roadPoint));
                return;
            }

            if (preview != null && draggingPreview)
            {
                if (TryGetPointerHeld(out Vector2 heldPointer) && !IsOverBuildUi(heldPointer) &&
                    TryGetGroundPoint(heldPointer, out Vector3 heldPoint))
                {
                    MovePreview(Snap(heldPoint));
                }
                if (TryGetPointerUp(out _) || !IsPointerPressed())
                {
                    draggingPreview = false;
                    ConfirmPreview();
                }
                return;
            }

            if (!pointerDown || IsOverBuildUi(pointer) || !TryGetGroundPoint(pointer, out Vector3 point)) return;
            point = Snap(point);

            if (preview != null)
            {
                draggingPreview = true;
                MovePreview(point);
                return;
            }

            Ray ray = dioramaCamera.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out RaycastHit hit, 60f))
            {
                MarketBuildObject selected = hit.collider.GetComponent<MarketBuildObject>();
                if (selected != null)
                {
                    BeginEdit(selected);
                    draggingPreview = preview != null;
                    MovePreview(point);
                }
            }
        }

        public void EnterBuildMode()
        {
            if (!configured || buildMode) return;
            buildMode = true;
            buildButtonVisible = true;
            hideBuildButtonAt = float.PositiveInfinity;
            SetGridVisible(layout.preferences.showGrid);
        }

        public void ExitBuildMode()
        {
            if (!buildMode) return;
            CancelPreview();
            ExitDeleteTool();
            buildMode = false;
            buildButtonVisible = false;
            SetGridVisible(false);
            SaveLayout();
        }

        public bool TryPlaceForTesting(BuildObjectType type, Vector3 worldPosition, int rotation = 0)
        {
            if (!configured) return false;
            BuildObjectDefinition definition = BuildObjectLibrary.Get(type);
            var data = NewEntry(definition, Snap(worldPosition));
            data.rotation = ((rotation % 4) + 4) % 4;
            if (!IsValid(data, null, out _)) return false;
            layout.objects.Add(data);
            Record(new BuildLayoutAction(null, data.Clone()));
            RebuildLayout();
            SaveLayout();
            return true;
        }

        /// <summary>Editor capture hook; it creates only a transient ghost and never changes the layout.</summary>
        public void ShowPreviewForCapture(BuildObjectType type, Vector3 worldPosition, int rotation = 0)
        {
            EnterBuildMode();
            CancelPreview();
            selectedDefinition = BuildObjectLibrary.Get(type);
            PlacedBuildObjectData data = NewEntry(selectedDefinition, Snap(worldPosition));
            data.rotation = ((rotation % 4) + 4) % 4;
            preview = BuildObjectVisual.Create(
                data,
                scene.Palette,
                layoutRoot,
                true,
                scene.SnowCapMaterial,
                BuildFenceJoins(data, null));
            ValidatePreview();
        }

        public bool TryRemoveForTesting(string id)
        {
            int index = FindIndex(id);
            if (index < 0) return false;
            PlacedBuildObjectData old = layout.objects[index].Clone();
            layout.objects.RemoveAt(index);
            Record(new BuildLayoutAction(old, null));
            RebuildLayout();
            SaveLayout();
            return true;
        }

        public void Undo()
        {
            if (undo.Count == 0 || preview != null) return;
            int last = undo.Count - 1;
            BuildLayoutAction action = undo[last];
            undo.RemoveAt(last);
            ApplyAction(action.Before, action.After);
            redo.Add(action);
            RebuildLayout();
            SaveLayout();
        }

        public void Redo()
        {
            if (redo.Count == 0 || preview != null) return;
            int last = redo.Count - 1;
            BuildLayoutAction action = redo[last];
            redo.RemoveAt(last);
            ApplyAction(action.After, action.Before);
            undo.Add(action);
            RebuildLayout();
            SaveLayout();
        }

        private void BeginNew(BuildObjectDefinition definition)
        {
            ExitDeleteTool();
            CancelPreview();
            selectedDefinition = definition;
            original = null;
            if (definition.Type == BuildObjectType.PathTile)
            {
                roadPlacementActive = true;
                EnsureRoadPreview();
                validationMessage = "Startpunkt der Straße setzen";
                return;
            }
            Vector3 center = GetDefaultPreviewPosition();
            PlacedBuildObjectData data = NewEntry(definition, center);
            SnapStructureToPath(data);
            preview = BuildObjectVisual.Create(
                data,
                scene.Palette,
                layoutRoot,
                true,
                scene.SnowCapMaterial,
                BuildFenceJoins(data, null));
            ValidatePreview();
        }

        private void BeginEdit(MarketBuildObject target)
        {
            if (target == null || target.Data == null) return;
            selectedDefinition = target.Definition;
            original = target.Data.Clone();
            PlacedBuildObjectData previewData = target.Data.Clone();
            preview = BuildObjectVisual.Create(
                previewData,
                scene.Palette,
                layoutRoot,
                true,
                scene.SnowCapMaterial,
                BuildFenceJoins(previewData, target.Data.id));
            ValidatePreview();
        }

        private void MovePreview(Vector3 position)
        {
            if (preview == null) return;
            preview.Data.x = position.x;
            preview.Data.z = position.z;
            SnapStructureToPath(preview.Data);
            preview.transform.position = preview.Data.Position;
            ValidatePreview();
        }

        private void RotatePreview()
        {
            if (preview == null) return;
            preview.Data.rotation = (preview.Data.rotation + 1) % 4;
            SnapStructureToPath(preview.Data);
            preview.transform.rotation = preview.Data.Rotation;
            preview.transform.position = preview.Data.Position;
            ValidatePreview();
        }

        private void ConfirmPreview()
        {
            if (preview == null || !previewIsValid) return;
            PlacedBuildObjectData after = preview.Data.Clone();
            if (original == null)
            {
                layout.objects.Add(after);
                Record(new BuildLayoutAction(null, after.Clone()));
            }
            else
            {
                int index = FindIndex(original.id);
                if (index < 0) return;
                if (SamePlacement(original, after))
                {
                    Destroy(preview.gameObject);
                    preview = null;
                    original = null;
                    safetyOverlay?.Clear();
                    return;
                }
                layout.objects[index] = after;
                Record(new BuildLayoutAction(original.Clone(), after.Clone()));
            }
            Destroy(preview.gameObject);
            preview = null;
            original = null;
            safetyOverlay?.Clear();
            RebuildLayout();
            SaveLayout();
        }

        private void CancelPreview()
        {
            if (preview != null) Destroy(preview.gameObject);
            preview = null;
            original = null;
            safetyOverlay?.Clear();
            draggingPreview = false;
            roadPlacementActive = false;
            hasRoadStart = false;
            hasRoadPreviewEnd = false;
            roadPreviewCells.Clear();
            if (roadPreview != null) roadPreview.Clear();
            validationMessage = null;
        }

        private void DeleteSelected()
        {
            if (original == null) return;
            int index = FindIndex(original.id);
            if (index >= 0)
            {
                PlacedBuildObjectData removed = layout.objects[index].Clone();
                layout.objects.RemoveAt(index);
                Record(new BuildLayoutAction(removed, null));
            }
            CancelPreview();
            RebuildLayout();
            SaveLayout();
        }

        private void ToggleDeleteTool()
        {
            if (deleteToolActive)
            {
                ExitDeleteTool();
                return;
            }
            CancelPreview();
            deleteToolActive = true;
            deleteMessage = "Struktur oder Straße auswählen";
        }

        private void ExitDeleteTool()
        {
            deleteToolActive = false;
            ClearDeleteSelection();
        }

        private void HandleDeleteClick(Vector2 screenPoint, Vector3 groundPoint)
        {
            MarketBuildObject hit = null;
            Ray ray = dioramaCamera.ScreenPointToRay(screenPoint);
            if (Physics.Raycast(ray, out RaycastHit raycastHit, 60f))
            {
                hit = raycastHit.collider.GetComponent<MarketBuildObject>();
            }

            if (hit != null && hit.Data != null && hit.Data.type != BuildObjectType.PathTile)
            {
                if (deleteStructureSelection != null && hit.Data.id == deleteStructureSelection.id)
                {
                    DeleteStructureSelection();
                }
                else
                {
                    SelectStructureForDeletion(hit.Data);
                }
                return;
            }

            Vector2Int cell = new Vector2Int(
                Mathf.RoundToInt(groundPoint.x / GridSize),
                Mathf.RoundToInt(groundPoint.z / GridSize));
            if (deleteRoadSelection.Count > 0 && deleteRoadSelection.Contains(cell))
            {
                DeleteRoadSelection();
                return;
            }
            if (pathCells.Contains(cell))
            {
                SelectRoadForDeletion(cell);
                return;
            }

            ClearDeleteSelection();
            deleteMessage = "Struktur oder platzierte Straße auswählen";
        }

        private void SelectStructureForDeletion(PlacedBuildObjectData data)
        {
            ClearDeleteSelection();
            deleteStructureSelection = data.Clone();
            deleteGhost = BuildObjectVisual.Create(
                deleteStructureSelection.Clone(), scene.Palette, layoutRoot, true, scene.SnowCapMaterial);
            TintDeleteGhost(deleteGhost);
            var zone = new HashSet<Vector2Int>();
            AddSafetyCells(deleteStructureSelection, zone);
            safetyOverlay?.SetZones(Array.Empty<Vector2Int>(), zone);
            deleteMessage = "Auswahl rot markiert – erneut auf die Struktur klicken zum Löschen";
        }

        private void SelectRoadForDeletion(Vector2Int center)
        {
            ClearDeleteSelection();
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector2Int cell = new Vector2Int(center.x + x, center.y + z);
                    if (pathCells.Contains(cell)) deleteRoadSelection.Add(cell);
                }
            }
            if (deleteRoadSelection.Count == 0)
            {
                deleteMessage = "Dieser Straßenbereich kann nicht gelöscht werden";
                return;
            }
            EnsureRoadPreview();
            roadPreview.SetCells(new List<Vector2Int>(deleteRoadSelection), false);
            deleteMessage = "3×3 Straßenblock rot markiert – erneut in den Bereich klicken zum Löschen";
        }

        private void DeleteStructureSelection()
        {
            int index = deleteStructureSelection != null ? FindIndex(deleteStructureSelection.id) : -1;
            if (index < 0)
            {
                ClearDeleteSelection();
                deleteMessage = "Die Struktur ist nicht mehr vorhanden";
                return;
            }
            PlacedBuildObjectData removed = layout.objects[index].Clone();
            layout.objects.RemoveAt(index);
            Record(new BuildLayoutAction(removed, null));
            ClearDeleteSelection();
            RebuildLayout();
            SaveLayout();
            deleteMessage = "Struktur entfernt";
        }

        private void DeleteRoadSelection()
        {
            int removed = 0;
            for (int index = layout.objects.Count - 1; index >= 0; index--)
            {
                PlacedBuildObjectData item = layout.objects[index];
                if (item == null || item.type != BuildObjectType.PathTile) continue;
                bool remove = false;
                foreach (Vector2Int cell in EnumerateCells(item))
                {
                    if (!deleteRoadSelection.Contains(cell)) continue;
                    remove = true;
                    break;
                }
                if (!remove) continue;
                layout.objects.RemoveAt(index);
                Record(new BuildLayoutAction(item.Clone(), null));
                removed++;
            }
            ClearDeleteSelection();
            if (removed > 0)
            {
                RebuildLayout();
                SaveLayout();
                deleteMessage = "Straßenblock entfernt";
            }
            else
            {
                deleteMessage = "In diesem Bereich gibt es keine platzierte Straße";
            }
        }

        private void ClearDeleteSelection()
        {
            if (deleteGhost != null) Destroy(deleteGhost.gameObject);
            deleteGhost = null;
            deleteStructureSelection = null;
            deleteRoadSelection.Clear();
            safetyOverlay?.Clear();
            if (roadPreview != null) roadPreview.Clear();
        }

        private static void TintDeleteGhost(MarketBuildObject ghost)
        {
            if (ghost == null) return;
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
            Color tint = new Color(0.96f, 0.18f, 0.16f, 0.56f);
            for (int index = 0; index < renderers.Length; index++)
            {
                Material[] materials = renderers[index].materials;
                for (int material = 0; material < materials.Length; material++)
                {
                    if (materials[material] != null && materials[material].HasProperty("_BaseColor"))
                    {
                        materials[material].SetColor("_BaseColor", tint);
                    }
                }
            }
        }

        private void RebuildLayout()
        {
            double rebuildStartedAt = Time.realtimeSinceStartupAsDouble;
            var changes = new List<LayoutDelta>();
            var desiredIds = new HashSet<string>();
            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData data = layout.objects[i];
                if (data == null || string.IsNullOrEmpty(data.id)) continue;
                desiredIds.Add(data.id);

                if (instances.TryGetValue(data.id, out MarketBuildObject current) &&
                    current != null && SamePlacement(current.Data, data))
                {
                    continue;
                }

                PlacedBuildObjectData before = current != null ? current.Data.Clone() : null;
                if (current != null) Destroy(current.gameObject);
                instances[data.id] = CreateVisual(data);
                changes.Add(new LayoutDelta(before, data.Clone()));
            }

            var removedIds = new List<string>();
            foreach (KeyValuePair<string, MarketBuildObject> item in instances)
            {
                if (desiredIds.Contains(item.Key)) continue;
                if (item.Value != null)
                {
                    changes.Add(new LayoutDelta(item.Value.Data.Clone(), null));
                    Destroy(item.Value.gameObject);
                }
                removedIds.Add(item.Key);
            }
            for (int i = 0; i < removedIds.Count; i++) instances.Remove(removedIds[i]);

            RefreshAffectedFences(changes);
            RebuildOccupation();
            deepSnow?.ApplyLayoutChanges(layout, changes);
            forest?.ApplyLayoutChanges(layout, changes);
            RefreshLighting();
            RefreshNavigation();
            FrameDiagnostics.RecordBuildAction(
                (float)((Time.realtimeSinceStartupAsDouble - rebuildStartedAt) * 1000.0));
        }

        private MarketBuildObject CreateVisual(PlacedBuildObjectData data)
        {
            return BuildObjectVisual.Create(
                data,
                scene.Palette,
                layoutRoot,
                false,
                scene.SnowCapMaterial,
                BuildFenceJoins(data, data.id));
        }

        private void RebuildOccupation()
        {
            occupied.Clear();
            pathCells.Clear();
            for (int i = 0; i < layout.objects.Count; i++)
            {
                if (layout.objects[i] != null) AddToOccupation(layout.objects[i]);
            }
        }

        private void RefreshAffectedFences(IReadOnlyList<LayoutDelta> changes)
        {
            if (changes == null || changes.Count == 0) return;

            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData fence = layout.objects[i];
                if (fence == null || fence.type != BuildObjectType.Fence ||
                    !IsFenceAffectedBy(fence, changes))
                {
                    continue;
                }

                if (instances.TryGetValue(fence.id, out MarketBuildObject previous) && previous != null)
                {
                    Destroy(previous.gameObject);
                }
                instances[fence.id] = CreateVisual(fence);
            }
        }

        private bool IsFenceAffectedBy(PlacedBuildObjectData fence, IReadOnlyList<LayoutDelta> changes)
        {
            foreach (Vector2Int fenceCell in EnumerateCells(fence))
            {
                for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
                {
                    if (TouchesFenceCell(changes[changeIndex].Before, fenceCell) ||
                        TouchesFenceCell(changes[changeIndex].After, fenceCell))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TouchesFenceCell(PlacedBuildObjectData data, Vector2Int fenceCell)
        {
            if (data == null) return false;
            foreach (Vector2Int cell in EnumerateCells(data))
            {
                if (Mathf.Abs(cell.x - fenceCell.x) <= 1 && Mathf.Abs(cell.y - fenceCell.y) <= 1)
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshLighting()
        {
            int lightBudget = scene != null && scene.Quality != null
                ? scene.Quality.CurrentWarmLightBudget
                : 4;
            var lights = new List<Light>(lightBudget);
            var renderers = new List<Renderer>(24);
            foreach (MarketBuildObject item in instances.Values)
            {
                for (int i = 0; i < item.WarmLights.Count; i++)
                {
                    Light light = item.WarmLights[i];
                    if (light == null) continue;
                    if (lights.Count < lightBudget)
                    {
                        lights.Add(light);
                    }
                    else
                    {
                        light.enabled = false;
                    }
                }
                for (int i = 0; i < item.WarmRenderers.Count; i++) renderers.Add(item.WarmRenderers[i]);
            }
            lighting?.SetDynamicWarmSources(lights.ToArray(), renderers.ToArray());
        }

        private void OnQualityChanged(QualityLevel level)
        {
            forest?.SetHighDetail(level == QualityLevel.High, layout);
            RefreshLighting();
        }

        private void RefreshNavigation()
        {
            if (paths == null) return;
            HashSet<Vector2Int> connectedRoads = BuildConnectedRoadNetwork();
            paths.SetRoadNetwork(connectedRoads, GridSize);
            var obstacles = new List<PathSystem.NavigationObstacle>();
            var interests = new List<PathSystem.NamedWaypoint>();
            var interestRoutes = new List<PathSystem.NamedRoute>();
            foreach (KeyValuePair<string, MarketBuildObject> pair in instances)
            {
                MarketBuildObject item = pair.Value;
                BuildObjectDefinition definition = item.Definition;
                PlacedBuildObjectData data = item.Data;
                Vector2Int size = definition.RotatedFootprint(data.rotation);
                if (definition.BlocksNavigation)
                {
                    obstacles.Add(new PathSystem.NavigationObstacle(
                        data.Position,
                        new Vector2(size.x * GridSize * 0.5f, size.y * GridSize * 0.5f)));
                }
                if (definition.Interest == BuildInterest.None) continue;
                if (!paths.IsRoadCell(GetAccessCell(data))) continue;
                Vector3 front = data.Position + data.Rotation * new Vector3(0f, 0f, -(size.y * GridSize * 0.5f + 0.36f));
                front.y = 0.55f;
                string waypointName = "build-" + data.id;
                interests.Add(new PathSystem.NamedWaypoint(
                    waypointName,
                    front,
                    new[] { NPCRole.Visitor, NPCRole.StallOperator },
                    new[] { NPCState.VisitingStand, NPCState.Observing }));
                interestRoutes.Add(new PathSystem.NamedRoute(
                    "visit-" + data.id,
                    new[] { "west-entry", waypointName },
                    new[] { NPCRole.Visitor, NPCRole.StallOperator },
                    new[] { NPCState.VisitingStand, NPCState.Observing }));
            }
            paths.SetDynamicLayout(interests.ToArray(), interestRoutes.ToArray(), obstacles.ToArray());
        }

        private HashSet<Vector2Int> BuildConnectedRoadNetwork()
        {
            var allRoads = new HashSet<Vector2Int>(pathCells);
            int minX = Mathf.RoundToInt(BuildMinX / GridSize);
            int maxX = Mathf.RoundToInt(BuildMaxX / GridSize);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = -5; z <= -3; z++) allRoads.Add(new Vector2Int(x, z));
            }

            var connected = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (Vector2Int cell in allRoads)
            {
                if (!IsPublicAccessCell(cell) || !connected.Add(cell)) continue;
                queue.Enqueue(cell);
            }
            Vector2Int[] directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                for (int index = 0; index < directions.Length; index++)
                {
                    Vector2Int next = current + directions[index];
                    if (allRoads.Contains(next) && connected.Add(next)) queue.Enqueue(next);
                }
            }
            return connected;
        }

        private void ValidatePreview()
        {
            previewIsValid = preview != null && IsValid(preview.Data, original != null ? original.id : null, out validationMessage);
            if (preview == null)
            {
                safetyOverlay?.Clear();
                return;
            }
            Renderer[] renderers = preview.GetComponentsInChildren<Renderer>();
            Color tint = previewIsValid ? new Color(0.72f, 0.93f, 0.78f, 0.48f) : new Color(0.94f, 0.55f, 0.50f, 0.52f);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].materials;
                for (int material = 0; material < materials.Length; material++)
                {
                    if (materials[material] != null && materials[material].HasProperty("_BaseColor"))
                    {
                        materials[material].SetColor("_BaseColor", tint);
                    }
                }
            }
            UpdateSafetyZones();
        }

        private void UpdateSafetyZones()
        {
            if (preview == null || preview.Data.type == BuildObjectType.PathTile)
            {
                safetyOverlay?.Clear();
                return;
            }

            var selectedCells = new HashSet<Vector2Int>();
            AddSafetyCells(preview.Data, selectedCells);
            var conflictingCells = new HashSet<Vector2Int>();
            string ignoredId = original != null ? original.id : null;
            foreach (PlacedBuildObjectData item in layout.objects)
            {
                if (item == null || item.id == ignoredId || item.type == BuildObjectType.PathTile) continue;
                var itemCells = new HashSet<Vector2Int>();
                AddSafetyCells(item, itemCells);
                if (selectedCells.Overlaps(itemCells)) conflictingCells.UnionWith(itemCells);
            }
            safetyOverlay?.SetZones(selectedCells, conflictingCells);
        }

        private void AddSafetyCells(PlacedBuildObjectData data, HashSet<Vector2Int> cells)
        {
            foreach (Vector2Int cell in EnumerateCells(data)) cells.Add(cell);
            if (BuildObjectLibrary.Get(data.type).Interest != BuildInterest.None)
            {
                cells.Add(GetAccessCell(data));
            }
        }

        private bool IsValid(PlacedBuildObjectData candidate, string ignoredId, out string reason)
        {
            reason = null;
            if (candidate == null) { reason = "Kein Objekt"; return false; }
            BuildObjectDefinition definition = BuildObjectLibrary.Get(candidate.type);
            if (candidate.type == BuildObjectType.PathTile && !IsPathConnected(candidate))
            {
                reason = "Neue Wege beginnen am öffentlichen Weg oder schließen an einen Pfad an";
                return false;
            }
            if (definition.Interest != BuildInterest.None && IsOccupiedByOther(GetAccessCell(candidate), ignoredId))
            {
                reason = "Der Zugang zum Objekt muss frei bleiben";
                return false;
            }
            foreach (Vector2Int cell in EnumerateCells(candidate))
            {
                Vector3 center = new Vector3(cell.x * GridSize, 0f, cell.y * GridSize);
                if (center.x < BuildMinX || center.x > BuildMaxX || center.z < BuildMinZ || center.z > BuildMaxZ)
                {
                    reason = "Ausserhalb des Baubereichs";
                    return false;
                }
                if (IsPublicAccessCell(cell) && !definition.MayStandOnPath)
                {
                    reason = "Der öffentliche Weg bleibt frei";
                    return false;
                }
                if (IsOccupiedByOther(cell, ignoredId))
                {
                    reason = "Objekte dürfen sich nicht überschneiden";
                    return false;
                }
                if (pathCells.Contains(cell) && candidate.type != BuildObjectType.PathTile && !definition.MayStandOnPath)
                {
                    reason = "Laufweg kann nicht überbaut werden";
                    return false;
                }
                foreach (PlacedBuildObjectData existing in layout.objects)
                {
                    if (existing == null || existing.id == ignoredId ||
                        BuildObjectLibrary.Get(existing.type).Interest == BuildInterest.None)
                    {
                        continue;
                    }
                    if (cell == GetAccessCell(existing))
                    {
                        reason = "Der Zugang eines anderen Objekts muss frei bleiben";
                        return false;
                    }
                }
            }
            return true;
        }

        private static Vector2Int GetAccessCell(PlacedBuildObjectData data)
        {
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Vector2Int size = definition.RotatedFootprint(data.rotation);
            Vector3 access = data.Position + data.Rotation * new Vector3(0f, 0f, -(size.y * GridSize * 0.5f + 0.36f));
            return new Vector2Int(Mathf.RoundToInt(access.x / GridSize), Mathf.RoundToInt(access.z / GridSize));
        }

        private bool IsPathConnected(PlacedBuildObjectData candidate)
        {
            foreach (Vector2Int cell in EnumerateCells(candidate))
            {
                if (IsPublicAccessCell(cell)) return true;
                if (pathCells.Contains(cell + Vector2Int.left) || pathCells.Contains(cell + Vector2Int.right) ||
                    pathCells.Contains(cell + Vector2Int.up) || pathCells.Contains(cell + Vector2Int.down))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A Manor-Lords-style road interaction: the first click anchors a start point, mouse
        /// or touch movement previews the routed road, and the second click commits that route.
        /// </summary>
        private void HandleRoadClick(Vector3 point)
        {
            if (!hasRoadStart)
            {
                if (!TryBuildRoadFootprint(point, point, out _, out string startReason))
                {
                    validationMessage = startReason;
                    return;
                }
                roadStart = point;
                hasRoadStart = true;
                UpdateRoadPreview(point);
                validationMessage = "Endpunkt wählen – die Vorschau folgt dem Zeiger";
                return;
            }

            if (TryCreateRoad(roadStart, point, out string reason))
            {
                hasRoadStart = false;
                hasRoadPreviewEnd = false;
                roadPreviewCells.Clear();
                if (roadPreview != null) roadPreview.Clear();
                validationMessage = "Straße angelegt – nächsten Startpunkt setzen";
            }
            else
            {
                validationMessage = reason;
            }
        }

        private bool TryCreateRoad(Vector3 start, Vector3 end, out string reason)
        {
            if (!TryBuildRoadFootprint(start, end, out HashSet<Vector2Int> cells, out reason)) return false;

            int added = 0;
            foreach (Vector2Int cell in cells)
            {
                if (pathCells.Contains(cell)) continue;
                var data = NewEntry(BuildObjectLibrary.Get(BuildObjectType.PathTile),
                    new Vector3(cell.x * GridSize, 0f, cell.y * GridSize));
                layout.objects.Add(data);
                Record(new BuildLayoutAction(null, data.Clone()));
                added++;
            }
            if (added == 0)
            {
                reason = "Die Straße liegt bereits vollständig";
                return false;
            }
            RebuildLayout();
            SaveLayout();
            reason = null;
            return true;
        }

        private void UpdateRoadPreview(Vector3 end)
        {
            if (hasRoadPreviewEnd && (roadPreviewEnd - end).sqrMagnitude < 0.0001f) return;
            hasRoadPreviewEnd = true;
            roadPreviewEnd = end;
            EnsureRoadPreview();
            if (TryBuildRoadFootprint(roadStart, end, out HashSet<Vector2Int> cells, out string reason))
            {
                roadPreviewCells.Clear();
                roadPreviewCells.AddRange(cells);
                roadPreview.SetCells(roadPreviewCells, true);
                validationMessage = "Endpunkt wählen – die Vorschau folgt dem Zeiger";
            }
            else
            {
                roadPreviewCells.Clear();
                roadPreview.Clear();
                validationMessage = reason;
            }
        }

        private bool TryBuildRoadFootprint(
            Vector3 start,
            Vector3 end,
            out HashSet<Vector2Int> cells,
            out string reason)
        {
            Vector2Int from = new Vector2Int(Mathf.RoundToInt(start.x / GridSize), Mathf.RoundToInt(start.z / GridSize));
            Vector2Int to = new Vector2Int(Mathf.RoundToInt(end.x / GridSize), Mathf.RoundToInt(end.z / GridSize));
            if (!TryFindRoadRoute(from, to, out List<Vector2Int> route, out reason))
            {
                cells = null;
                return false;
            }

            cells = BuildRoadFootprint(route);
            foreach (Vector2Int cell in cells)
            {
                if (!IsRoadCellValid(cell, out reason)) return false;
            }
            reason = null;
            return true;
        }

        private bool TryFindRoadRoute(
            Vector2Int start,
            Vector2Int end,
            out List<Vector2Int> route,
            out string reason)
        {
            HashSet<Vector2Int> blocked = BuildRoadObstacles();
            if (!IsRoadCenterAvailable(start, blocked) || !IsRoadCenterAvailable(end, blocked))
            {
                route = null;
                reason = "Start oder Ende liegt zu nah an einer Struktur";
                return false;
            }

            roadQueueScratch.Clear();
            roadVisitedScratch.Clear();
            roadPreviousScratch.Clear();
            roadVisitedScratch.Add(start);
            roadQueueScratch.Enqueue(start);
            bool found = false;
            while (roadQueueScratch.Count > 0)
            {
                Vector2Int current = roadQueueScratch.Dequeue();
                if (current == end)
                {
                    found = true;
                    break;
                }
                for (int index = 0; index < CardinalDirections.Length; index++)
                {
                    Vector2Int next = current + CardinalDirections[index];
                    if (!roadVisitedScratch.Add(next) || !IsRoadCenterAvailable(next, blocked)) continue;
                    roadPreviousScratch[next] = current;
                    roadQueueScratch.Enqueue(next);
                }
            }

            if (!found)
            {
                route = null;
                reason = "Für diese Straße gibt es keinen freien Weg um die Strukturen";
                return false;
            }

            roadRouteScratch.Clear();
            for (Vector2Int cursor = end; ; cursor = roadPreviousScratch[cursor])
            {
                roadRouteScratch.Add(cursor);
                if (cursor == start) break;
            }
            roadRouteScratch.Reverse();
            route = roadRouteScratch;
            reason = null;
            return true;
        }

        private HashSet<Vector2Int> BuildRoadObstacles()
        {
            roadBlockedScratch.Clear();
            foreach (PlacedBuildObjectData item in layout.objects)
            {
                if (item == null || item.type == BuildObjectType.PathTile) continue;
                // The route solver expands this set by the fixed road half-width. Generated
                // forest is deliberately absent from layout and therefore never blocks roads.
                foreach (Vector2Int cell in EnumerateCells(item)) roadBlockedScratch.Add(cell);
            }
            return roadBlockedScratch;
        }

        private HashSet<Vector2Int> BuildRoadFootprint(List<Vector2Int> route)
        {
            roadFootprintScratch.Clear();
            for (int index = 0; index < route.Count; index++)
            {
                Vector2Int direction = index < route.Count - 1
                    ? route[index + 1] - route[index]
                    : index > 0 ? route[index] - route[index - 1] : Vector2Int.right;
                bool vertical = direction.x == 0;
                for (int offset = -RoadHalfWidthCells; offset <= RoadHalfWidthCells; offset++)
                {
                    roadFootprintScratch.Add(vertical
                        ? new Vector2Int(route[index].x + offset, route[index].y)
                        : new Vector2Int(route[index].x, route[index].y + offset));
                }
            }
            return roadFootprintScratch;
        }

        private static bool IsRoadCenterAvailable(Vector2Int center, HashSet<Vector2Int> blocked)
        {
            for (int z = -RoadHalfWidthCells; z <= RoadHalfWidthCells; z++)
            {
                for (int x = -RoadHalfWidthCells; x <= RoadHalfWidthCells; x++)
                {
                    Vector2Int cell = new Vector2Int(center.x + x, center.y + z);
                    Vector3 position = new Vector3(cell.x * GridSize, 0f, cell.y * GridSize);
                    if (position.x < BuildMinX || position.x > BuildMaxX ||
                        position.z < BuildMinZ || position.z > BuildMaxZ || blocked.Contains(cell))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool TryGetRoadHoverPoint(out Vector3 point)
        {
            Vector2 screenPoint;
            if (Input.touchCount > 0) screenPoint = Input.GetTouch(0).position;
            else screenPoint = Input.mousePosition;
            if (IsOverBuildUi(screenPoint))
            {
                point = default;
                return false;
            }
            return TryGetGroundPoint(screenPoint, out point);
        }

        private void EnsureRoadPreview()
        {
            if (roadPreview != null) return;
            roadPreview = RoadPreviewOverlay.Create(layoutRoot);
        }

        private bool IsRoadCellValid(Vector2Int cell, out string reason)
        {
            reason = null;
            Vector3 center = new Vector3(cell.x * GridSize, 0f, cell.y * GridSize);
            if (center.x < BuildMinX || center.x > BuildMaxX || center.z < BuildMinZ || center.z > BuildMaxZ)
            {
                reason = "Die Straße liegt außerhalb des Baubereichs";
                return false;
            }
            if (IsOccupiedByOther(cell, null))
            {
                reason = "Straßen dürfen keine Struktur überbauen";
                return false;
            }
            return true;
        }

        private bool IsOccupiedByOther(Vector2Int cell, string ignoredId)
        {
            foreach (PlacedBuildObjectData item in layout.objects)
            {
                if (item == null || item.id == ignoredId || item.type == BuildObjectType.PathTile) continue;
                foreach (Vector2Int itemCell in EnumerateCells(item))
                {
                    if (itemCell == cell) return true;
                }
            }
            return false;
        }

        private void AddToOccupation(PlacedBuildObjectData data)
        {
            foreach (Vector2Int cell in EnumerateCells(data))
            {
                if (data.type == BuildObjectType.PathTile) pathCells.Add(cell);
                else occupied.Add(cell);
            }
        }

        private static bool IsPublicAccessCell(Vector2Int cell)
        {
            float z = cell.y * GridSize;
            return z >= -3.01f && z <= -1.79f;
        }

        private IEnumerable<Vector2Int> EnumerateCells(PlacedBuildObjectData data)
        {
            BuildObjectDefinition definition = BuildObjectLibrary.Get(data.type);
            Vector2Int size = definition.RotatedFootprint(data.rotation);
            int centerX = Mathf.RoundToInt(data.x / GridSize);
            int centerZ = Mathf.RoundToInt(data.z / GridSize);
            int minX = centerX - size.x / 2;
            int minZ = centerZ - size.y / 2;
            for (int z = 0; z < size.y; z++)
            {
                for (int x = 0; x < size.x; x++) yield return new Vector2Int(minX + x, minZ + z);
            }
        }

        private IReadOnlyList<FenceJoin> BuildFenceJoins(PlacedBuildObjectData fence, string ignoredId)
        {
            if (fence == null || fence.type != BuildObjectType.Fence) return null;

            var joins = new List<FenceJoin>(4);
            Vector2Int[] directions =
            {
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.left,
                Vector2Int.down
            };
            foreach (Vector2Int cell in EnumerateCells(fence))
            {
                for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
                {
                    Vector2Int direction = directions[directionIndex];
                    if (HasFenceAt(cell + direction, ignoredId))
                    {
                        joins.Add(new FenceJoin(cell, direction));
                    }
                }
            }
            return joins;
        }

        private bool HasFenceAt(Vector2Int cell, string ignoredId)
        {
            for (int index = 0; index < layout.objects.Count; index++)
            {
                PlacedBuildObjectData candidate = layout.objects[index];
                if (candidate == null || candidate.id == ignoredId || candidate.type != BuildObjectType.Fence) continue;
                foreach (Vector2Int candidateCell in EnumerateCells(candidate))
                {
                    if (candidateCell == cell) return true;
                }
            }
            return false;
        }

        private PlacedBuildObjectData NewEntry(BuildObjectDefinition definition, Vector3 position)
        {
            return new PlacedBuildObjectData
            {
                id = Guid.NewGuid().ToString("N"),
                type = definition.Type,
                x = position.x,
                z = position.z,
                rotation = 0
            };
        }

        private Vector3 GetDefaultPreviewPosition()
        {
            if (dioramaCamera != null && TryGetGroundPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f), out Vector3 point))
            {
                return Snap(point);
            }
            return Vector3.zero;
        }

        private static Vector3 Snap(Vector3 point)
        {
            return new Vector3(
                Mathf.Round(point.x / GridSize) * GridSize,
                0f,
                Mathf.Round(point.z / GridSize) * GridSize);
        }

        /// <summary>Moves stalls and benches so their service/access side lands on a nearby road.</summary>
        private void SnapStructureToPath(PlacedBuildObjectData data)
        {
            if (data == null || BuildObjectLibrary.Get(data.type).Interest == BuildInterest.None) return;
            Vector2Int access = GetAccessCell(data);
            if (!TryGetNearestPathCell(access, out Vector2Int pathCell)) return;
            data.x += (pathCell.x - access.x) * GridSize;
            data.z += (pathCell.y - access.y) * GridSize;
        }

        private bool TryGetNearestPathCell(Vector2Int origin, out Vector2Int nearest)
        {
            int bestDistance = int.MaxValue;
            nearest = default;
            foreach (Vector2Int cell in pathCells)
            {
                int distance = Mathf.Abs(cell.x - origin.x) + Mathf.Abs(cell.y - origin.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = cell;
                }
            }

            Vector2Int mainRoadCell = new Vector2Int(origin.x, Mathf.Clamp(origin.y, -5, -3));
            int mainRoadDistance = Mathf.Abs(mainRoadCell.y - origin.y);
            if (mainRoadDistance < bestDistance)
            {
                bestDistance = mainRoadDistance;
                nearest = mainRoadCell;
            }
            return bestDistance <= PathSnapRangeCells;
        }

        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            if (dioramaCamera != null)
            {
                Ray ray = dioramaCamera.ScreenPointToRay(screenPosition);
                Plane ground = new Plane(Vector3.up, Vector3.zero);
                if (ground.Raycast(ray, out float distance))
                {
                    point = ray.GetPoint(distance);
                    return true;
                }
            }
            point = default;
            return false;
        }

        private static bool TryGetPointerDown(out Vector2 position)
        {
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                position = Input.GetTouch(0).position;
                return true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                return true;
            }
            position = default;
            return false;
        }

        private static bool TryGetPointerHeld(out Vector2 position)
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    position = touch.position;
                    return true;
                }
            }
            if (Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                return true;
            }
            position = default;
            return false;
        }

        private static bool TryGetPointerUp(out Vector2 position)
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    position = touch.position;
                    return true;
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                position = Input.mousePosition;
                return true;
            }
            position = default;
            return false;
        }

        private static bool IsPointerPressed()
        {
            return Input.touchCount > 0 || Input.GetMouseButton(0);
        }

        private static bool SamePlacement(PlacedBuildObjectData first, PlacedBuildObjectData second)
        {
            return first != null && second != null && first.type == second.type &&
                   Mathf.Approximately(first.x, second.x) && Mathf.Approximately(first.z, second.z) &&
                   first.rotation == second.rotation;
        }

        private bool IsOverBuildUi(Vector2 screenPosition)
        {
            return screenPosition.y <= Screen.height * 0.38f;
        }

        private void Record(BuildLayoutAction action)
        {
            undo.Add(action);
            if (undo.Count > HistoryLimit) undo.RemoveAt(0);
            redo.Clear();
        }

        private void ApplyAction(PlacedBuildObjectData desired, PlacedBuildObjectData counterpart)
        {
            if (desired == null)
            {
                int removeIndex = counterpart != null ? FindIndex(counterpart.id) : -1;
                if (removeIndex >= 0) layout.objects.RemoveAt(removeIndex);
                return;
            }
            int index = FindIndex(desired.id);
            if (index >= 0) layout.objects[index] = desired.Clone();
            else layout.objects.Add(desired.Clone());
        }

        private int FindIndex(string id)
        {
            for (int i = 0; i < layout.objects.Count; i++)
            {
                if (layout.objects[i] != null && layout.objects[i].id == id) return i;
            }
            return -1;
        }

        private void SaveLayout()
        {
            if (layout == null) return;
            layout.preferences.lastCategory = category.ToString();
            BuildLayoutStore.Save(layout);
        }

        private void SetGridVisible(bool visible)
        {
            if (!visible)
            {
                if (gridOverlay != null) gridOverlay.gameObject.SetActive(false);
                return;
            }
            if (gridOverlay == null)
            {
                var grid = new GameObject("Baumodus Raster");
                grid.transform.SetParent(layoutRoot, false);
                gridOverlay = grid.AddComponent<BuildGridOverlay>();
                gridOverlay.Configure(BuildMinX, BuildMaxX, BuildMinZ, BuildMaxZ, GridSize);
            }
            gridOverlay.gameObject.SetActive(true);
        }

        private void OnGUI()
        {
            if (!configured) return;
            if (!buildMode)
            {
                if (!buildButtonVisible) return;
                float scale = GetUiScale();
                Rect safe = Screen.safeArea;
                Rect button = new Rect(safe.xMax - 126f * scale, safe.y + 16f * scale, 110f * scale, 48f * scale);
                if (GUI.Button(button, "Bauen")) EnterBuildMode();
                return;
            }

            DrawBuildUi();
        }

        private void DrawBuildUi()
        {
            float scale = GetUiScale();
            Rect safe = Screen.safeArea;
            float height = Mathf.Min(safe.height * 0.34f, 264f * scale);
            Rect area = new Rect(safe.x + 8f * scale, safe.yMax - height - 8f * scale, safe.width - 16f * scale, height);
            GUI.Box(area, GUIContent.none);
            float pad = 12f * scale;
            float touch = 46f * scale;
            Rect header = new Rect(area.x + pad, area.y + pad, area.width - 2f * pad, touch);
            if (GUI.Button(new Rect(header.x, header.y, 78f * scale, header.height), "Fertig")) ExitBuildMode();
            if (GUI.Button(new Rect(header.x + 86f * scale, header.y, 96f * scale, header.height),
                deleteToolActive ? "Abbruch" : "Löschen")) ToggleDeleteTool();
            GUI.enabled = undo.Count > 0 && preview == null && !deleteToolActive;
            if (GUI.Button(new Rect(header.xMax - 156f * scale, header.y, 74f * scale, header.height), "Undo")) Undo();
            GUI.enabled = redo.Count > 0 && preview == null && !deleteToolActive;
            if (GUI.Button(new Rect(header.xMax - 76f * scale, header.y, 76f * scale, header.height), "Redo")) Redo();
            GUI.enabled = true;

            float y = header.yMax + 8f * scale;
            float categoryWidth = (area.width - pad * 2f - 2f * 6f * scale) / 3f;
            foreach (BuildCategory value in (BuildCategory[])Enum.GetValues(typeof(BuildCategory)))
            {
                string label = value == BuildCategory.Stalls ? "Stände" : value == BuildCategory.Paths ? "Wege" : "Deko";
                Rect categoryButton = new Rect(area.x + pad + ((int)value * (categoryWidth + 6f * scale)), y, categoryWidth, 40f * scale);
                if (GUI.Button(categoryButton, label))
                {
                    ExitDeleteTool();
                    category = value;
                    palettePage = 0;
                    selectedDefinition = null;
                    CancelPreview();
                }
            }
            y += 48f * scale;

            if (deleteToolActive)
            {
                GUI.Label(new Rect(area.x + pad, y + 12f * scale, area.width - 2f * pad, 54f * scale),
                    string.IsNullOrEmpty(deleteMessage) ? "Struktur oder Straße auswählen" : deleteMessage);
                return;
            }

            var categoryEntries = new List<BuildObjectDefinition>();
            foreach (BuildObjectDefinition definition in BuildObjectLibrary.GetCategory(category)) categoryEntries.Add(definition);
            const int PageSize = 4;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(categoryEntries.Count / (float)PageSize));
            palettePage = Mathf.Clamp(palettePage, 0, pageCount - 1);
            float objectWidth = (area.width - pad * 2f - 8f * scale) * 0.5f;
            int first = palettePage * PageSize;
            int last = Mathf.Min(first + PageSize, categoryEntries.Count);
            for (int index = first; index < last; index++)
            {
                int visibleIndex = index - first;
                float rowY = y + (visibleIndex / 2) * (touch + 6f * scale);
                Rect objectButton = new Rect(
                    area.x + pad + (visibleIndex % 2) * (objectWidth + 8f * scale),
                    rowY,
                    objectWidth,
                    touch);
                if (GUI.Button(objectButton, categoryEntries[index].DisplayName)) BeginNew(categoryEntries[index]);
            }

            float pageY = y + touch * 2f + 10f * scale;
            if (pageCount > 1)
            {
                GUI.enabled = palettePage > 0;
                if (GUI.Button(new Rect(area.x + pad, pageY, touch, 34f * scale), "‹")) palettePage--;
                GUI.enabled = palettePage < pageCount - 1;
                if (GUI.Button(new Rect(area.xMax - pad - touch, pageY, touch, 34f * scale), "›")) palettePage++;
                GUI.enabled = true;
                GUI.Label(new Rect(area.x + pad + touch + 8f * scale, pageY, area.width - 2f * (pad + touch + 8f * scale), 34f * scale),
                    (palettePage + 1) + " / " + pageCount, GUI.skin.label);
            }

            float controlsY = area.yMax - touch - pad;
            float actionWidth = (area.width - pad * 2f - 2f * 8f * scale) / 3f;
            if (roadPlacementActive)
            {
                GUI.Label(new Rect(area.x + pad, controlsY - 28f * scale, area.width - 2f * pad, 24f * scale),
                    !string.IsNullOrEmpty(validationMessage)
                        ? validationMessage
                        : hasRoadStart ? "Endpunkt der Straße im Gelände wählen" : "Startpunkt der Straße im Gelände wählen");
                if (GUI.Button(new Rect(area.xMax - pad - actionWidth, controlsY, actionWidth, touch), "Abbruch")) CancelPreview();
                return;
            }
            if (preview == null) return;
            if (GUI.Button(new Rect(area.x + pad, controlsY, actionWidth, touch), "Drehen")) RotatePreview();
            if (original != null && GUI.Button(new Rect(area.x + pad + actionWidth + 8f * scale, controlsY, actionWidth, touch), "Löschen")) DeleteSelected();
            if (GUI.Button(new Rect(area.xMax - pad - actionWidth, controlsY, actionWidth, touch), "Abbruch")) CancelPreview();
            GUI.enabled = previewIsValid;
            if (GUI.Button(new Rect(area.xMax - pad - actionWidth, controlsY - touch - 6f * scale, actionWidth, touch), "Setzen")) ConfirmPreview();
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(validationMessage))
            {
                GUI.Label(new Rect(area.x + pad, controlsY - 28f * scale, area.width - actionWidth - 2f * pad, 24f * scale), validationMessage);
            }
        }

        private static float GetUiScale()
        {
            // Values in the build palette are specified in Android dp. This keeps the 46-dp
            // controls comfortably tappable instead of merely scaling with pixel resolution.
            return Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 160f : 1f);
        }

        private void OnDestroy()
        {
            if (scene != null && scene.Quality != null) scene.Quality.QualityChanged -= OnQualityChanged;
            deepSnow?.Clear();
            forest?.Clear();
        }

        private sealed class BuildLayoutAction
        {
            public readonly PlacedBuildObjectData Before;
            public readonly PlacedBuildObjectData After;
            public BuildLayoutAction(PlacedBuildObjectData before, PlacedBuildObjectData after)
            {
                Before = before;
                After = after;
            }
        }
    }

    /// <summary>
    /// Non-persistent vegetation for the buildable snowfield.  It is regenerated from the
    /// confirmed layout, so a tree can never make ground permanently unavailable: structures
    /// and roads simply displace it on the next layout refresh.
    /// </summary>
    internal sealed class DynamicForestController
    {
        // The forest frames the central market and remains decorative, so a placed object
        // simply clears its own space on the next layout refresh.
        private const int MinX = -120;
        private const int MaxX = 120;
        private const int MinZ = -120;
        private const int MaxZ = 120;
        // One cell only prevents duplicate roots. Crowns are intentionally allowed to overlap
        // so the landscape reads as a proper dense forest instead of an orchard.
        private const int TreeStepCells = 1;
        private const int MaxTreeCount = 3000;

        private readonly Transform root;
        private readonly Material[] palette;
        private readonly Material snowCapMaterial;
        private readonly List<MarketBuildObject> trees = new List<MarketBuildObject>(MaxTreeCount);

        public DynamicForestController(Transform parent, Material[] sourcePalette, Material snowCap)
        {
            root = new GameObject("Dynamischer Wald").transform;
            root.SetParent(parent, false);
            palette = sourcePalette;
            snowCapMaterial = snowCap;
        }

        public void Rebuild(BuildLayoutData layout)
        {
            Clear();
            if (layout == null || root == null) return;

            var placed = new List<Vector2Int>(MaxTreeCount);
            uint state = 0x4e554a79u;
            const int attempts = 50000;
            for (int attempt = 0; attempt < attempts && trees.Count < MaxTreeCount; attempt++)
            {
                int x = RandomRange(ref state, MinX, MaxX);
                int z = RandomRange(ref state, MinZ, MaxZ);
                var cell = new Vector2Int(x, z);
                // A broad central opening keeps the established market readable. Outside it,
                // random grid cells and a spacing rule form irregular, dense forest clusters.
                if (IsCentralMarketOpening(cell)) continue;
                TryAddTree(cell, layout, placed);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < trees.Count; i++)
            {
                if (trees[i] == null) continue;
                trees[i].gameObject.SetActive(false);
                UnityEngine.Object.Destroy(trees[i].gameObject);
            }
            trees.Clear();
        }

        private void TryAddTree(Vector2Int cell, BuildLayoutData layout, List<Vector2Int> placed)
        {
            if (trees.Count >= MaxTreeCount || !CanPlaceTree(cell, layout, placed)) return;
            var data = new PlacedBuildObjectData
            {
                id = "generated-tree-" + cell.x + "-" + cell.y,
                type = BuildObjectType.SmallFir,
                x = cell.x * BuildModeController.GridSize,
                z = cell.y * BuildModeController.GridSize,
                rotation = StableHash(cell) & 3,
                variant = BuildObjectVisual.GeneratedForestVariant
            };
            MarketBuildObject tree = BuildObjectVisual.Create(data, palette, root, false, snowCapMaterial);
            tree.name = "Generierter Waldbaum";
            // Sink roots into the raised snow while leaving the first trunk section visible.
            tree.transform.localPosition += Vector3.up * (VoxelVolume.GridSize * 2f);
            float widthScale = 0.78f + (StableHash(cell + new Vector2Int(17, 31)) % 43) * 0.01f;
            float heightScale = 0.78f + (StableHash(cell + new Vector2Int(43, 11)) % 78) * 0.01f;
            tree.transform.localScale = new Vector3(widthScale, heightScale, widthScale);
            BoxCollider collider = tree.GetComponent<BoxCollider>();
            if (collider != null) collider.enabled = false;
            trees.Add(tree);
            placed.Add(cell);
        }

        private static bool CanPlaceTree(Vector2Int cell, BuildLayoutData layout, List<Vector2Int> placed)
        {
            // The original public road and its shoulders remain visibly clear.
            if (cell.y >= -7 && cell.y <= -1) return false;

            for (int i = 0; i < placed.Count; i++)
            {
                if (Mathf.Abs(placed[i].x - cell.x) < TreeStepCells &&
                    Mathf.Abs(placed[i].y - cell.y) < TreeStepCells)
                {
                    return false;
                }
            }

            Vector2Int treeSize = BuildObjectLibrary.Get(BuildObjectType.SmallFir).Footprint;
            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData item = layout.objects[i];
                if (item == null) continue;
                Vector2Int itemSize = BuildObjectLibrary.Get(item.type).RotatedFootprint(item.rotation);
                int itemX = Mathf.RoundToInt(item.x / BuildModeController.GridSize);
                int itemZ = Mathf.RoundToInt(item.z / BuildModeController.GridSize);
                int clearanceX = Mathf.CeilToInt((treeSize.x + itemSize.x) * 0.5f) + 1;
                int clearanceZ = Mathf.CeilToInt((treeSize.y + itemSize.y) * 0.5f) + 1;
                if (Mathf.Abs(itemX - cell.x) <= clearanceX && Mathf.Abs(itemZ - cell.y) <= clearanceZ)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsLargeOpenGap(Vector2Int cell, BuildLayoutData layout)
        {
            const int gapRadiusCells = 8;
            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData item = layout.objects[i];
                if (item == null || item.type == BuildObjectType.PathTile) continue;
                int x = Mathf.RoundToInt(item.x / BuildModeController.GridSize);
                int z = Mathf.RoundToInt(item.z / BuildModeController.GridSize);
                if (Mathf.Abs(x - cell.x) < gapRadiusCells && Mathf.Abs(z - cell.y) < gapRadiusCells)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsCentralMarketOpening(Vector2Int cell)
        {
            float normalizedX = cell.x / 23f;
            float normalizedZ = (cell.y - 2f) / 18f;
            return normalizedX * normalizedX + normalizedZ * normalizedZ < 1f;
        }

        private static int RandomRange(ref uint state, int minimum, int maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int span = Mathf.Max(1, maximum - minimum + 1);
            return minimum + (int)(state % (uint)span);
        }

        private static int StableHash(Vector2Int cell)
        {
            unchecked
            {
                int hash = cell.x * 73856093 ^ cell.y * 19349663;
                return hash == int.MinValue ? 0 : Mathf.Abs(hash);
            }
        }
    }

    /// <summary>
    /// Raised snow outside the cleared two-cell perimeter of structures and roads. It remains
    /// non-persistent and is regenerated with the editable layout, just like the forest.
    /// </summary>
    internal sealed class DynamicDeepSnowController
    {
        private static readonly int MinX = Mathf.RoundToInt(BuildModeController.BuildMinX / BuildModeController.GridSize);
        private static readonly int MaxX = Mathf.RoundToInt(BuildModeController.BuildMaxX / BuildModeController.GridSize);
        private static readonly int MinZ = Mathf.RoundToInt(BuildModeController.BuildMinZ / BuildModeController.GridSize);
        private static readonly int MaxZ = Mathf.RoundToInt(BuildModeController.BuildMaxZ / BuildModeController.GridSize);
        private const int ClearanceCells = 2;
        private const int VoxelsPerBuildCell = 4;
        private const int SnowHeightVoxels = 2;

        private readonly Transform root;
        private readonly Material[] palette;
        private VoxelAssetGenerator surface;

        public DynamicDeepSnowController(Transform parent, Material[] sourcePalette)
        {
            root = new GameObject("Tiefschnee").transform;
            root.SetParent(parent, false);
            palette = sourcePalette;
        }

        public void Rebuild(BuildLayoutData layout)
        {
            Clear();
            if (layout == null || root == null) return;

            int cellWidth = MaxX - MinX + 1;
            int cellDepth = MaxZ - MinZ + 1;
            // Horizontal transform scaling maps one compact voxel to one 0.60 m build cell.
            // It gives the full build area a continuous deep-snow mesh without allocating a
            // 29-million-voxel volume just to repeat the same four-by-four cell pattern.
            var snow = new VoxelVolume(new Vector3Int(cellWidth, SnowHeightVoxels, cellDepth));

            for (int z = MinZ; z <= MaxZ; z++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    var cell = new Vector2Int(x, z);
                    if (!IsDeepSnowCell(cell, layout)) continue;
                    int localX = x - MinX;
                    int localZ = z - MinZ;
                    snow.FillCuboid(
                        localX,
                        0,
                        localZ,
                        1,
                        SnowHeightVoxels,
                        1,
                        GetSnowVariation(cell));
                }
            }

            surface = VoxelAssetGenerator.Create("Erhöhter Tiefschnee", snow, palette, root, true);
            surface.transform.localPosition = new Vector3(
                MinX * BuildModeController.GridSize - BuildModeController.GridSize * 0.5f,
                0.30f,
                MinZ * BuildModeController.GridSize - BuildModeController.GridSize * 0.5f);
            surface.transform.localScale = new Vector3(VoxelsPerBuildCell, 1f, VoxelsPerBuildCell);
            MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        public void Clear()
        {
            if (surface == null) return;
            surface.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(surface.gameObject);
            surface = null;
        }

        private static bool IsDeepSnowCell(Vector2Int cell, BuildLayoutData layout)
        {
            // The permanent main road has the same two-cell cleared shoulder as placed paths.
            if (cell.y >= -5 - ClearanceCells && cell.y <= -3 + ClearanceCells) return false;

            for (int index = 0; index < layout.objects.Count; index++)
            {
                PlacedBuildObjectData item = layout.objects[index];
                if (item == null) continue;
                BuildObjectDefinition definition = BuildObjectLibrary.Get(item.type);
                Vector2Int size = definition.RotatedFootprint(item.rotation);
                int centerX = Mathf.RoundToInt(item.x / BuildModeController.GridSize);
                int centerZ = Mathf.RoundToInt(item.z / BuildModeController.GridSize);
                int minX = centerX - size.x / 2 - ClearanceCells;
                int maxX = centerX + (size.x - 1) / 2 + ClearanceCells;
                int minZ = centerZ - size.y / 2 - ClearanceCells;
                int maxZ = centerZ + (size.y - 1) / 2 + ClearanceCells;
                if (cell.x >= minX && cell.x <= maxX && cell.y >= minZ && cell.y <= maxZ)
                {
                    return false;
                }
            }
            return true;
        }

        private static VoxelMaterialSlot GetSnowVariation(Vector2Int cell)
        {
            // The three-cell patches keep the tonal variation organic rather than checkerboard-like.
            int patchX = Mathf.FloorToInt(cell.x / 3f);
            int patchZ = Mathf.FloorToInt(cell.y / 3f);
            unchecked
            {
                int hash = patchX * 92837111 ^ patchZ * 689287499;
                return (hash & 7) == 0 ? VoxelMaterialSlot.Slot1 : VoxelMaterialSlot.Slot0;
            }
        }
    }

    /// <summary>Single transparent mesh used while a road endpoint is being chosen.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class RoadPreviewOverlay : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;
        private Vector3[] vertices = Array.Empty<Vector3>();
        private int[] triangles = Array.Empty<int>();

        public static RoadPreviewOverlay Create(Transform parent)
        {
            var preview = new GameObject("Straßenvorschau");
            preview.transform.SetParent(parent, false);
            return preview.AddComponent<RoadPreviewOverlay>();
        }

        private void Awake()
        {
            mesh = new Mesh { name = "Straßenvorschau Mesh" };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { renderQueue = 3001 };
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            SetColor(new Color(0.48f, 0.9f, 0.66f, 0.68f));
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetCells(IReadOnlyList<Vector2Int> cells, bool valid)
        {
            if (mesh == null) return;
            if (cells == null || cells.Count == 0)
            {
                Clear();
                return;
            }
            int vertexCount = cells.Count * 4;
            int triangleCount = cells.Count * 6;
            EnsureCapacity(vertexCount, triangleCount);
            float half = BuildModeController.GridSize * 0.5f;
            for (int index = 0; index < cells.Count; index++)
            {
                Vector2Int cell = cells[index];
                float x = cell.x * BuildModeController.GridSize;
                float z = cell.y * BuildModeController.GridSize;
                int vertex = index * 4;
                vertices[vertex] = new Vector3(x - half, 0.47f, z - half);
                vertices[vertex + 1] = new Vector3(x + half, 0.47f, z - half);
                vertices[vertex + 2] = new Vector3(x + half, 0.47f, z + half);
                vertices[vertex + 3] = new Vector3(x - half, 0.47f, z + half);
                int triangle = index * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }
            mesh.Clear();
            mesh.SetVertices(vertices, 0, vertexCount);
            mesh.SetTriangles(triangles, 0, triangleCount, 0, false);
            mesh.RecalculateBounds();
            SetColor(valid ? new Color(0.48f, 0.9f, 0.66f, 0.68f) : new Color(0.94f, 0.38f, 0.32f, 0.68f));
        }

        public void Clear()
        {
            if (mesh != null) mesh.Clear();
        }

        private void SetColor(Color color)
        {
            if (material == null) return;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private void EnsureCapacity(int vertexCount, int triangleCount)
        {
            if (vertices.Length < vertexCount)
            {
                vertices = new Vector3[Mathf.NextPowerOfTwo(vertexCount)];
            }
            if (triangles.Length < triangleCount)
            {
                triangles = new int[Mathf.NextPowerOfTwo(triangleCount)];
            }
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }

    /// <summary>
    /// Shows the reserved footprint and access cell of the structure currently being placed.
    /// Conflicting placed structures are rendered separately so an overlap is immediately clear.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class StructureSafetyOverlay : MonoBehaviour
    {
        private Mesh mesh;
        private Material selectedMaterial;
        private Material conflictMaterial;

        public static StructureSafetyOverlay Create(Transform parent)
        {
            var overlay = new GameObject("Struktur-Sicherheitszonen");
            overlay.transform.SetParent(parent, false);
            return overlay.AddComponent<StructureSafetyOverlay>();
        }

        private void Awake()
        {
            mesh = new Mesh { name = "Struktur-Sicherheitszonen Mesh" };
            GetComponent<MeshFilter>().sharedMesh = mesh;
            selectedMaterial = CreateMaterial(new Color(0.3f, 0.74f, 1f, 0.36f));
            conflictMaterial = CreateMaterial(new Color(1f, 0.25f, 0.2f, 0.48f));
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { selectedMaterial, conflictMaterial };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetZones(IEnumerable<Vector2Int> selectedCells, IEnumerable<Vector2Int> conflictingCells)
        {
            var selected = new List<Vector2Int>(selectedCells ?? Array.Empty<Vector2Int>());
            var conflicting = new List<Vector2Int>(conflictingCells ?? Array.Empty<Vector2Int>());
            int count = selected.Count + conflicting.Count;
            if (count == 0)
            {
                Clear();
                return;
            }

            var vertices = new Vector3[count * 4];
            var selectedTriangles = new int[selected.Count * 6];
            var conflictTriangles = new int[conflicting.Count * 6];
            int vertex = 0;
            FillCells(selected, vertices, selectedTriangles, ref vertex);
            FillCells(conflicting, vertices, conflictTriangles, ref vertex);
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(selectedTriangles, 0);
            mesh.SetTriangles(conflictTriangles, 1);
            mesh.RecalculateBounds();
        }

        public void Clear()
        {
            if (mesh != null) mesh.Clear();
        }

        private static void FillCells(
            List<Vector2Int> cells,
            Vector3[] vertices,
            int[] triangles,
            ref int vertex)
        {
            float half = BuildModeController.GridSize * 0.5f;
            for (int index = 0; index < cells.Count; index++)
            {
                float x = cells[index].x * BuildModeController.GridSize;
                float z = cells[index].y * BuildModeController.GridSize;
                vertices[vertex] = new Vector3(x - half, 0.475f, z - half);
                vertices[vertex + 1] = new Vector3(x + half, 0.475f, z - half);
                vertices[vertex + 2] = new Vector3(x + half, 0.475f, z + half);
                vertices[vertex + 3] = new Vector3(x - half, 0.475f, z + half);
                int triangle = index * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
                vertex += 4;
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { color = color, renderQueue = 3002 };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            return material;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (selectedMaterial != null) Destroy(selectedMaterial);
            if (conflictMaterial != null) Destroy(conflictMaterial);
        }
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class BuildGridOverlay : MonoBehaviour
    {
        private Material material;

        public void Configure(float minX, float maxX, float minZ, float maxZ, float spacing)
        {
            int columns = Mathf.RoundToInt((maxX - minX) / spacing);
            int rows = Mathf.RoundToInt((maxZ - minZ) / spacing);
            var vertices = new Vector3[(columns + rows + 2) * 2];
            int cursor = 0;
            for (int x = 0; x <= columns; x++)
            {
                float position = minX + x * spacing;
                vertices[cursor++] = new Vector3(position, 0.318f, minZ);
                vertices[cursor++] = new Vector3(position, 0.318f, maxZ);
            }
            for (int z = 0; z <= rows; z++)
            {
                float position = minZ + z * spacing;
                vertices[cursor++] = new Vector3(minX, 0.318f, position);
                vertices[cursor++] = new Vector3(maxX, 0.318f, position);
            }
            var mesh = new Mesh { name = "Baumodus Raster Mesh" };
            mesh.vertices = vertices;
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { color = new Color(0.35f, 0.46f, 0.52f, 0.36f), renderQueue = 3000 };
            GetComponent<MeshRenderer>().sharedMaterial = material;
            GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
            if (mesh != null) Destroy(mesh);
        }
    }
}
