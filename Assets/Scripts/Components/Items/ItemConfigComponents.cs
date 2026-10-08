using Unity.Entities;

/// <summary>
/// 역할·목적: 품목별 슬롯 최대 실물 개수 MaxStack을 제공하는 정적 설정이다. 품목 번호와 버퍼 인덱스가 대응한다.
/// 부착 엔티티: World 단일 ItemRegistry 설정 엔티티의 버퍼다.
/// 생성: ItemConfigInitSystem(Initialization)이 모든 실제 품목의 명시적 양수 설정을 검증하여 한 번 게시한다.
/// 이용: CrafterRecipeCommandSystem(Command)의 전용 슬롯 계산, BuildingStorageInputReservationSystem(Reservation), 생산·드론/입출고의 재고·공간 검사가 읽는다.
/// 제거: 게시 후 World 수명 동안 읽기 전용이며 실행 중 교체/삭제를 지원하지 않는다. 버퍼 해제는 World가 관리한다.
/// </summary>
[InternalBufferCapacity(0)]
public struct ItemConfigElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int MaxStack;

    public ItemConfigElement(ItemTypeEnum itemType, int maxStack)
    {
        ItemType = itemType;
        MaxStack = maxStack;
    }
}

/// <summary>
/// 역할·목적: ItemConfigElement 버퍼의 World 단일 설정 소유자를 식별하는 태그다.
/// 부착 엔티티: 아이템 실물과 별도인 설정 엔티티다.
/// 생성: ItemConfigInitSystem(Initialization)이 한 번 게시하며 사전 등록도 InitializeItemRegistry 경계로 수행한다. 기존 설정의 재게시를 거부한다.
/// 이용: 제작 Command·채굴/제작/출고 Decision·입고 Reservation·드론 계획/인계가 버퍼와 함께 읽어 슬롯 여유·품목 제한을 계산한다.
/// 제거: 초기화 시스템 종료 때 삭제하지 않는다. 버퍼와 엔티티는 World 수명에 속하며 실행 중 교체/삭제를 지원하지 않는다.
/// 조회는 원본 버퍼만 사용한다. None·범위 밖·인덱스 불일치는 용량 0이며 기본값으로 대체하지 않는다.
/// </summary>
public struct ItemRegistry : IComponentData
{
    public static int GetMaxStack(DynamicBuffer<ItemConfigElement> items, ItemTypeEnum itemType)
    {
        int index = (int)itemType;
        if (itemType == ItemTypeEnum.None || index >= items.Length)
        {
            return 0;
        }
        if (items[index].ItemType != itemType)
        {
            return 0;
        }
        return items[index].MaxStack;
    }
}
