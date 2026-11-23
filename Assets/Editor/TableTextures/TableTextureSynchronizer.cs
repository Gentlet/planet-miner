using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TableTextureSynchronizer
{
    private const string SubScenePath = "Assets/Scenes/V2 Test Scene/sub.unity";
    private const string ConfigRoot = "Assets/Resources/Config/";
    private const string MaterialsRoot = "Assets/Resources/Materials/";
    private const string BaseMap = "_BaseMap";
    private const string LegacyMainTexture = "_MainTex";

    /// <summary>생성 JSON과 저장된 프리팹 등록을 대조해 프로젝트 머티리얼의 텍스처 참조를 저장한다.</summary>
    [MenuItem("Tools/Planet Miner/Apply Table Textures")]
    public static void Apply()
    {
        int updated = ApplyGeneratedTextures();
        Debug.Log($"Table textures applied. Updated materials: {updated}.");
    }

    /// <summary>Unity에 생성 JSON과 이미지 임포트를 완료한 뒤 저장이 확인된 머티리얼 수를 반환한다.</summary>
    public static int ApplyGeneratedTextures()
    {
        RequireEditMode();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return ApplyFromJson(
            ReadImportedConfig("BuildingDefinitions.json"),
            ReadImportedConfig("ItemConfig.json"));
    }

    internal static int ApplyFromJson(string buildingJson, string itemJson)
    {
        RequireEditMode();
        return SaveChanges(ReadAssignments(buildingJson, itemJson));
    }

    /// <summary>빌드가 사용할 생성 JSON과 저장된 머티리얼 참조를 확인한다.</summary>
    internal static void ValidateGeneratedTextures()
    {
        var assignments = ReadAssignments(
            ReadImportedConfig("BuildingDefinitions.json"),
            ReadImportedConfig("ItemConfig.json"));
        foreach (var assignment in assignments)
        {
            Material material = assignment.Key;
            var (owner, texture) = assignment.Value;
            string materialPath = AssetDatabase.GetAssetPath(material);
            if (EditorUtility.IsDirty(material))
            {
                throw new InvalidDataException($"{owner}: material '{materialPath}' has unsaved changes. Save or revert the edit before building.");
            }

            if (HasTexture(material, texture))
            {
                continue;
            }

            string slot = material.GetTexture(BaseMap) != texture ? BaseMap : LegacyMainTexture;
            Texture actual = material.GetTexture(slot);
            string expectedPath = AssetDatabase.GetAssetPath(texture);
            string actualPath = actual == null ? "(none)" : AssetDatabase.GetAssetPath(actual);
            throw new InvalidDataException(
                $"{owner}: material '{materialPath}' {slot} expects '{expectedPath}' but references '{actualPath}'. " +
                "Run Tools > Planet Miner > Apply Table Textures before building.");
        }
    }

    private static Dictionary<Material, (string Owner, Texture2D Texture)> ReadAssignments(string buildingJson, string itemJson)
    {
        var assignments = new Dictionary<Material, (string Owner, Texture2D Texture)>();
        Scene scene = EditorSceneManager.OpenPreviewScene(SubScenePath);
        try
        {
            var buildings = FindAuthoring<BuildingPrefabDatabaseAuthoring>(scene).Prefabs
                .ToDictionary(entry => entry.BuildingId);
            var items = FindAuthoring<ItemPrefabDatabaseAuthoring>(scene).GetRegisteredPrefabs()
                .ToDictionary(entry => entry.Type, entry => entry.Prefab);
            var protectedMaterials = FindOtherWorldMaterialPaths(scene);

            foreach (JToken row in ReadRows(buildingJson, "buildingDefinitions"))
            {
                int id = row.Value<int>("buildingId");
                string owner = $"BuildingDefinitions {id}";
                if (!buildings.Remove(id, out var entry))
                {
                    throw new InvalidDataException($"{owner}: missing prefab or duplicate BuildingId.");
                }

                AddAssignment(assignments, protectedMaterials, entry.Prefab, "Building", owner, row.Value<string>("textureResourcePath"));
            }

            foreach (JToken row in ReadRows(itemJson, "Items"))
            {
                int id = row.Value<int>("ItemId");
                string owner = $"ItemConfig {id}";
                string name = row.Value<string>("ItemType");
                if (!Enum.TryParse(name, out ItemTypeEnum type) || type == ItemTypeEnum.None ||
                    type.ToString() != name || !items.Remove(type, out GameObject prefab))
                {
                    throw new InvalidDataException($"{owner}: missing prefab or duplicate ItemType '{name}'.");
                }

                AddAssignment(assignments, protectedMaterials, prefab, "Item", owner, row.Value<string>("TextureResourcePath"));
            }

            if (buildings.Count != 0 || items.Count != 0)
            {
                throw new InvalidDataException(
                    $"Missing texture rows for registered buildings [{string.Join(", ", buildings.Keys)}] " +
                    $"or items [{string.Join(", ", items.Keys)}].");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }

        return assignments;
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Apply table textures while the Unity Editor is in Edit Mode.");
        }

        if (EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
        {
            throw new InvalidOperationException("Apply table textures after Unity finishes compiling or building.");
        }
    }

    private static string ReadImportedConfig(string fileName)
    {
        string path = ConfigRoot + fileName;
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (asset == null)
        {
            throw new InvalidDataException($"Missing generated table at '{path}'.");
        }

        string imported = asset.text;
        string onDisk = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        if (!string.Equals(imported, onDisk, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Generated table '{path}' has changes Unity has not imported.");
        }

        return imported;
    }

    private static JArray ReadRows(string json, string key)
    {
        if (JObject.Parse(json)[key] is not JArray rows || rows.Count == 0)
        {
            throw new InvalidDataException($"Missing generated table rows for '{key}'.");
        }

        return rows;
    }

    private static T FindAuthoring<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
    }

    private static HashSet<string> FindOtherWorldMaterialPaths(Scene scene)
    {
        var drone = FindAuthoring<DronePrefabDatabaseAuthoring>(scene);
        var resources = FindAuthoring<ResourcePrefabDatabaseAuthoring>(scene);
        var prefabPaths = resources.LoadMode == ResourcePrefabLoadMode.ResourcesAutoLoad
            ? AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Prefabs/Resource" })
                .Select(AssetDatabase.GUIDToAssetPath)
            : resources.Prefabs.Select(entry => AssetDatabase.GetAssetPath(entry.Prefab));
        return AssetDatabase.GetDependencies(prefabPaths.Append(AssetDatabase.GetAssetPath(drone.Prefab))
            .Where(path => !string.IsNullOrEmpty(path)).Distinct().ToArray()).ToHashSet();
    }

    private static void AddAssignment(
        Dictionary<Material, (string Owner, Texture2D Texture)> assignments,
        HashSet<string> protectedMaterials,
        GameObject prefab,
        string domain,
        string owner,
        string imagePath)
    {
        if (prefab == null)
        {
            throw new InvalidDataException($"{owner}: no prefab is registered for '{imagePath}'.");
        }

        if (string.IsNullOrEmpty(imagePath) || !imagePath.StartsWith("Sprites/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{owner}: invalid texture resource path '{imagePath}'.");
        }

        var texture = Resources.Load<Texture2D>(imagePath);
        if (texture == null)
        {
            throw new InvalidDataException($"{owner}: texture '{imagePath}' was not found.");
        }

        if (Path.ChangeExtension(AssetDatabase.GetAssetPath(texture), null) != "Assets/Resources/" + imagePath)
        {
            throw new InvalidDataException($"{owner}: texture path '{imagePath}' must match its asset path under Assets/Resources.");
        }

        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 1)
        {
            throw new InvalidDataException($"{owner}: prefab '{prefab.name}' must have one MeshRenderer.");
        }

        var materials = renderers[0].sharedMaterials;
        if (materials.Length != 1)
        {
            throw new InvalidDataException($"{owner}: prefab '{prefab.name}' must have one material slot.");
        }

        Material material = materials[0];
        if (material == null)
        {
            throw new InvalidDataException($"{owner}: prefab '{prefab.name}' has no material assigned.");
        }

        string materialPath = AssetDatabase.GetAssetPath(material);
        if (!materialPath.StartsWith(MaterialsRoot + domain + "/", StringComparison.Ordinal) ||
            protectedMaterials.Contains(materialPath))
        {
            throw new InvalidDataException($"{owner}: material '{materialPath}' is outside its texture ownership.");
        }

        if (!material.HasProperty(BaseMap))
        {
            throw new InvalidDataException($"{owner}: material '{materialPath}' does not have a {BaseMap} texture slot.");
        }

        if (assignments.TryGetValue(material, out var existing) && existing.Texture != texture)
        {
            throw new InvalidDataException(
                $"{owner} and {existing.Owner} request different textures for material '{materialPath}'.");
        }

        assignments[material] = (owner, texture);
    }

    private static int SaveChanges(Dictionary<Material, (string Owner, Texture2D Texture)> changes)
    {
        var updates = new List<(Material Material, string Owner, Texture2D Texture)>();
        foreach (var change in changes)
        {
            Material material = change.Key;
            var (owner, texture) = change.Value;
            // Inspector에서 미저장 텍스처가 Table과 이미 일치할 수 있으므로 변경 대상을 거르기 전에 확인한다.
            if (EditorUtility.IsDirty(material))
            {
                throw new InvalidOperationException(
                    $"{owner}: material '{AssetDatabase.GetAssetPath(material)}' has unsaved changes.");
            }

            if (HasTexture(material, texture))
            {
                continue;
            }

            if (!AssetDatabase.IsOpenForEdit(material))
            {
                throw new InvalidOperationException(
                    $"{owner}: material '{AssetDatabase.GetAssetPath(material)}' cannot be edited.");
            }

            CheckMaterialWriteAccess(AssetDatabase.GetAssetPath(material), owner);
            updates.Add((material, owner, texture));
        }

        int savedCount = 0;
        foreach (var (material, owner, texture) in updates)
        {
            string materialPath = AssetDatabase.GetAssetPath(material);
            Undo.RecordObject(material, "Apply Table Texture");
            material.SetTexture(BaseMap, texture);
            if (material.HasProperty(LegacyMainTexture))
            {
                material.SetTexture(LegacyMainTexture, texture);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            // 파일 교체에 실패해도 SaveAssetIfDirty가 반환할 수 있으므로 저장된 참조를 다시 확인한다.
            AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var saved = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (saved == null)
            {
                throw new IOException(
                    $"{owner}: could not reload material '{materialPath}' after saving. Verified before failure: {savedCount}.");
            }

            if (!HasTexture(saved, texture))
            {
                throw new IOException(
                    $"{owner}: texture was not saved to '{materialPath}'. Verified before failure: {savedCount}.");
            }

            savedCount++;
        }

        return savedCount;
    }

    private static void CheckMaterialWriteAccess(string materialPath, string owner)
    {
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), materialPath);
        try
        {
            // 이미 쓰기가 차단된 파일은 Unity의 네이티브 저장 대화상자를 열기 전에 알린다.
            using var handle = File.Open(fullPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{owner}: material '{materialPath}' cannot be opened for writing. No textures were saved.", error);
        }
    }

    private static bool HasTexture(Material material, Texture2D texture)
    {
        return material.GetTexture(BaseMap) == texture &&
            (!material.HasProperty(LegacyMainTexture) || material.GetTexture(LegacyMainTexture) == texture);
    }
}
