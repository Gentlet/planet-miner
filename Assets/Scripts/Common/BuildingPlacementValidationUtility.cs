using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 단일/묶음 배치의 회전 점유·자원 필요·해금·바닥 실물 여부를 검증한다.
/// 입력·출력: 직전 동기화의 공간 인덱스와 후보/설정을 읽고 후보별 승인/거부 값을 쓴다. 월드 상태와 인덱스를 변경하지 않는다.
/// 이용: BuildingPlacementCommandSystem(Command)과 BuildingToolWorld 미리보기가 같은 설정 조회와 검증 규칙을 사용한다.
/// 수명·경계: 묶음 내부 claimedCells와 Command가 전달한 최종 승인 셀을 함께 검사한다. 외부 승인 셀은 읽기만 하며 영속 예약이나 조기 공간 동기화가 아니다.
/// </summary>
[BurstCompile]
public static class BuildingPlacementValidationUtility
{
    private static PlacementValidationResult ValidateSinglePlacement(
        in PlacementCandidate candidate,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        bool isUnlocked)
    {
        if (candidate.TargetType == BuildingTypeEnum.MainFacility)
        {
            return new PlacementValidationResult(PlacementValidationCode.NotPlayerBuildable);
        }
        if (!isUnlocked)
        {
            return new PlacementValidationResult(PlacementValidationCode.BlockedByResearch);
        }

        if (candidate.TargetTier <= 0 || candidate.FootprintSize.x <= 0 || candidate.FootprintSize.y <= 0)
        {
            return new PlacementValidationResult(PlacementValidationCode.InvalidFootprint);
        }

        int2 effectiveSize = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
        if ((long)candidate.OriginPosition.x + effectiveSize.x - 1 > int.MaxValue ||
            (long)candidate.OriginPosition.y + effectiveSize.y - 1 > int.MaxValue)
        {
            return new PlacementValidationResult(PlacementValidationCode.InvalidFootprint);
        }

        bool hasResource = false;
        bool hasGroundItems = false;
        Entity beltToRedirect = Entity.Null;
        // 방향 변경은 기존 벨트의 원점에서 실행하므로 같은 원점의 회전 점유를 검사한다.
        if (candidate.TargetType == BuildingTypeEnum.Belt &&
            buildingMap.TryGetValue(candidate.OriginPosition, out var originBuilding) &&
            originBuilding.Type == BuildingTypeEnum.Belt && originBuilding.BuildingId == candidate.BuildingId &&
            math.all(originBuilding.OriginPosition == candidate.OriginPosition))
        {
            beltToRedirect = originBuilding.Entity;
        }

        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                int2 cell = candidate.OriginPosition + new int2(x, y);

                // 1. 기존 건물 및 공사 현장 점유 검사
                if (buildingMap.TryGetValue(cell, out var existingBuilding) &&
                    (beltToRedirect == Entity.Null || existingBuilding.Entity != beltToRedirect))
                {
                    if (existingBuilding.Type == BuildingTypeEnum.ConstructionSite)
                    {
                        return new PlacementValidationResult(PlacementValidationCode.BlockedByConstructionSite);
                    }
                    return new PlacementValidationResult(PlacementValidationCode.BlockedByBuilding);
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
        if (candidate.TargetType == BuildingTypeEnum.Miner && !hasResource)
        {
            return new PlacementValidationResult(PlacementValidationCode.RequiresResourceNode);
        }

        if (beltToRedirect != Entity.Null)
        {
            return new PlacementValidationResult(PlacementValidationCode.BeltUpgradeAllowed, hasGroundItems);
        }

        return new PlacementValidationResult(PlacementValidationCode.Success, hasGroundItems);
    }

    private static bool TryResolveCandidate(
        in NativeArray<BuildingConfigElement> configs,
        ref PlacementCandidate candidate,
        out BuildingConfigElement config)
    {
        if (!BuildingConfigLookupUtility.TryGetConfig(configs, candidate.BuildingId, out config))
        {
            return false;
        }

        candidate.TargetType = config.BuildingType;
        candidate.TargetTier = config.Tier;
        return true;
    }

    /// <summary>
    /// ID로 해석한 건물 정의와 현재 점유를 기준으로 묶음 배치를 검증한다.
    /// </summary>
    public static void ValidateBatchPlacement(
        NativeArray<PlacementCandidate> candidates,
        PlacementFlags flags,
        in NativeParallelHashMap<int2, BuildingInfo>.ReadOnly buildingMap,
        in NativeParallelHashMap<int2, Entity>.ReadOnly resourceMap,
        in NativeParallelMultiHashMap<int2, Entity>.ReadOnly itemMap,
        NativeArray<PlacementValidationResult> results,
        in NativeArray<BuildingConfigElement> configs,
        Allocator allocator = Allocator.Temp,
        NativeParallelHashSet<int2> approvedCells = default,
        NativeArray<PlacementValidationCode> settingsRejections = default)
    {
        var claimedCells = new NativeParallelHashSet<int2>(math.max(16, candidates.Length * 4), allocator);
        bool anyFailed = false;

        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (!TryResolveCandidate(configs, ref candidate, out var config))
            {
                results[i] = new PlacementValidationResult(PlacementValidationCode.UnknownBuildingId);
                anyFailed = true;
                continue;
            }
            candidates[i] = candidate;
            if (settingsRejections.IsCreated && settingsRejections[i] != PlacementValidationCode.Success)
            {
                results[i] = new PlacementValidationResult(settingsRejections[i]);
                anyFailed = true;
                continue;
            }
            // 전달 크기는 미리보기의 설정 스냅샷이다. 설정과 다르면 어떤 셀도 승인하거나 선점하지 않는다.
            if (config.IsUnlocked && !math.all(candidate.FootprintSize == config.Footprint))
            {
                results[i] = new PlacementValidationResult(PlacementValidationCode.InvalidFootprint);
                anyFailed = true;
                continue;
            }

            var singleResult = ValidateSinglePlacement(
                candidate,
                buildingMap,
                resourceMap,
                itemMap,
                config.IsUnlocked);

            if (!singleResult.IsValid)
            {
                results[i] = singleResult;
                anyFailed = true;
                continue;
            }

            int2 effectiveSize = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
            bool internallyConflict = false;

            for (int y = 0; y < effectiveSize.y && !internallyConflict; y++)
            {
                for (int x = 0; x < effectiveSize.x; x++)
                {
                    int2 cell = candidate.OriginPosition + new int2(x, y);
                    if (claimedCells.Contains(cell) ||
                        (approvedCells.IsCreated && approvedCells.Contains(cell)))
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

    /// <summary>Command가 최종 승인해 ECB에 기록하는 후보의 회전 셀을 등록한다. Strict로 거부한 후보에는 호출하지 않는다.</summary>
    public static void ClaimApprovedPlacement(
        in PlacementCandidate candidate,
        NativeParallelHashSet<int2> approvedCells)
    {
        int2 effectiveSize = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                approvedCells.Add(candidate.OriginPosition + new int2(x, y));
            }
        }
    }
}
