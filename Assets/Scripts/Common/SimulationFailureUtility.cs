using Unity.Collections;
using Unity.Entities;

public static class SimulationFailureUtility
{
    /// <summary>현재 ECB 경계에서 오류를 게시한다. 다음 게임 틱은 실행하지 않는다. Rollback은 아니다.</summary>
    public static void Record(ref EntityCommandBuffer ecb, in FixedString128Bytes message)
    {
        UnityEngine.Debug.LogError(message);
        Entity error = ecb.CreateEntity();
        ecb.AddComponent(error, new SimulationFatalError { Message = message });
    }
}
