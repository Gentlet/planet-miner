using Unity.Entities;
using Unity.Mathematics;

public partial class BuildingUI
{
    private const string DisconnectedGridText = "접속망: 없음";

    private void RefreshPowerConsumer()
    {
        if (!_entityManager.HasComponent<PowerConsumer>(_selectedBuilding))
        {
            SetPowerLabels(
                DisconnectedGridText,
                "최대 소비량: 0",
                "공급률: 0%",
                "전력 상태: 미접속");
            return;
        }

        PowerConsumer consumer = _entityManager
            .GetComponentData<PowerConsumer>(_selectedBuilding);
        bool isConnected = TryGetConnectedGrid(
            _selectedBuilding,
            out _,
            out int stableGridId);
        string gridText = isConnected
            ? $"접속망: #{stableGridId}"
            : DisconnectedGridText;
        SetPowerLabels(
            gridText,
            $"최대 소비량: {consumer.maximumConsumption:0.##}",
            $"공급률: {math.saturate(consumer.supplyRatio) * 100f:0.#}%",
            $"전력 상태: {GetPowerState(consumer.supplyRatio, isConnected)}");
        ApplyPowerStatusOverride(consumer.supplyRatio, isConnected);
    }

    private void ApplyPowerStatusOverride(float supplyRatio, bool isConnected)
    {
        if (!isConnected)
        {
            SetStatus("전력 없음", "status-error");
            return;
        }

        if (supplyRatio <= 0f)
        {
            SetStatus("전력 없음", "status-error");
            return;
        }

        if (supplyRatio < 0.9999f)
            SetStatus("전력 부족", "status-waiting");
    }

    private void RefreshCoalGenerator()
    {
        if (!_entityManager.HasComponent<PowerGenerator>(_selectedBuilding))
        {
            Close();
            return;
        }

        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetPowerOnlyLayout("석탄 발전기");
        PowerGenerator generator = _entityManager
            .GetComponentData<PowerGenerator>(_selectedBuilding);
        CoalGenerator coalGenerator = _entityManager
            .GetComponentData<CoalGenerator>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);
        int coalCount = CountStoredCoal(storedItems);

        SetStatus(
            generator.currentGeneration > 0f ? "발전 중" : "발전 대기",
            generator.currentGeneration > 0f
                ? "status-normal"
                : "status-waiting");
        SetPowerLabels(
            GetConnectedGridText(_selectedBuilding),
            $"출력: {generator.currentGeneration:0.##} / {generator.maximumGeneration:0.##}",
            $"저장 석탄: {coalCount}개",
            $"남은 연료 에너지: {coalGenerator.remainingFuelEnergy:0.##}");
    }

    private void RefreshMainFacility()
    {
        if (!_entityManager.HasComponent<PowerGenerator>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetPowerOnlyLayout("메인스테이션");
        PowerGenerator generator = _entityManager
            .GetComponentData<PowerGenerator>(_selectedBuilding);
        bool isConnected = TryGetConnectedGrid(
            _selectedBuilding,
            out _,
            out int stableGridId);

        SetStatus(
            isConnected ? "기본 전력 공급 중" : "전력망 연결 대기",
            isConnected ? "status-normal" : "status-waiting");
        SetPowerLabels(
            isConnected ? $"접속망: #{stableGridId}" : DisconnectedGridText,
            $"출력: {generator.currentGeneration:0.##} / {generator.maximumGeneration:0.##}",
            "기본 발전원",
            "연료 소모 없음");
    }

    private void RefreshPowerPole()
    {
        SetPowerOnlyLayout("전신주");

        if (!TryGetConnectedGrid(
                _selectedBuilding,
                out Entity powerGridEntity,
                out int stableGridId))
        {
            SetStatus("전력망 등록 대기", "status-waiting");
            SetPowerLabels(
                DisconnectedGridText,
                "가용 발전량: 0",
                "실제 소비량: 0",
                "여유 전력: 0",
                "연결 건물: 0개");
            return;
        }

        if (!_entityManager.HasComponent<PowerGridState>(powerGridEntity))
        {
            Close();
            return;
        }

        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(powerGridEntity);
        SetStatus("전력망 연결됨", "status-normal");
        SetPowerLabels(
            $"접속망: #{stableGridId}",
            $"가용 발전량: {state.availableGeneration:0.##}",
            $"실제 소비량: {state.actualConsumption:0.##}",
            $"여유 전력: {state.sparePower:0.##}",
            $"연결 건물: {state.connectedBuildingCount}개");
    }

    private void SetPowerLabels(
        string grid,
        string primary,
        string secondary,
        string tertiary,
        string quaternary = "")
    {
        _powerGridLabel.text = grid;
        _powerPrimaryLabel.text = primary;
        _powerSecondaryLabel.text = secondary;
        _powerTertiaryLabel.text = tertiary;
        _powerQuaternaryLabel.text = quaternary;
        SetVisible(_powerQuaternaryLabel, quaternary.Length > 0);
    }

    private string GetConnectedGridText(Entity buildingEntity)
    {
        return TryGetConnectedGrid(buildingEntity, out _, out int stableGridId)
            ? $"접속망: #{stableGridId}"
            : DisconnectedGridText;
    }

    private bool TryGetConnectedGrid(
        Entity buildingEntity,
        out Entity powerGridEntity,
        out int stableGridId)
    {
        powerGridEntity = Entity.Null;
        stableGridId = 0;

        if (!_entityManager.HasComponent<PowerGridConnection>(buildingEntity))
            return false;

        powerGridEntity = _entityManager
            .GetComponentData<PowerGridConnection>(buildingEntity)
            .powerGridEntity;

        if (powerGridEntity == Entity.Null)
            return false;

        if (!_entityManager.Exists(powerGridEntity))
            return false;

        if (!_entityManager.HasComponent<PowerGrid>(powerGridEntity))
            return false;

        stableGridId = _entityManager
            .GetComponentData<PowerGrid>(powerGridEntity)
            .stableId;
        return true;
    }

    private static int CountStoredCoal(
        DynamicBuffer<StoredItemElement> storedItems)
    {
        int coalCount = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            if (storedItems[i].type == ItemTypeEnum.Coal)
                coalCount++;
        }

        return coalCount;
    }

    private static string GetPowerState(float supplyRatio, bool isConnected)
    {
        if (!isConnected)
            return "미접속";

        if (supplyRatio <= 0f)
            return "무전력";

        return supplyRatio < 0.9999f ? "전력 부족" : "정상 공급";
    }

    private float GetProductionSpeedMultiplier(out string reason)
    {
        if (!_entityManager.HasComponent<PowerConsumer>(_selectedBuilding))
        {
            reason = "속도 변경 이유: 전력 영향을 받지 않음";
            return 1f;
        }

        PowerConsumer consumer = _entityManager
            .GetComponentData<PowerConsumer>(_selectedBuilding);
        float supplyRatio = math.saturate(consumer.supplyRatio);
        bool isConnected = TryGetConnectedGrid(_selectedBuilding, out _, out _);

        if (!isConnected)
        {
            reason = "속도 변경 이유: 전력망 미접속";
            return 0f;
        }

        if (supplyRatio <= 0f)
        {
            reason = "속도 변경 이유: 전력 공급 없음 (0%)";
            return 0f;
        }

        if (supplyRatio < 0.9999f)
        {
            reason = $"속도 변경 이유: 전력 부족 (공급률 {supplyRatio * 100f:0.#}%)";
            return supplyRatio;
        }

        reason = "속도 변경 이유: 전력 정상 공급 (100%)";
        return 1f;
    }
}
