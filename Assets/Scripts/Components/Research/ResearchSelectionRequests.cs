using Unity.Collections;
using Unity.Entities;

public enum ResearchSelectionStatusEnum : byte
{
    None,
    Selected,
    AlreadySelected,
    RejectedUnknownResearch,
    RejectedPrerequisites,
    RejectedCompleted
}

/// <summary>Submit이 World 접수 순번을 부여하는 불변 입력이다. Command가 처리하고 EndCommand에 소비한다.</summary>
public struct ResearchSelectionRequest : IRequestComponent
{
    public FixedString32Bytes ResearchId;
    public ulong Sequence;
}

/// <summary>같은 요청 엔티티에 EndCommand 공개하는 개별 접수 결과다. 현재 활성 연구는 ResearchState에서 읽는다.</summary>
public struct ResearchSelectionResult : IComponentData
{
    public FixedString32Bytes RequestedResearchId;
    public ResearchSelectionStatusEnum Status;
}

/// <summary>접수 API만 증가시키는 World 단일 순번 원본이다. 연구 진행 순서나 대기열이 아니다.</summary>
public struct ResearchSelectionRequestSequence : IComponentData
{
    public ulong NextValue;
}
