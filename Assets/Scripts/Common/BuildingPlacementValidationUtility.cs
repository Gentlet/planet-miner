using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 배치 타당성 검증 순수 함수 유틸리티.
/// 
/// [책임]
/// - UI 프리뷰 시스템과 런타임 배치 시스템(Phase 7 CommandGroup)에서 공통으로 호출되는 검증 유틸리티.
/// - Unmanaged 공간 인덱스(BuildingSpatialIndex, ResourceSpatialIndex, ItemSpatialIndex)의 ReadOnly 뷰를 직접 조회.
/// - 단일 건물 및 다중 배치 묶음(Batch)의 경합·충돌·중재(PlacementFlags)를 무비용(O(1) 해시 조회, Zero GC Alloc)으로 판정.
/// </summary>
[BurstCompile]
public static class BuildingPlacementValidationUtility
{
    /// <summary>
    /// 건물 회전 방향에 따른 유효 점유 크기(가로, 세로)를 반환.
    /// Left / Right (90도 회전)인 경우 가로와 세로가 스왑됨.
    /// </summary>
    public static int2 GetEffectiveSize(int2 footprintSize, DirectionEnum direction)
    {
        int2 normalized = math.max(footprintSize, new int2(1, 1));
        return (direction == DirectionEnum.Left || direction == DirectionEnum.Right)
            ? new int2(normalized.y, normalized.x)
            : normalized;
    }

    /// <summary>
    /// 단일 건물 배치 요청에 대한 유효성을 검증.
    /// </summary>
    /// <param name="targetType">설치하려는 건물 타입</param>
    /// <param name="footprintSize">건물 기본 크기</param>
    /// <param name="originPosition">건물 기준 좌표 (회전된 바운딩 박스의 좌하단 Min Cell)</param>
    /// <param name="direction">건물 배치 방향</param>
    /// <param name="buildingMap">건물 공간 인덱스 ReadOnly 뷰</param>
    /// <param name="resourceMap">자원 노드 공간 인덱스 ReadOnly 뷰</param>
    /// <param name="itemMap">월드 아이템 공간 인덱스 ReadOnly 뷰</param>
    /// <returns>검증 결과 (성공, 실패 원인, 바닥 아이템 여부)</returns>
    public static PlacementValidationResult ValidateSinglePlacement(
        BuildingTypeEnum targetType,
        int2 footprintSize,
        int2 originPosition,
        DirectionEnum direction,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        bool isUnlocked = true)
    {
        if (!isUnlocked)
        {
            return new PlacementValidationResult(PlacementValidationCode.BlockedByResearch);
        }

        if (footprintSize.x <= 0 || footprintSize.y <= 0)
        {
            return new PlacementValidationResult(PlacementValidationCode.InvalidFootprint);
        }

        int2 effectiveSize = GetEffectiveSize(footprintSize, direction);

        bool hasResource = false;
        bool hasGroundItems = false;
        bool isBeltUpgrade = false;

        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                int2 cell = originPosition + new int2(x, y);

                // 1. 기존 건물 및 공사 현장 점유 검사
                if (buildingMap.TryGetValue(cell, out var existingBuilding))
                {
                    if (targetType == BuildingTypeEnum.Belt && existingBuilding.Type == BuildingTypeEnum.Belt)
                    {
                        // 기존 벨트 위에 새 벨트를 설치하는 경우 업그레이드/방향 전환 허용
                        isBeltUpgrade = true;
                    }
                    else if (existingBuilding.Type == BuildingTypeEnum.ConstructionSite)
                    {
                        return new PlacementValidationResult(PlacementValidationCode.BlockedByConstructionSite);
                    }
                    else
                    {
                        return new PlacementValidationResult(PlacementValidationCode.BlockedByBuilding);
                    }
                }

                // 2. 자원 노드 검사 (일반 건물은 자원 위에도 배치 가능)
                if (resourceMap.ContainsKey(cell))
                {
                    hasResource = true;
                }

                // 3. 바닥 월드 아이템 검사 (배치를 차단하지는 않고 회수 대기 플래그에 활용)
                if (itemMap.ContainsKey(cell))
                {
                    hasGroundItems = true;
                }
            }
        }

        // 4. 채굴기(Miner) 전용 자원 충족 검사: 풋프린트 내 자원 노드가 최소 1개 이상 존재해야 함
        if (targetType == BuildingTypeEnum.Miner && !hasResource)
        {
            return new PlacementValidationResult(PlacementValidationCode.RequiresResourceNode);
        }

        if (isBeltUpgrade)
        {
            return new PlacementValidationResult(PlacementValidationCode.BeltUpgradeAllowed, hasGroundItems);
        }

        return new PlacementValidationResult(PlacementValidationCode.Success, hasGroundItems);
    }

    /// <summary>
    /// 동일 프레임 다중 배치 묶음(Batch)의 경합 및 유효성을 일괄 검증.
    /// </summary>
    /// <param name="candidates">배치 후보 목록</param>
    /// <param name="flags">충돌 중재 플래그 (StrictAllOrNothing 기본값, AllowPartialPlacement)</param>
    /// <param name="buildingMap">건물 공간 인덱스 ReadOnly 뷰</param>
    /// <param name="resourceMap">자원 노드 공간 인덱스 ReadOnly 뷰</param>
    /// <param name="itemMap">월드 아이템 공간 인덱스 ReadOnly 뷰</param>
    /// <param name="results">각 후보별 검증 결과 출력 버퍼 (candidates와 동일 크기)</param>
    /// <param name="allocator">선점 추적용 임시 할당자</param>
    public static void ValidateBatchPlacement(
        NativeArray<PlacementCandidate> candidates,
        PlacementFlags flags,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        NativeArray<PlacementValidationResult> results,
        Allocator allocator = Allocator.Temp)
    {
        var claimedCells = new NativeParallelHashSet<int2>(math.max(16, candidates.Length * 4), allocator);
        bool anyFailed = false;

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            var singleResult = ValidateSinglePlacement(
                candidate.TargetType,
                candidate.FootprintSize,
                candidate.OriginPosition,
                candidate.Direction,
                buildingMap,
                resourceMap,
                itemMap);

            if (!singleResult.IsValid)
            {
                results[i] = singleResult;
                anyFailed = true;
                continue;
            }

            // 동일 묶음 내 앞선 후보와의 셀 선점 충돌 검사
            int2 effectiveSize = GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
            bool internallyConflict = false;

            for (int y = 0; y < effectiveSize.y && !internallyConflict; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    int2 cell = candidate.OriginPosition + new int2(x, y);
                    if (claimedCells.Contains(cell))
                    {
                        internallyConflict = true;
                        break;
                    }
                }
            }

            if (internallyConflict)
            {
                // 동일 프레임 선점 충돌: 앞선 요청에 의해 차단됨
                results[i] = new PlacementValidationResult(PlacementValidationCode.BlockedByConstructionSite);
                anyFailed = true;
                continue;
            }

            // 충돌이 없으면 셀 선점 등록
            for (int y = 0; y < effectiveSize.y; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    claimedCells.Add(candidate.OriginPosition + new int2(x, y));
                }
            }

            results[i] = singleResult;
        }

        claimedCells.Dispose();

        // 중재 정책 적용: AllowPartialPlacement 플래그가 없는 경우(기본 StrictAllOrNothing)
        // 단 1개라도 실패하면 전체 묶음을 롤백 처리
        bool allowPartial = (flags & PlacementFlags.AllowPartialPlacement) != 0;
        if (!allowPartial && anyFailed)
        {
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].IsValid)
                {
                    // 원래 유효했으나 묶음 전체 실패로 인해 연쇄 취소됨
                    results[i] = new PlacementValidationResult(PlacementValidationCode.BatchAllOrNothingRolledBack);
                }
            }
        }
    }

    /// <summary>
    /// BuildingConfig 버퍼를 참조하여 연구 해금 상태까지 함께 검증하는 배치 묶음 검증 오버로드.
    /// </summary>
    public static void ValidateBatchPlacement(
        NativeArray<PlacementCandidate> candidates,
        PlacementFlags flags,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        NativeArray<PlacementValidationResult> results,
        in DynamicBuffer<BuildingConfigElement> configBuffer,
        Allocator allocator = Allocator.Temp)
    {
        var claimedCells = new NativeParallelHashSet<int2>(math.max(16, candidates.Length * 4), allocator);
        bool anyFailed = false;

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            bool isUnlocked = configBuffer.IsEmpty || BuildingConfigLookupUtility.IsBuildingUnlocked(configBuffer, candidate.TargetType);

            var singleResult = ValidateSinglePlacement(
                candidate.TargetType,
                candidate.FootprintSize,
                candidate.OriginPosition,
                candidate.Direction,
                buildingMap,
                resourceMap,
                itemMap,
                isUnlocked);

            if (!singleResult.IsValid)
            {
                results[i] = singleResult;
                anyFailed = true;
                continue;
            }

            int2 effectiveSize = GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
            bool internallyConflict = false;

            for (int y = 0; y < effectiveSize.y && !internallyConflict; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    int2 cell = candidate.OriginPosition + new int2(x, y);
                    if (claimedCells.Contains(cell))
                    {
                        internallyConflict = true;
                        break;
                    }
                }
            }

            if (internallyConflict)
            {
                results[i] = new PlacementValidationResult(PlacementValidationCode.BlockedByConstructionSite);
                anyFailed = true;
                continue;
            }

            for (int y = 0; y < effectiveSize.y; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    claimedCells.Add(candidate.OriginPosition + new int2(x, y));
                }
            }

            results[i] = singleResult;
        }

        claimedCells.Dispose();

        bool allowPartial = (flags & PlacementFlags.AllowPartialPlacement) != 0;
        if (!allowPartial && anyFailed)
        {
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].IsValid)
                {
                    results[i] = new PlacementValidationResult(PlacementValidationCode.BatchAllOrNothingRolledBack);
                }
            }
        }
    }

    /// <summary>
    /// BuildingConfig NativeArray를 참조하여 연구 해금 상태까지 함께 검증하는 배치 묶음 검증 오버로드.
    /// </summary>
    public static void ValidateBatchPlacement(
        NativeArray<PlacementCandidate> candidates,
        PlacementFlags flags,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        NativeArray<PlacementValidationResult> results,
        in NativeArray<BuildingConfigElement> configs,
        Allocator allocator = Allocator.Temp)
    {
        var claimedCells = new NativeParallelHashSet<int2>(math.max(16, candidates.Length * 4), allocator);
        bool anyFailed = false;

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            bool isUnlocked = !configs.IsCreated || configs.Length == 0 || BuildingConfigLookupUtility.IsBuildingUnlocked(configs, candidate.TargetType);

            var singleResult = ValidateSinglePlacement(
                candidate.TargetType,
                candidate.FootprintSize,
                candidate.OriginPosition,
                candidate.Direction,
                buildingMap,
                resourceMap,
                itemMap,
                isUnlocked);

            if (!singleResult.IsValid)
            {
                results[i] = singleResult;
                anyFailed = true;
                continue;
            }

            int2 effectiveSize = GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
            bool internallyConflict = false;

            for (int y = 0; y < effectiveSize.y && !internallyConflict; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    int2 cell = candidate.OriginPosition + new int2(x, y);
                    if (claimedCells.Contains(cell))
                    {
                        internallyConflict = true;
                        break;
                    }
                }
            }

            if (internallyConflict)
            {
                results[i] = new PlacementValidationResult(PlacementValidationCode.BlockedByConstructionSite);
                anyFailed = true;
                continue;
            }

            for (int y = 0; y < effectiveSize.y; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    claimedCells.Add(candidate.OriginPosition + new int2(x, y));
                }
            }

            results[i] = singleResult;
        }

        claimedCells.Dispose();

        bool allowPartial = (flags & PlacementFlags.AllowPartialPlacement) != 0;
        if (!allowPartial && anyFailed)
        {
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].IsValid)
                {
                    results[i] = new PlacementValidationResult(PlacementValidationCode.BatchAllOrNothingRolledBack);
                }
            }
        }
    }
}
