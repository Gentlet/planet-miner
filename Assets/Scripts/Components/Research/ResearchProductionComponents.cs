using Unity.Collections;
using Unity.Entities;

/// <summary>고정 품목 슬롯의 최초 구성을 구분한다. 빈 재료 목록도 유효하므로 버퍼 길이를 준비 완료로 사용하지 않는다.</summary>
public struct ResearchInputConfigured : IComponentData, IEnableableComponent
{
}

/// <summary>Preparation의 전력 외 자격/정의 인덱스/속도 스냅샷이다. enable은 이번 틱 준비 완료다.</summary>
public struct ResearchWorkPreparation : IComponentData, IEnableableComponent
{
    public int DefinitionIndex;
    public float Speed;
    public bool CanStartCycle;
    public bool CanAdvance;
}

/// <summary>Building Decision이 확정 공급을 반영한 실행 자격/시간이다. 전력 외 준비와 실제 소비를 분리한다.</summary>
public struct ResearchDecision : IComponentData, IEnableableComponent
{
    public bool CanStartCycle;
    public bool CanAdvance;
    public float PoweredTime;
}

/// <summary>현재 입력에 허용할 품목별 총량이다. 빈 버퍼는 이번 틱 허용된 입고 품목이 없음을 뜻한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchInputLimitElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int MaximumQuantity;
}

/// <summary>StoredItemElement 중 회수 대기인 실물의 분류다. 소유자는 연구건물이며 실제 픽업만 기존 인계 소유자가 처리한다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchRecoveryElement : IBufferElementData
{
    public Entity Item;
}

/// <summary>Execution이 한 주기 완료를 기록하고 StateApply가 EndBuilding에 소비하는 전역 기여 결과다.</summary>
[InternalBufferCapacity(0)]
public struct ResearchContributionElement : IBufferElementData
{
    public FixedString32Bytes ResearchId;
    public float Progress;
}
