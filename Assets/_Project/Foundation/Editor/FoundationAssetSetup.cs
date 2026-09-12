using System;
using System.Collections.Generic;
using System.IO;
using Project16.Foundation.Composition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project16.Foundation.Editor
{
    public static class FoundationAssetSetup
    {
        public const string SettingsPath = "Assets/_Project/Resources/Project16/FoundationSettings.asset";
        public const string StarterPrefabPath = "Assets/_Project/Prefabs/Composition/SceneStarter.prefab";
        public const string BootScenePath = "Assets/_Project/Scenes/FoundationBoot.unity";

        [MenuItem("Tools/Project16/Create Foundation Assets")]
        public static void CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Foundation asset setup requires Edit Mode.");

            EnsureFolder(Path.GetDirectoryName(SettingsPath));
            EnsureFolder(Path.GetDirectoryName(StarterPrefabPath));
            EnsureFolder(Path.GetDirectoryName(BootScenePath));

            var settings = AssetDatabase.LoadAssetAtPath<FoundationSettings>(SettingsPath);
            if (settings == null)
            {
                if (File.Exists(SettingsPath)) throw new InvalidOperationException("Settings path contains an unrelated asset.");
                settings = ScriptableObject.CreateInstance<FoundationSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            if (!File.Exists(BootScenePath) || !File.Exists(StarterPrefabPath))
            {
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    var starter = new GameObject("Starter");
                    SceneManager.MoveGameObjectToScene(starter, scene);
                    var component = starter.AddComponent<SceneStarter>();
                    var serialized = new SerializedObject(component);
                    serialized.FindProperty("_settings").objectReferenceValue = settings;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    if (!File.Exists(StarterPrefabPath)) PrefabUtility.SaveAsPrefabAsset(starter, StarterPrefabPath);
                    if (!File.Exists(BootScenePath) && !EditorSceneManager.SaveScene(scene, BootScenePath))
                        throw new IOException("Could not save the new foundation boot scene.");
                }
                finally
                {
                    if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            var builds = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!builds.Exists(scene => scene.path == BootScenePath))
            {
                builds.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
                EditorBuildSettings.scenes = builds.ToArray();
            }
            Debug.Log("Foundation assets are ready. Existing scenes, assets and build entries were preserved.");
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
