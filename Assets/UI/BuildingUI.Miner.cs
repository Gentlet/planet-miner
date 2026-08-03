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
        float progressRatio = miner.speed > 0f
            ? math.saturate(miner.timer / miner.speed)
            : 0f;
        float progressPercent = progressRatio * 100f;

        _progressBar.value = progressPercent;
        _progressBar.title = $"{progressPercent:0}%";
        _remainingTimeLabel.text = miner.speed > 0f
            ? $"다음 채굴까지: {math.max(0f, miner.speed - miner.timer):0.0}초"
            : "채굴 대기";
        _speedLabel.text = miner.speed > 0f
            ? $"채굴 속도: {miner.speed:0.##}초당 1개"
            : "채굴 속도: 정지";
    }
}
