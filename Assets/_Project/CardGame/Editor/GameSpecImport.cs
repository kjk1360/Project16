using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BansheeGz.BGDatabase;
using Newtonsoft.Json.Linq;
using Project16.CardGame.Composition;
using UnityEditor;
using UnityEngine;

namespace Project16.CardGame.Editor
{
    /// <summary>Imports this project's authored tables without touching BGRepo.I.</summary>
    public static class GameSpecImport
    {
        public const string SourcePath = "Assets/_Project/SpecAuthoring/CardGame.tables.json";
        public const string OutputPath = "Assets/_Project/Resources/Project16/CardGameSpecs.bytes";

        [MenuItem("Tools/Project16/Import Card Game Specs (JSON to BG)")]
        public static void Import()
        {
            var bytes = Build(File.ReadAllText(SourcePath));
            // Validate every executable row and cross-table reference before replacing the asset.
            GameSpecLoader.Load(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllBytes(OutputPath, bytes);
            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"Validated card game BG tables: {OutputPath} ({bytes.Length} bytes)");
        }

        public static byte[] Build(string json)
        {
            var document = JObject.Parse(json);
            if ((int?)document["formatVersion"] != 1) throw new InvalidDataException("Unsupported table authoring format.");
            var definitions = document["tables"] as JArray ?? throw new InvalidDataException("tables is required.");
            var repo = new BGRepo();
            try
            {
                var tables = new Dictionary<string, BGMetaRow>(StringComparer.Ordinal);
                foreach (var definition in definitions)
                {
                    var name = RequiredString(definition, "name");
                    if (tables.ContainsKey(name)) throw new InvalidDataException("Duplicate table: " + name);
                    tables.Add(name, new BGMetaRow(repo, name));
                }
                foreach (var definition in definitions)
                {
                    var table = tables[RequiredString(definition, "name")];
                    foreach (var field in Fields(definition))
                    {
                        var name = RequiredString(field, "name");
                        var type = RequiredString(field, "type");
                        switch (type)
                        {
                            case "string": new BGFieldString(table, name); break;
                            case "int": new BGFieldInt(table, name); break;
                            case "bool": new BGFieldBool(table, name); break;
                            case "strings": new BGFieldListString(table, name); break;
                            case "relation": new BGFieldRelationSingle(table, name, tables[RequiredString(field, "table")]); break;
                            // Repeated actor/monster definitions express multiple instances.
                            // BG defaults to deduplication, which would change the authored game.
                            case "relations": new BGFieldRelationMultiple(table, name, tables[RequiredString(field, "table")]) { AllowDuplicates = true }; break;
                            default: throw new InvalidDataException("Unsupported field type: " + type);
                        }
                    }
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in Rows(definition))
                    {
                        var key = RequiredString(row, "key");
                        if (!seen.Add(key)) throw new InvalidDataException($"Duplicate {table.Name} row: {key}");
                        table.NewEntity().Name = key;
                    }
                }
                foreach (var definition in definitions)
                {
                    var table = tables[RequiredString(definition, "name")];
                    foreach (var row in Rows(definition))
                    {
                        var entity = table.GetEntity(RequiredString(row, "key"));
                        var known = new HashSet<string>(Fields(definition).Select(field => RequiredString(field, "name")), StringComparer.Ordinal) { "key" };
                        foreach (var property in ((JObject)row).Properties())
                            if (!known.Contains(property.Name)) throw new InvalidDataException($"Unknown column {table.Name}.{property.Name}");
                        foreach (var field in Fields(definition))
                        {
                            var name = RequiredString(field, "name");
                            var value = row[name];
                            if (value == null) throw new InvalidDataException($"Missing {table.Name}/{entity.Name}.{name}");
                            switch (RequiredString(field, "type"))
                            {
                                case "string": entity.Set<string>(name, NullableString(value)); break;
                                case "int":
                                    RequireType(value, JTokenType.Integer, name);
                                    entity.Set(name, value.Value<int>()); break;
                                case "bool":
                                    RequireType(value, JTokenType.Boolean, name);
                                    entity.Set(name, value.Value<bool>()); break;
                                case "strings": entity.Set(name, Strings(value, name)); break;
                                case "relation":
                                    entity.Set<BGEntity>(name, Lookup(tables, field, NullableString(value))); break;
                                case "relations":
                                    entity.Set(name, Strings(value, name).Select(key => Lookup(tables, field, key)).ToList()); break;
                            }
                        }
                    }
                }
                return repo.Save();
            }
            finally { repo.Clear(); }
        }

        private static BGEntity Lookup(Dictionary<string, BGMetaRow> tables, JToken field, string key)
        {
            if (key == null) return null;
            var table = tables[RequiredString(field, "table")];
            return table.GetEntity(key) ?? throw new InvalidDataException($"Missing relation: {table.Name}/{key}");
        }
        private static JArray Fields(JToken definition) => definition["fields"] as JArray ?? throw new InvalidDataException("fields is required.");
        private static JArray Rows(JToken definition) => definition["rows"] as JArray ?? throw new InvalidDataException("rows is required.");
        private static string RequiredString(JToken value, string key)
        {
            var result = NullableString(value[key]);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("Required string: " + key);
            return result;
        }
        private static string NullableString(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            RequireType(value, JTokenType.String, "string");
            return value.Value<string>();
        }
        private static List<string> Strings(JToken value, string name)
        {
            RequireType(value, JTokenType.Array, name);
            return value.Select(item => NullableString(item) ?? throw new InvalidDataException("Null list element: " + name)).ToList();
        }
        private static void RequireType(JToken value, JTokenType expected, string name)
        {
            if (value.Type != expected) throw new InvalidDataException($"Expected {expected} for {name}, found {value.Type}.");
        }
    }
}
