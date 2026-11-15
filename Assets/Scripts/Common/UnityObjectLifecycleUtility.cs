using UnityEngine;

/// <summary>표시 소유자가 만든 Unity 오브젝트를 실행 모드에 맞춰 해제한다.</summary>
internal static class UnityObjectLifecycleUtility
{
    internal static void DestroyOwned(Object value)
    {
        if (value == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(value);
        }
        else
        {
            Object.DestroyImmediate(value);
        }
    }
}
