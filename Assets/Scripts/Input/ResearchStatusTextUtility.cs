using System.Text;
using Unity.Entities;

/// <summary>연구건물의 현재 원본을 정보 문구로 투영한다. 초기화 원인은 이 값에서 추정하지 않고 별도 공개 결과를 사용한다.</summary>
public static class ResearchStatusTextUtility
{
    public static void Append(EntityManager manager, Entity building, StringBuilder text)
    {
        using var registryQuery = manager.CreateEntityQuery(typeof(ResearchRegistry), typeof(ResearchState),
            typeof(ResearchDefinitionElement), typeof(ResearchIngredientElement));
        if (registryQuery.CalculateEntityCount() != 1)
        {
            text.AppendLine("연구 설정 준비 중");
            return;
        }
        Entity registry = registryQuery.GetSingletonEntity();
        var active = manager.GetComponentData<ResearchState>(registry).ActiveResearchId;
        if (active.IsEmpty)
        {
            text.AppendLine("활성 연구 없음");
            return;
        }
        foreach (var definition in manager.GetBuffer<ResearchDefinitionElement>(registry, true))
        {
            if (definition.StableId != active)
            {
                continue;
            }
            text.AppendLine("전역 연구: " + definition.DisplayName);
            var cycle = manager.GetComponentData<ResearchCycleState>(building);
            bool cycleMatchesActiveResearch = cycle.ResearchId == active;
            float elapsed = cycleMatchesActiveResearch ? cycle.ElapsedTime : 0f;
            text.AppendLine("로컬 1주기 " + elapsed.ToString("F2") + "/" + definition.CycleDuration.ToString("F2"));
            bool materialsReady = true;
            var ingredients = manager.GetBuffer<ResearchIngredientElement>(registry, true);
            DynamicBuffer<StoredItemElement> storedItems = default;
            for (int i = 0; i < definition.IngredientCount; i++)
            {
                var ingredient = ingredients[definition.IngredientStart + i];
                int count = 0;
                if (!storedItems.IsCreated)
                {
                    storedItems = manager.GetBuffer<StoredItemElement>(building, true);
                }
                foreach (var stored in storedItems)
                {
                    if (stored.ItemType == ingredient.ItemType &&
                        ItemOwnershipApplySystem.CanTransferItem(manager, stored.ItemEntity, stored.ItemType, building))
                    {
                        count++;
                    }
                }
                materialsReady &= count >= ingredient.Amount;
                text.AppendLine("1주기 " + ingredient.ItemType + " " + count + "/" + ingredient.Amount);
            }
            int recovery = manager.HasBuffer<ResearchRecoveryElement>(building) ? manager.GetBuffer<ResearchRecoveryElement>(building, true).Length : 0;
            text.AppendLine("회수 대기 " + recovery + "개");
            float ratio = manager.HasComponent<PowerSupplyResult>(building) ? manager.GetComponentData<PowerSupplyResult>(building).SupplyRatio : 0f;
            bool demolitionPending = manager.HasComponent<PendingBuildingDemolition>(building);
            string status = ReadStatus(demolitionPending, cycleMatchesActiveResearch, ratio, recovery, materialsReady);
            text.AppendLine("상태: " + status);
            if (manager.HasComponent<ResearchWorkPreparation>(building))
            {
                var prepared = manager.GetComponentData<ResearchWorkPreparation>(building);
                if (manager.IsComponentEnabled<ResearchWorkPreparation>(building) && prepared.DefinitionIndex >= 0)
                {
                    text.AppendLine("연구 속도 " + prepared.Speed.ToString("F2") + " / 공급 반영 " + (prepared.Speed * ratio).ToString("F2"));
                }
            }
            return;
        }
    }

    private static string ReadStatus(bool demolitionPending, bool cycleMatchesActiveResearch, float supplyRatio, int recoveryCount, bool materialsReady)
    {
        // 철거 승인에 따른 수요 차단은 공급 부족과 원인이 다르므로 먼저 표시한다.
        if (demolitionPending)
        {
            return "철거 대기 / 연구 중단";
        }
        if (cycleMatchesActiveResearch)
        {
            if (supplyRatio <= 0f)
            {
                return "전력 없음 / 진행 보존";
            }
            if (supplyRatio < 1f)
            {
                return "전력 부족으로 감속 중";
            }
            return "연구 진행 중";
        }
        if (recoveryCount > 0)
        {
            return "불필요 재료 회수 대기";
        }
        if (!materialsReady)
        {
            return "재료 대기";
        }
        return supplyRatio <= 0f ? "전력 없음 / 착수 대기" : "다음 주기 대기";
    }
}
