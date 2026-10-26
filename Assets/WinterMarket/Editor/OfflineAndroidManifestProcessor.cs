#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEngine;

namespace WinterMarket.Editor
{
    public sealed class OfflineAndroidManifestProcessor : IPostGenerateGradleAndroidProject
    {
        private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
        private static readonly XNamespace Tools = "http://schemas.android.com/tools";

        public int callbackOrder => 9999;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string gradleRoot = Directory.GetParent(unityLibraryPath)?.FullName ?? unityLibraryPath;
            string[] manifests = Directory.GetFiles(
                gradleRoot,
                "AndroidManifest.xml",
                SearchOption.AllDirectories);

            int removed = 0;
            for (int i = 0; i < manifests.Length; i++)
            {
                XDocument document = XDocument.Load(manifests[i], LoadOptions.PreserveWhitespace);
                if (document.Root == null)
                {
                    continue;
                }

                XElement[] internetPermissions = document.Root
                    .Elements()
                    .Where(element =>
                        (element.Name.LocalName == "uses-permission" ||
                         element.Name.LocalName == "uses-permission-sdk-23") &&
                        string.Equals(
                            (string)element.Attribute(Android + "name"),
                            "android.permission.INTERNET",
                            StringComparison.Ordinal))
                    .ToArray();
                for (int permissionIndex = 0; permissionIndex < internetPermissions.Length; permissionIndex++)
                {
                    internetPermissions[permissionIndex].Remove();
                    removed++;
                }
                document.Save(manifests[i], SaveOptions.DisableFormatting);
            }

            string launcherManifest = Path.Combine(
                gradleRoot,
                "launcher",
                "src",
                "main",
                "AndroidManifest.xml");
            if (File.Exists(launcherManifest))
            {
                XDocument launcher = XDocument.Load(launcherManifest, LoadOptions.PreserveWhitespace);
                XElement root = launcher.Root;
                if (root != null)
                {
                    root.SetAttributeValue(XNamespace.Xmlns + "tools", Tools.NamespaceName);
                    root.AddFirst(
                        new XElement(
                            "uses-permission",
                            new XAttribute(Android + "name", "android.permission.INTERNET"),
                            new XAttribute(Tools + "node", "remove")));
                    launcher.Save(launcherManifest, SaveOptions.DisableFormatting);
                }
            }

            Debug.Log($"WINTERMARKT_OFFLINE_MANIFEST removedInternetDeclarations={removed}");
        }
    }
}
#endif
