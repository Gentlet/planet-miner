using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 일반 생성·생산 결과·철거 환급이 같은 프리팹 런타임 초기화 경계를 사용하게 한다.
/// 입력·출력: 검증된 프리팹과 품목·Owner·위치를 받아 호출자의 ECB에 인스턴스와 비활성 요청/이동/입고 결정 컴포넌트를 기록한다.
/// 이용: ItemLifecycleApplySystem과 BuildingLifecycleApplySystem이 호출한다. DB 조회·실패 처리·소유 버퍼 등록은 호출자가 소유한다.
/// 수명·가시화: 반환 Entity는 ECB의 지연 생성 참조다. 생성과 수납 렌더 태그는 호출자가 선택한 Playback에서 확정된다.
/// </summary>
public static class ItemLifecycleUtility
{
    public static Entity SpawnPrefabItem(
        ref EntityCommandBuffer ecb,
        Entity prefab,
        ItemTypeEnum itemType,
        int2 gridPosition,
        float3 worldPosition,
        ItemOwnership ownership)
    {
        Entity item = ecb.Instantiate(prefab);
        ecb.SetComponent(item, new ItemIdentity(itemType));
        ecb.AddComponent(item, new GridPosition(gridPosition));
        ecb.SetComponent(item, LocalTransform.FromPosition(worldPosition));
        ecb.AddComponent(item, ownership);

        // 요청/이동 컴포넌트는 미리 부착하되 비활성으로 시작해 생성 직후 자동 삭제/이전/이동하지 않게 한다.
        ecb.AddComponent<DestroyItemRequest>(item);
        ecb.SetComponentEnabled<DestroyItemRequest>(item, false);
        ecb.AddComponent<TransferOwnershipRequest>(item);
        ecb.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        ecb.AddComponent<BeltMovementState>(item);
        ecb.SetComponentEnabled<BeltMovementState>(item, false);
        ecb.AddComponent<BeltMovementDecision>(item);
        ecb.AddComponent<BuildingItemInputDecision>(item);
        ecb.SetComponentEnabled<BuildingItemInputDecision>(item, false);

        if (ownership.Owner != Entity.Null)
        {
            ecb.AddComponent<DisableRendering>(item);
        }

        return item;
    }
}
