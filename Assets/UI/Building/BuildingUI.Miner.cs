using Unity.Entities;
using Unity.Mathematics;

public partial class BuildingUI
{
    private void RefreshMiner(DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<ProducedItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetMinerLayout();
        Miner miner = _entityManager.GetComponentData<Miner>(_selectedBuilding);
        DynamicBuffer<ProducedItemElement> producedItems =
            _entityManager.GetBuffer<ProducedItemElement>(_selectedBuilding, true);

        CountItems(producedItems, _producedCounts, static item => item.type);
        UpdateSimpleInventory(
            _outputContainer,
            _producedCounts,
            storageLimits,
            "배출 대기");
        UpdateMinerStatus(miner, producedItems, storageLimits);
        RefreshPowerConsumer();
        UpdateMinerProgress(miner);
    }

    private void UpdateMinerStatus(
        Miner miner,
        DynamicBuffer<ProducedItemElement> producedItems,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (miner.speed <= 0f)
        {
            SetStatus("작동 정지", "status-error");
            return;
        }

        for (int i = 0; i < producedItems.Length; i++)
        {
            ItemTypeEnum itemType = producedItems[i].type;
            int capacity = storageLimits.GetStorageLimit(itemType);

            if (capacity > 0 &&
                _producedCounts.TryGetValue(itemType, out int count) &&
                count >= capacity)
            {
                SetStatus("채굴 아이템 보관함이 가득 찼습니다", "status-error");
                return;
            }
        }

        SetStatus("채굴 중", "status-normal");
    }

    private void UpdateMinerProgress(Miner miner)
    {
        float productionSpeedMultiplier = GetProductionSpeedMultiplier(
            out string speedChangeReason);
        float researchSpeedMultiplier = GetResearchSpeedMultiplier(
            ResearchStatModifierTypeEnum.MiningSpeed);
        productionSpeedMultiplier *= researchSpeedMultiplier;
        if (researchSpeedMultiplier > 1f)
            speedChangeReason += $" · 채굴 연구 보너스 ×{researchSpeedMultiplier:0.##}";
        _speedReasonLabel.text = speedChangeReason;
        float progressRatio = miner.speed > 0f
            ? math.saturate(miner.timer / miner.speed)
            : 0f;
        float progressPercent = progressRatio * 100f;

        _progressBar.value = progressPercent;
        _progressBar.title = $"{progressPercent:0}%";

        if (miner.speed > 0f && productionSpeedMultiplier > 0f)
        {
            float remainingTime = math.max(0f, miner.speed - miner.timer) /
                                  productionSpeedMultiplier;
            float effectiveMiningTime = miner.speed / productionSpeedMultiplier;
            float itemsPerMinute = 60f / effectiveMiningTime;
            _remainingTimeLabel.text = $"다음 채굴까지: {remainingTime:0.0}초";
            _speedLabel.text =
                $"채굴 속도: 1회 {effectiveMiningTime:0.##}초  ·  분당 {itemsPerMinute:0.#}개";
            return;
        }

        _remainingTimeLabel.text = miner.speed > 0f
            ? "다음 채굴까지: 전력 공급 대기"
            : "채굴 대기";
        _speedLabel.text = "채굴 속도: 정지";
    }
}
