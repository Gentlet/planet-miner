using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public partial class BuildingUI
{
    private void RefreshConstructionSite()
    {
        if (!_entityManager.Exists(_selectedConstructionSite) ||
            !_entityManager.HasComponent<ConstructionSite>(_selectedConstructionSite))
        {
            TryTransferConstructionSiteSelection();
            return;
        }

        if (!_entityManager.HasBuffer<ConstructionMaterialRequirementElement>(
                _selectedConstructionSite) ||
            !_entityManager.HasBuffer<StoredItemElement>(_selectedConstructionSite))
        {
            Close();
            return;
        }

        ConstructionSite site = _entityManager.GetComponentData<ConstructionSite>(
            _selectedConstructionSite);
        DynamicBuffer<ConstructionMaterialRequirementElement> requirements =
            _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(
                _selectedConstructionSite,
                true);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(
                _selectedConstructionSite,
                true);

        SetConstructionSiteLayout(GetBuildingDisplayName(site.type));
        CountItems(storedItems, _storedCounts, static item => item.type);

        int totalRequired = 0;
        int totalDelivered = 0;

        for (int index = 0; index < requirements.Length; index++)
        {
            ConstructionMaterialRequirementElement requirement = requirements[index];
            int delivered = _storedCounts.TryGetValue(
                requirement.itemType,
                out int storedCount)
                ? Mathf.Min(storedCount, requirement.quantity)
                : 0;

            totalRequired += requirement.quantity;
            totalDelivered += delivered;
            SetItemRow(
                _inputContainer,
                index,
                requirement.itemType,
                delivered,
                requirement.quantity,
                delivered >= requirement.quantity ? "납품 완료" : "납품 대기",
                false);
        }

        TrimItemRowsAndSetEmptyState(_inputContainer, requirements.Length);

        float progress = totalRequired > 0
            ? (float)totalDelivered / totalRequired
            : 0f;
        _progressBar.value = progress * 100f;
        _progressBar.title = $"{progress * 100f:0.#}%";
        _remainingTimeLabel.text = $"자재 납품: {totalDelivered} / {totalRequired}";
        _speedLabel.text = GetConstructionTransportStatus();
        _speedReasonLabel.text = string.Empty;
        SetStatus(
            totalDelivered >= totalRequired ? "완공 처리 대기" : "자재 납품 중",
            totalDelivered >= totalRequired ? "status-normal" : "status-waiting");
    }

    private void TryTransferConstructionSiteSelection()
    {
        if (_chunkMap.TryGetBuilding(
                _selectedConstructionSiteCell,
                out Entity buildingEntity) &&
            _entityManager.Exists(buildingEntity) &&
            IsSupportedBuilding(buildingEntity))
        {
            _selectedConstructionSite = Entity.Null;
            _selectedBuilding = buildingEntity;
            _nextRefreshTime = 0f;
            Refresh();
            return;
        }

        if (_chunkMap.IsBuildingReserved(_selectedConstructionSiteCell))
        {
            SetConstructionSiteLayout("건물");
            TrimItemRowsAndSetEmptyState(_inputContainer, 0);
            _progressBar.value = 100f;
            _progressBar.title = "100%";
            _remainingTimeLabel.text = "건물 생성 중";
            _speedLabel.text = string.Empty;
            _speedReasonLabel.text = string.Empty;
            SetStatus("완공 처리 중", "status-normal");
            return;
        }

        Close();
    }

    private string GetConstructionTransportStatus()
    {
        EntityQuery taskQuery = _droneBuildingTaskQuery;
        if (taskQuery == default)
        {
            taskQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        }

        using NativeArray<Entity> taskEntities =
            taskQuery.ToEntityArray(Allocator.Temp);

        int pendingCount = 0;
        int inProgressCount = 0;
        int suspendedCount = 0;

        for (int index = 0; index < taskEntities.Length; index++)
        {
            Entity taskEntity = taskEntities[index];
            DroneBuildingItemTaskData taskData = _entityManager
                .GetComponentData<DroneBuildingItemTaskData>(taskEntity);

            if (taskData.targetBuilding != _selectedConstructionSite)
                continue;

            if (!_entityManager.HasComponent<DroneTaskStatus>(taskEntity))
            {
                pendingCount++;
                continue;
            }

            DroneTaskStatus taskStatus = _entityManager
                .GetComponentData<DroneTaskStatus>(taskEntity);

            switch (taskStatus.state)
            {
                case DroneTaskStateEnum.InProgress:
                    inProgressCount++;
                    break;
                case DroneTaskStateEnum.Suspended:
                    suspendedCount++;
                    break;
                case DroneTaskStateEnum.Pending:
                    pendingCount++;
                    break;
            }
        }

        if (inProgressCount > 0)
            return $"운송 상태: 진행 중 {inProgressCount}건 · 대기 {pendingCount}건";

        if (suspendedCount > 0)
            return $"운송 상태: 중단 {suspendedCount}건 · 대기 {pendingCount}건";

        return pendingCount > 0
            ? $"운송 상태: 대기 {pendingCount}건"
            : "운송 상태: 작업 생성 중";
    }

    private void RefreshStaticBuilding()
    {
        BuildingType buildingType = _entityManager.GetComponentData<BuildingType>(
            _selectedBuilding);
        SetStaticBuildingLayout(GetBuildingDisplayName(buildingType.type));
        SetStatus("정상 가동", "status-normal");
    }

    private static string GetBuildingDisplayName(BuildingTypeEnum buildingType)
    {
        return buildingType switch
        {
            BuildingTypeEnum.Belt => "벨트",
            BuildingTypeEnum.Miner => "채굴기",
            BuildingTypeEnum.Crafter => "제작기",
            BuildingTypeEnum.Splitter => "분배기",
            BuildingTypeEnum.Merger => "병합기",
            BuildingTypeEnum.Storage => "창고",
            BuildingTypeEnum.PowerPole => "전신주",
            BuildingTypeEnum.CoalGenerator => "석탄 발전기",
            BuildingTypeEnum.MainFacility => "메인스테이션",
            BuildingTypeEnum.DroneStation => "드론 정거장",
            _ => "건물"
        };
    }
}
