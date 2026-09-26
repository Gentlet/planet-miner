using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace PlanetMiner.Config
{
    [Serializable]
    public class BuildingRuntimeConfigJsonData
    {
        public List<BuildingRuntimeConfigEntry> buildings = new List<BuildingRuntimeConfigEntry>();
    }

    [Serializable]
    public class BuildingRuntimeConfigEntry
    {
        public string buildingType;
        public float speed;
        public int storageCapacity;
    }

    /// <summary>
    /// BuildingRuntimeConfig JSON을 로드, 파싱, 검증하고 ECS 월드에 게시하는 유틸리티.
    /// </summary>
    public static class BuildingRuntimeConfigLoader
    {
        public const string DefaultResourcePath = "Config/BuildingRuntimeConfig";

        /// <summary>
        /// Resources 경로에서 JSON을 로드하여 검증된 설정 엘리먼트 목록을 반환.
        /// </summary>
        public static bool TryLoadConfigFromResources(string resourcePath, out List<BuildingRuntimeConfigElement> configElements)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null || string.IsNullOrEmpty(textAsset.text))
            {
                Debug.LogError($"[BuildingRuntimeConfigLoader] Failed to load config TextAsset at Resources path: '{resourcePath}'.");
                configElements = null;
                return false;
            }

            return TryParseJson(textAsset.text, out configElements);
        }

        /// <summary>
        /// JSON 문자열을 파싱하고 유효성 전수 검사를 수행.
        /// 결함 발견 시 부분 게시를 방지하기 위해 즉시 false를 반환 (All-or-Nothing).
        /// </summary>
        public static bool TryParseJson(string json, out List<BuildingRuntimeConfigElement> configElements)
        {
            configElements = null;

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[BuildingRuntimeConfigLoader] JSON string is null or empty.");
                return false;
            }

            BuildingRuntimeConfigJsonData data;
            try
            {
                data = JsonUtility.FromJson<BuildingRuntimeConfigJsonData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildingRuntimeConfigLoader] JSON parsing exception: {ex.Message}");
                return false;
            }

            if (data == null || data.buildings == null || data.buildings.Count == 0)
            {
                Debug.LogError("[BuildingRuntimeConfigLoader] Parsed buildings list is null or empty.");
                return false;
            }

            var result = new List<BuildingRuntimeConfigElement>(data.buildings.Count);
            var seenTypes = new HashSet<BuildingTypeEnum>();

            for (int i = 0; i < data.buildings.Count; i++)
            {
                var entry = data.buildings[i];

                if (string.IsNullOrEmpty(entry.buildingType))
                {
                    Debug.LogError($"[BuildingRuntimeConfigLoader] Entry at index {i} has missing or empty buildingType.");
                    return false;
                }

                if (!Enum.TryParse<BuildingTypeEnum>(entry.buildingType, true, out var buildingType) ||
                    buildingType == BuildingTypeEnum.None ||
                    buildingType >= BuildingTypeEnum.Count)
                {
                    Debug.LogError($"[BuildingRuntimeConfigLoader] Invalid buildingType '{entry.buildingType}' at index {i}.");
                    return false;
                }

                if (!seenTypes.Add(buildingType))
                {
                    Debug.LogError($"[BuildingRuntimeConfigLoader] Duplicate buildingType '{buildingType}' detected at index {i}.");
                    return false;
                }

                // 건물 타입별 세부 스펙 검증
                if (buildingType == BuildingTypeEnum.Storage)
                {
                    if (entry.storageCapacity <= 0 || entry.storageCapacity > GameConstants.MaxStorageSlots)
                    {
                        Debug.LogError($"[BuildingRuntimeConfigLoader] Storage has invalid storageCapacity {entry.storageCapacity} (allowed: 1 ~ {GameConstants.MaxStorageSlots}).");
                        return false;
                    }
                }
                else if (buildingType == BuildingTypeEnum.Belt ||
                         buildingType == BuildingTypeEnum.Miner ||
                         buildingType == BuildingTypeEnum.Crafter ||
                         buildingType == BuildingTypeEnum.ResearchBuilding)
                {
                    if (entry.speed <= 0f)
                    {
                        Debug.LogError($"[BuildingRuntimeConfigLoader] {buildingType} has invalid speed {entry.speed} (must be > 0).");
                        return false;
                    }
                }

                result.Add(new BuildingRuntimeConfigElement(buildingType, entry.speed, entry.storageCapacity));
            }

            configElements = result;
            return true;
        }

        /// <summary>
        /// 검증된 설정 엘리먼트들을 ECS 월드의 BuildingRuntimeConfig 싱글톤 엔티티로 게시.
        /// </summary>
        public static Entity PublishConfig(EntityManager entityManager, List<BuildingRuntimeConfigElement> configElements)
        {
            if (configElements == null || configElements.Count == 0)
            {
                Debug.LogError("[BuildingRuntimeConfigLoader] Cannot publish null or empty config elements.");
                return Entity.Null;
            }

            var entity = entityManager.CreateEntity(typeof(BuildingRuntimeConfig));
            var buffer = entityManager.AddBuffer<BuildingRuntimeConfigElement>(entity);

            for (int i = 0; i < configElements.Count; i++)
            {
                buffer.Add(configElements[i]);
            }

            return entity;
        }
    }
}
