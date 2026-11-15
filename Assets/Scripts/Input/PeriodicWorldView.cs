using UnityEngine;

/// <summary>각 View 인스턴스의 비배율 0.2초 갱신 주기. 활성화/명시적 갱신은 기존 파생형이 소유한다.</summary>
public abstract class PeriodicWorldView : MonoBehaviour
{
    private float _nextRefresh;

    protected void LateUpdate()
    {
        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }

        _nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    public abstract void Refresh();
}
