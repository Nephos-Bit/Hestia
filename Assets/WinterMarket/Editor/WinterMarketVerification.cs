#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WinterMarket.Editor
{
    public static class WinterMarketVerification
    {
        [MenuItem("Wintermarkt/Run Functional Checks")]
        public static void RunFunctionalChecks()
        {
            var failures = new List<string>();
            CheckVoxelMeshing(failures);
            CheckTimePhases(failures);
            CheckRoleCoverage(failures);
            CheckSceneBoot(failures);
            CheckBuildFoundation(failures);
            CheckOfflineConfiguration(failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "WINTERMARKT_CHECKS_FAIL\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("WINTERMARKT_CHECKS_PASS checks=6 voxelMesh=timePhases=roles=sceneBoot=building=offline");
        }

        private static void CheckVoxelMeshing(List<string> failures)
        {
            var volume = new VoxelVolume(new Vector3Int(2, 1, 1));
            volume.FillCuboid(0, 0, 0, 2, 1, 1, VoxelMaterialSlot.Slot0);
            Mesh mesh = null;
            try
            {
                mesh = VoxelGreedyMesher.CreateMesh(volume, "Greedy Mesher Check");
                if (mesh.vertexCount != 24 || mesh.subMeshCount != 1 || mesh.triangles.Length != 36)
                {
                    failures.Add(
                        $"Greedy mesher did not merge a 2x1x1 cuboid as six quads " +
                        $"(vertices={mesh.vertexCount}, submeshes={mesh.subMeshCount}, indices={mesh.triangles.Length}).");
                }
            }
            catch (Exception exception)
            {
                failures.Add("Voxel mesher threw: " + exception.Message);
            }
            finally
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }

        private static void CheckTimePhases(List<string> failures)
        {
            var clockObject = new GameObject("Clock Test");
            try
            {
                TimeOfDayController clock = clockObject.AddComponent<TimeOfDayController>();
                ExpectPhase(clock, 5.49f, TimeOfDayPhase.Night, failures);
                ExpectPhase(clock, 5.5f, TimeOfDayPhase.EarlyMorning, failures);
                ExpectPhase(clock, 9.5f, TimeOfDayPhase.Day, failures);
                ExpectPhase(clock, 16.5f, TimeOfDayPhase.Evening, failures);
                ExpectPhase(clock, 20.5f, TimeOfDayPhase.Closing, failures);
                ExpectPhase(clock, 22.5f, TimeOfDayPhase.Closing, failures);
                ExpectPhase(clock, 23.5f, TimeOfDayPhase.Night, failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clockObject);
            }
        }

        private static void ExpectPhase(
            TimeOfDayController clock,
            float hour,
            TimeOfDayPhase expected,
            List<string> failures)
        {
            TimeOfDayPhase actual = clock.GetPhase(hour);
            if (actual != expected)
            {
                failures.Add($"Phase at {hour:F2} was {actual}, expected {expected}.");
            }
        }

        private static void CheckRoleCoverage(List<string> failures)
        {
            if (Enum.GetValues(typeof(NPCRole)).Length < 5)
            {
                failures.Add("Fewer than five NPC roles are declared.");
            }
            if (!Enum.IsDefined(typeof(NPCRole), NPCRole.Child))
            {
                failures.Add("Child companion role is missing.");
            }
            if (Enum.GetValues(typeof(NPCState)).Length < 9)
            {
                failures.Add("The required nine NPC states are not declared.");
            }
        }

        private static void CheckSceneBoot(List<string> failures)
        {
            const string scenePath = "Assets/WinterMarket/Scenes/WinterMarket.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                failures.Add("WinterMarket scene asset is missing.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SceneController controller = UnityEngine.Object.FindFirstObjectByType<SceneController>();
            if (controller == null)
            {
                failures.Add("SceneController is missing from the startup scene.");
                return;
            }

            controller.Build();
            if (controller.DioramaCamera == null ||
                controller.TimeOfDay == null ||
                controller.Lighting == null ||
                controller.Paths == null ||
                controller.Ambient == null ||
                UnityEngine.Object.FindFirstObjectByType<NPCDirector>() == null)
            {
                failures.Add("One or more runtime modules did not initialize.");
            }
            if (controller.DioramaCamera != null && !controller.DioramaCamera.orthographic)
            {
                failures.Add("Diorama camera is no longer orthographic.");
            }
            if (controller.GetComponent<DioramaUIController>() != null)
            {
                failures.Add("Diorama mode unexpectedly created a visible settings UI.");
            }
            if (controller.Paths == null ||
                controller.Paths.GetRouteWaypointCount("WalkThrough") < 6 ||
                controller.Paths.GetRouteWaypointCount("MarketStalls") < 5 ||
                controller.Paths.GetRouteWaypointCount("Delivery") < 3 ||
                controller.Paths.GetRouteWaypointCount("Cleaning") < 4)
            {
                failures.Add("V0.2 pedestrian routes were not configured with spatial variation.");
            }
            else
            {
                Vector3 boothCenter = new Vector3(-4.35f, 0.55f, 0.95f);
                Vector3 constrained = controller.Paths.ConstrainToWalkableSpace(
                    boothCenter + Vector3.left,
                    boothCenter,
                    0.24f);
                Vector3 horizontalOffset = constrained - boothCenter;
                horizontalOffset.y = 0f;
                if (horizontalOffset.sqrMagnitude < 0.01f)
                {
                    failures.Add("NPC navigation did not reject a position inside the bakery footprint.");
                }
                if (!controller.Paths.TryGetObstacleDetour(
                        new Vector3(-7.2f, 0.55f, 0.95f),
                        new Vector3(-1.2f, 0.55f, 0.95f),
                        0.24f,
                        out Vector3 detour) ||
                    Mathf.Abs(detour.z - 0.95f) < 0.5f)
                {
                    failures.Add("NPC navigation did not create a usable detour around the bakery footprint.");
                }
            }
            if (UnityEngine.Object.FindObjectsByType<VoxelAssetGenerator>(FindObjectsSortMode.None).Length < 12)
            {
                failures.Add("Diorama scene contains too few generated voxel assets.");
            }
            NPCDirector director = UnityEngine.Object.FindFirstObjectByType<NPCDirector>();
            if (director == null || director.PoolSize != 6)
            {
                failures.Add("NPC object pool was not built with six reusable figures.");
            }
            if (UnityEngine.Object.FindFirstObjectByType<BuildModeController>() == null)
            {
                failures.Add("V0.3 BuildModeController did not initialize.");
            }
            CheckMobileEnvironmentChunking(failures);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void CheckMobileEnvironmentChunking(List<string> failures)
        {
            GameObject forestRoot = GameObject.Find("Dynamischer Wald Chunks");
            if (forestRoot == null)
            {
                failures.Add("Mobile forest chunks were not initialized.");
                return;
            }

            int forestRenderers = forestRoot.GetComponentsInChildren<MeshRenderer>(true).Length;
            if (forestRenderers == 0 || forestRenderers > 108)
            {
                failures.Add(
                    $"Forest chunk renderer budget is invalid (renderers={forestRenderers}, expected 1..108).");
            }

            if (forestRoot.GetComponentsInChildren<MarketBuildObject>(true).Length != 0)
            {
                failures.Add("Forest still creates MarketBuildObject instances per tree.");
            }
        }

        private static void CheckBuildFoundation(List<string> failures)
        {
            int decorationCount = 0;
            bool hasCocoa = false;
            bool hasPath = false;
            for (int i = 0; i < BuildObjectLibrary.All.Count; i++)
            {
                BuildObjectDefinition definition = BuildObjectLibrary.All[i];
                if (definition.Category == BuildCategory.Decoration) decorationCount++;
                hasCocoa |= definition.Type == BuildObjectType.CocoaStall;
                hasPath |= definition.Type == BuildObjectType.PathTile;
            }
            if (decorationCount < 9 || !hasCocoa || !hasPath)
            {
                failures.Add("V0.3 library is missing required decoration, cocoa stall, or path entries.");
            }

            BuildLayoutData defaults = BuildLayoutStore.CreateDefault();
            var ids = new HashSet<string>();
            for (int i = 0; i < defaults.objects.Count; i++)
            {
                PlacedBuildObjectData entry = defaults.objects[i];
                if (entry == null || string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id))
                {
                    failures.Add("Default V0.3 layout contains missing or duplicate object ids.");
                    break;
                }
                float grid = BuildModeController.GridSize;
                if (Mathf.Abs(entry.x / grid - Mathf.Round(entry.x / grid)) > 0.001f ||
                    Mathf.Abs(entry.z / grid - Mathf.Round(entry.z / grid)) > 0.001f)
                {
                    failures.Add("Default V0.3 layout contains a prop that is not aligned to the construction grid.");
                    break;
                }
            }
            if (defaults.formatVersion != BuildLayoutData.CurrentVersion || defaults.objects.Count < 8)
            {
                failures.Add("Default V0.3 layout is not versioned or does not restore editable base props.");
            }

            var navigationRoot = new GameObject("Dynamic Navigation Check");
            try
            {
                PathSystem navigation = navigationRoot.AddComponent<PathSystem>();
                navigation.Configure(
                    new[] { new PathSystem.NamedWaypoint("west-entry", Vector3.zero) },
                    Array.Empty<PathSystem.NamedRoute>(),
                    Array.Empty<PathSystem.NavigationObstacle>());
                navigation.SetDynamicLayout(
                    new[] { new PathSystem.NamedWaypoint("build-test", new Vector3(1.5f, 0.55f, 0f)) },
                    new[] { new PathSystem.NamedRoute("visit-test", new[] { "west-entry", "build-test" }) },
                    new[] { new PathSystem.NavigationObstacle(new Vector3(0.75f, 0f, 0.8f), Vector2.one * 0.3f) });
                if (navigation.GetRouteWaypointCount("visit-test") != 2 ||
                    !navigation.TryGetRouteWaypoint("visit-test", 1, out Vector3 target) ||
                    Vector3.Distance(target, new Vector3(1.5f, 0.55f, 0f)) > 0.01f ||
                    navigation.NavigationRevision < 2)
                {
                    failures.Add("Dynamic build navigation did not register a route and target.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(navigationRoot);
            }
        }

        private static void CheckOfflineConfiguration(List<string> failures)
        {
            if (PlayerSettings.Android.forceInternetPermission)
            {
                failures.Add("Android forceInternetPermission is enabled.");
            }
        }
    }
}
#endif
