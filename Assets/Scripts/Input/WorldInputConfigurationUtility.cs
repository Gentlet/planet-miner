using UnityEngine;

/// <summary>카메라/입력 구성 실패를 기록한 뒤 해당 컴포넌트를 비활성화한다.</summary>
internal static class WorldInputConfigurationUtility
{
    internal static void Reject(MonoBehaviour owner, string componentName, string reason)
    {
        Debug.LogError($"{componentName}: {reason}", owner);
        owner.enabled = false;
    }
}
