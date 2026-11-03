using Unity.Entities;

/// <summary>World UI 소유 요청이다. 0은 피드백 없는 건설 요청이며 양수 토큰은 활성 화면의 한 등록 수명을 가리킨다.</summary>
public struct WorldUiRequestOwner : IComponentData
{
    public ulong FeedbackToken;
}
