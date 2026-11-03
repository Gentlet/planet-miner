using UnityEngine;

/// <summary>입력 원본은 WorldInputController에 두고, 도구가 소비한 프레임을 정보 선택에 중복 전달하지 않는다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WorldInputController), typeof(WorldConstructionController), typeof(WorldSelectionController))]
public sealed class WorldInteractionController : MonoBehaviour
{
    private WorldInputController _input;
    private WorldConstructionController _tools;
    private WorldSelectionController _selection;
    private bool _waitForRelease;
    private void OnEnable()
    {
        _input = GetComponent<WorldInputController>();
        _tools = GetComponent<WorldConstructionController>();
        _selection = GetComponent<WorldSelectionController>();
        _tools.RouteExternally();
        _waitForRelease = _input.CurrentFrame.Primary.HasFlag(WorldPointerButtonStateEnum.Held);
        _input.FrameReady += OnFrame;
    }

    private void OnDisable()
    {
        if (_input != null)
        {
            _input.FrameReady -= OnFrame;
        }

        if (_tools != null)
        {
            _tools.CancelGesture();
        }

        _waitForRelease = true;
    }

    private void OnFrame(WorldInputFrame frame)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (_waitForRelease)
        {
            if (frame.Primary.HasFlag(WorldPointerButtonStateEnum.Released))
            {
                _waitForRelease = false;
            }
            else
            {
                frame = new WorldInputFrame(frame.FrameNumber, frame.HasWorldPosition, frame.ScreenPosition, frame.WorldPosition, frame.GridCell, WorldPointerButtonStateEnum.None, frame.Secondary, frame.Construction);
            }
        }

        bool consumed = _tools.isActiveAndEnabled && _tools.HandleFrame(frame);
        if (!consumed && frame.HasWorldPosition && frame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed))
        {
            _selection.SelectAt(frame.WorldPosition, frame.GridCell);
        }

        if (frame.Construction.Has(WorldConstructionCommandEnum.Cancel))
        {
            _selection.Clear();
        }
    }
}
