using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;

/// <summary>형식 2 파일에 기록된 배정 전 자동 작업을 복원한다.</summary>
internal static class GamePassiveDroneTaskSave
{
    public static void Record(EntityManager manager, EntityCommandBuffer ecb, List<SavedPassiveDroneTask> saved,
        Dictionary<string, Entity> targets, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes, ulong nextSequence)
    {
        var sequences = new HashSet<ulong>();
        var keys = new HashSet<(DroneLogisticsTaskKindEnum, Entity, ItemTypeEnum)>();
        foreach (var entry in saved)
        {
            if (entry == null)
            {
                throw new InvalidDataException("자동 드론 작업 기록이 비어 있습니다.");
            }
            if (string.IsNullOrEmpty(entry.TargetId) || !targets.TryGetValue(entry.TargetId, out var target))
            {
                throw new InvalidDataException("자동 드론 작업의 저장 대상이 없습니다.");
            }
            ItemTypeEnum item = GameWorldRestore.ResolveItem(entry.ItemId, itemTypes);
            if (entry.CreationSequence == 0 || entry.CreationSequence >= nextSequence ||
                !sequences.Add(entry.CreationSequence) || !keys.Add((entry.Kind, target, item)))
            {
                throw new InvalidDataException("자동 드론 작업의 순번 또는 대상이 중복되거나 유효하지 않습니다.");
            }

            var creation = new DroneTaskCreationDecisionElement { Kind = entry.Kind, Target = target, ItemType = item };
            switch (entry.Kind)
            {
                case DroneLogisticsTaskKindEnum.ConstructionSupply when manager.HasComponent<ConstructionSite>(target) && entry.Quantity == 0:
                    break;
                case DroneLogisticsTaskKindEnum.WorldItemRecovery when manager.HasComponent<ItemIdentity>(target) && manager.GetComponentData<ItemIdentity>(target).Type == item && entry.Quantity == 0:
                    creation.RecoveryReason = DroneRecoveryReasonEnum.SiteClearance;
                    break;
                case DroneLogisticsTaskKindEnum.StorageExport when manager.HasComponent<ResearchCycleState>(target) && entry.Quantity > 0:
                    creation.Area = DroneTransportAreaEnum.Input;
                    creation.Purpose = DroneTransportPurposeEnum.ResearchRecovery;
                    creation.Quantity = entry.Quantity;
                    break;
                default:
                    throw new InvalidDataException("지원하지 않는 자동 드론 작업 또는 대상입니다.");
            }
            // 먼저 복원된 실물을 대상으로 연결하고 기존 작업 순번을 기록한다. World의 다음 발급 순번은 별도로 복원한다.
            var savedSequence = new DroneTaskSequence { NextValue = entry.CreationSequence };
            DroneTaskCreationUtility.RecordCreation(ecb, creation, ref savedSequence);
        }
    }
}
