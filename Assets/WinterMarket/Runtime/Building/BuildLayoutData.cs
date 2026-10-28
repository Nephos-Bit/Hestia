using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WinterMarket
{
    public enum BuildCategory
    {
        Stalls,
        Decoration,
        Paths
    }

    public enum BuildObjectType
    {
        BakeryStall,
        DrinksStall,
        CocoaStall,
        SmallFir,
        Snowman,
        Bench,
        Lantern,
        FireBowl,
        Crates,
        Fence,
        StringLights,
        ChristmasTree,
        Handcart,
        PathTile
    }

    public enum BuildInterest
    {
        None,
        Stall,
        Rest,
        Warmth
    }

    /// <summary>Immutable catalogue entry. Footprints use the 0.60 m construction grid.</summary>
    public sealed class BuildObjectDefinition
    {
        public BuildObjectType Type { get; }
        public string DisplayName { get; }
        public BuildCategory Category { get; }
        public Vector2Int Footprint { get; }
        public bool BlocksNavigation { get; }
        public bool MayStandOnPath { get; }
        public BuildInterest Interest { get; }
        public bool HasWarmLight { get; }

        public BuildObjectDefinition(
            BuildObjectType type,
            string displayName,
            BuildCategory category,
            Vector2Int footprint,
            bool blocksNavigation = true,
            bool mayStandOnPath = false,
            BuildInterest interest = BuildInterest.None,
            bool hasWarmLight = false)
        {
            Type = type;
            DisplayName = displayName;
            Category = category;
            Footprint = new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
            BlocksNavigation = blocksNavigation;
            MayStandOnPath = mayStandOnPath;
            Interest = interest;
            HasWarmLight = hasWarmLight;
        }

        public Vector2Int RotatedFootprint(int quarterTurns)
        {
            return Mathf.Abs(quarterTurns % 2) == 1
                ? new Vector2Int(Footprint.y, Footprint.x)
                : Footprint;
        }
    }

    public static class BuildObjectLibrary
    {
        private static readonly BuildObjectDefinition[] Entries =
        {
            new BuildObjectDefinition(BuildObjectType.BakeryStall, "Bäckerei", BuildCategory.Stalls, new Vector2Int(8, 5), true, false, BuildInterest.Stall, true),
            new BuildObjectDefinition(BuildObjectType.DrinksStall, "Heisse Getränke", BuildCategory.Stalls, new Vector2Int(8, 5), true, false, BuildInterest.Stall, true),
            new BuildObjectDefinition(BuildObjectType.CocoaStall, "Kakaostand", BuildCategory.Stalls, new Vector2Int(3, 2), true, false, BuildInterest.Stall, true),
            new BuildObjectDefinition(BuildObjectType.SmallFir, "Kleine Tanne", BuildCategory.Decoration, new Vector2Int(5, 5)),
            new BuildObjectDefinition(BuildObjectType.Snowman, "Schneemann", BuildCategory.Decoration, new Vector2Int(2, 2), true, false),
            new BuildObjectDefinition(BuildObjectType.Bench, "Holzbank", BuildCategory.Decoration, new Vector2Int(6, 2), true, false, BuildInterest.Rest),
            new BuildObjectDefinition(BuildObjectType.Lantern, "Laterne", BuildCategory.Decoration, new Vector2Int(2, 2), false, true, BuildInterest.None, true),
            new BuildObjectDefinition(BuildObjectType.FireBowl, "Feuerkorb", BuildCategory.Decoration, new Vector2Int(2, 2), true, false, BuildInterest.Warmth, true),
            new BuildObjectDefinition(BuildObjectType.Crates, "Holzkisten", BuildCategory.Decoration, new Vector2Int(5, 3)),
            new BuildObjectDefinition(BuildObjectType.Fence, "Zaunelement", BuildCategory.Decoration, new Vector2Int(3, 1)),
            new BuildObjectDefinition(BuildObjectType.StringLights, "Lichterdekoration", BuildCategory.Decoration, new Vector2Int(3, 1), false, true, BuildInterest.None, false),
            new BuildObjectDefinition(BuildObjectType.ChristmasTree, "Kleiner Weihnachtsbaum", BuildCategory.Decoration, new Vector2Int(5, 5), true, false, BuildInterest.None, true),
            new BuildObjectDefinition(BuildObjectType.Handcart, "Handkarren", BuildCategory.Decoration, new Vector2Int(6, 3)),
            new BuildObjectDefinition(BuildObjectType.PathTile, "Straße", BuildCategory.Paths, Vector2Int.one, false, true)
        };

        public static IReadOnlyList<BuildObjectDefinition> All => Entries;

        public static BuildObjectDefinition Get(BuildObjectType type)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Type == type) return Entries[i];
            }
            return Entries[0];
        }

        public static IEnumerable<BuildObjectDefinition> GetCategory(BuildCategory category)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Category == category) yield return Entries[i];
            }
        }
    }

    [Serializable]
    public sealed class PlacedBuildObjectData
    {
        public string id;
        public BuildObjectType type;
        public float x;
        public float z;
        public int rotation;
        public string variant;

        public Vector3 Position => new Vector3(x, 0f, z);
        public Quaternion Rotation => Quaternion.Euler(0f, Mathf.Repeat(rotation, 4) * 90f, 0f);

        public PlacedBuildObjectData Clone()
        {
            return new PlacedBuildObjectData
            {
                id = id,
                type = type,
                x = x,
                z = z,
                rotation = rotation,
                variant = variant
            };
        }
    }

    [Serializable]
    public sealed class BuildUserPreferences
    {
        public string lastCategory = BuildCategory.Decoration.ToString();
        public bool showGrid = true;
    }

    [Serializable]
    public sealed class BuildLayoutData
    {
        public const int CurrentVersion = 4;
        public int formatVersion = CurrentVersion;
        public List<PlacedBuildObjectData> objects = new List<PlacedBuildObjectData>();
        public BuildUserPreferences preferences = new BuildUserPreferences();

        public BuildLayoutData Clone()
        {
            var copy = new BuildLayoutData
            {
                formatVersion = formatVersion,
                preferences = new BuildUserPreferences
                {
                    lastCategory = preferences != null ? preferences.lastCategory : BuildCategory.Decoration.ToString(),
                    showGrid = preferences == null || preferences.showGrid
                }
            };
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null) copy.objects.Add(objects[i].Clone());
            }
            return copy;
        }
    }

    /// <summary>Versioned, scene-independent local layout persistence with a recoverable backup.</summary>
    public static class BuildLayoutStore
    {
        private const string FileName = "wintermarkt-layout.json";
        private const string BackupName = "wintermarkt-layout.backup.json";
        private const string LegacyFileName = "wintermarkt-layout-v1.json";
        private const string LegacyBackupName = "wintermarkt-layout-v1.backup.json";

        public static string LayoutPath => Path.Combine(Application.persistentDataPath, FileName);

        public static BuildLayoutData LoadOrDefault()
        {
            if (TryRead(LayoutPath, out BuildLayoutData layout) ||
                TryRead(Path.Combine(Application.persistentDataPath, BackupName), out layout) ||
                TryRead(Path.Combine(Application.persistentDataPath, LegacyFileName), out layout) ||
                TryRead(Path.Combine(Application.persistentDataPath, LegacyBackupName), out layout))
            {
                Migrate(layout);
                return layout;
            }

            return CreateDefault();
        }

        public static void Save(BuildLayoutData layout)
        {
            if (layout == null) return;
            layout.formatVersion = BuildLayoutData.CurrentVersion;
            if (layout.preferences == null) layout.preferences = new BuildUserPreferences();
            string directory = Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            string target = LayoutPath;
            string temporary = target + ".tmp";
            string backup = Path.Combine(directory, BackupName);
            string json = JsonUtility.ToJson(layout, true);

            try
            {
                File.WriteAllText(temporary, json);
                if (File.Exists(target))
                {
                    try
                    {
                        File.Replace(temporary, target, backup);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(target, backup, true);
                        File.Delete(target);
                        File.Move(temporary, target);
                    }
                    catch (IOException)
                    {
                        File.Copy(target, backup, true);
                        File.Delete(target);
                        File.Move(temporary, target);
                    }
                }
                else
                {
                    File.Move(temporary, target);
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                Debug.LogWarning("Wintermarkt: Layoutdatei konnte nicht gespeichert werden; Änderungen bleiben nur für diese Sitzung erhalten.");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static BuildLayoutData CreateDefault()
        {
            var layout = new BuildLayoutData();
            // All placed object origins use the same 0.60 m construction grid, including
            // transferred V0.2 props. Their voxel models remain unmodified.
            AddDefault(layout, "builtin-bakery", BuildObjectType.BakeryStall, -4.2f, 0f, 0);
            AddDefault(layout, "builtin-drinks", BuildObjectType.DrinksStall, 4.2f, 0f, 0);
            AddDefault(layout, "builtin-tree", BuildObjectType.ChristmasTree, 0f, 1.2f, 0);
            AddDefault(layout, "builtin-lamp-west", BuildObjectType.Lantern, -6f, -2.4f, 0);
            AddDefault(layout, "builtin-lamp-east", BuildObjectType.Lantern, 6f, -2.4f, 0);
            AddDefault(layout, "builtin-bench", BuildObjectType.Bench, -8.4f, -0.6f, 0);
            AddDefault(layout, "builtin-crates", BuildObjectType.Crates, 8.4f, 3.6f, 0);
            AddDefault(layout, "builtin-cart", BuildObjectType.Handcart, 4.8f, 3.6f, 0);
            return layout;
        }

        private static void AddDefault(
            BuildLayoutData layout, string id, BuildObjectType type, float x, float z, int rotation)
        {
            layout.objects.Add(new PlacedBuildObjectData { id = id, type = type, x = x, z = z, rotation = rotation });
        }

        private static bool TryRead(string path, out BuildLayoutData layout)
        {
            layout = null;
            try
            {
                if (!File.Exists(path)) return false;
                layout = JsonUtility.FromJson<BuildLayoutData>(File.ReadAllText(path));
                return layout != null && layout.objects != null && layout.formatVersion > 0 && layout.formatVersion <= BuildLayoutData.CurrentVersion;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning("Wintermarkt: Layoutdatei konnte nicht gelesen werden; ein gesicherter Stand wird verwendet.");
                return false;
            }
        }

        private static void Migrate(BuildLayoutData layout)
        {
            if (layout.preferences == null) layout.preferences = new BuildUserPreferences();
            if (layout.objects == null) layout.objects = new List<PlacedBuildObjectData>();
            if (layout.formatVersion < 2)
            {
                // V0.3 initially snapped the transferred props to the old construction
                // grid. Restore their authored V0.2 coordinates before switching to the
                // coarser grid used for all new edits.
                for (int i = 0; i < layout.objects.Count; i++)
                {
                    PlacedBuildObjectData item = layout.objects[i];
                    if (item == null) continue;
                    switch (item.id)
                    {
                        case "builtin-bakery": item.x = -4.35f; item.z = 0.95f; break;
                        case "builtin-drinks": item.x = 4.35f; item.z = 0.55f; break;
                        case "builtin-tree": item.x = 0f; item.z = 1.45f; break;
                        case "builtin-lamp-west": item.x = -6.1f; item.z = -2.25f; break;
                        case "builtin-lamp-east": item.x = 6.1f; item.z = -2.25f; break;
                        case "builtin-bench": item.x = -8.3f; item.z = -0.7f; break;
                        case "builtin-crates": item.x = 8.2f; item.z = 3.3f; break;
                        case "builtin-cart": item.x = 4.8f; item.z = 3.5f; break;
                    }
                }
            }
            if (layout.formatVersion < 4)
            {
                // The original two stalls predated path snapping. Keep their established X
                // positions but align their service fronts with the three-cell main road.
                for (int i = 0; i < layout.objects.Count; i++)
                {
                    PlacedBuildObjectData item = layout.objects[i];
                    if (item == null) continue;
                    if (item.id == "builtin-bakery" || item.id == "builtin-drinks") item.z = 0f;
                }
            }
            // V3 makes the placement invariant explicit. Applying this on every load also
            // repairs manually edited or incomplete files without touching model geometry.
            for (int i = 0; i < layout.objects.Count; i++)
            {
                PlacedBuildObjectData item = layout.objects[i];
                if (item == null) continue;
                item.x = SnapToBuildGrid(item.x);
                item.z = SnapToBuildGrid(item.z);
                item.rotation = ((item.rotation % 4) + 4) % 4;
            }
            layout.formatVersion = BuildLayoutData.CurrentVersion;
        }

        private static float SnapToBuildGrid(float value)
        {
            return Mathf.Round(value / BuildModeController.GridSize) * BuildModeController.GridSize;
        }
    }
}
