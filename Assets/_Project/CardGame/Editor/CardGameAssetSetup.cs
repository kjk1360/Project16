using System;
using System.IO;
using Project16.CardGame.Composition;
using Project16.Foundation.Composition;
using UnityEditor;
using UnityEngine;

namespace Project16.CardGame.Editor
{
    public static class CardGameAssetSetup
    {
        public const string ModulePath = "Assets/_Project/Settings/CardGameModule.asset";
        public const string SettingsPath = "Assets/_Project/Resources/Project16/FoundationSettings.asset";

        [MenuItem("Tools/Project16/Create Card Game Module")]
        public static void CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires Edit Mode.");
            var settings = AssetDatabase.LoadAssetAtPath<FoundationSettings>(SettingsPath);
            if (settings == null) throw new InvalidOperationException("Create the Foundation assets first.");
            if (!File.Exists(GameSpecImport.OutputPath)) GameSpecImport.Import();
            var database = AssetDatabase.LoadAssetAtPath<TextAsset>(GameSpecImport.OutputPath);
            GameSpecLoader.Load(database.bytes);
            var module = AssetDatabase.LoadAssetAtPath<CardGameModule>(ModulePath);
            if (module == null)
            {
                if (File.Exists(ModulePath)) throw new InvalidOperationException("Module path contains an unrelated asset.");
                module = ScriptableObject.CreateInstance<CardGameModule>();
                var serialized = new SerializedObject(module);
                serialized.FindProperty("_specDatabase").objectReferenceValue = database;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(module, ModulePath);
                AssetDatabase.SaveAssetIfDirty(module);
            }
            var settingsObject = new SerializedObject(settings);
            var modules = settingsObject.FindProperty("_modules");
            for (var i = 0; i < modules.arraySize; i++)
                if (modules.GetArrayElementAtIndex(i).objectReferenceValue == module) return;
            modules.InsertArrayElementAtIndex(modules.arraySize);
            modules.GetArrayElementAtIndex(modules.arraySize - 1).objectReferenceValue = module;
            settingsObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("Card game module added to Foundation settings. Existing modules were retained.");
        }
    }
}
