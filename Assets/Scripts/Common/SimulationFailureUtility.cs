using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: 복구되지 않는 생성/설정 오류를 로그와 SimulationFatalError로 게시해 다음 게임 틱을 차단한다.
/// 입력·출력: 초기화는 오류를 즉시 게시하고 실행 실패는 호출자의 ECB에 기록한다. 현재 틱의 이미 기록된 변경을 rollback하지 않는다.
/// 이용·수명: 설정/프리팹 초기화와 생성/환급 실패가 사용하며 실행 중 오류의 실체화 시점은 전달한 ECB Playback이 정한다.
/// </summary>
public static class SimulationFailureUtility
{
    /// <summary>초기화는 ECB를 기다리지 않고 같은 World의 기존 중단 오류를 확인한다.</summary>
    public static bool HasFatalError(EntityManager entityManager)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
        return !query.IsEmptyIgnoreFilter;
    }

    /// <summary>초기화 실패를 즉시 공개한다. 상세 로그와 길이가 제한된 ECS 진단 메시지를 분리한다.</summary>
    public static void RecordInitializationFailure(
        EntityManager entityManager, string details, in FixedString128Bytes message)
    {
        UnityEngine.Debug.LogError(details);
        Entity error = entityManager.CreateEntity(typeof(SimulationFatalError));
        entityManager.SetComponentData(error, new SimulationFatalError { Message = message });
    }

    /// <summary>현재 ECB 경계에서 오류를 게시한다. 다음 게임 틱은 실행하지 않는다. Rollback은 아니다.</summary>
    public static void Record(ref EntityCommandBuffer ecb, in FixedString128Bytes message)
    {
        UnityEngine.Debug.LogError(message);
        Entity error = ecb.CreateEntity();
        ecb.AddComponent(error, new SimulationFatalError { Message = message });
    }
}
