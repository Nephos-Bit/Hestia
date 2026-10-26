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
            CheckOfflineConfiguration(failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "WINTERMARKT_CHECKS_FAIL\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("WINTERMARKT_CHECKS_PASS checks=5 voxelMesh=timePhases=roles=sceneBoot=offline");
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
            EditorSceneManager.MarkSceneDirty(scene);
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
