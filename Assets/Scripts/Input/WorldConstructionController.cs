using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public enum BuildingToolModeEnum : byte
{
    None, Building, CopyRange, Blueprint, DemolitionRange
}

/// <summary>플레이어의 선택/제스처/배치 스냅샷만 소유한다. 실물/점유 변경은 공개 요청의 기존 소유자에게 맡긴다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WorldInputController))]
public sealed class WorldConstructionController : MonoBehaviour
{
    [SerializeField, Min(1)] private int _maximumCandidates = 1024;
    private WorldInputController _input;
    private World _world;
    private List<BuildingToolCatalogEntry> _catalog = new List<BuildingToolCatalogEntry>();
    private List<BuildingBlueprintElement> _blueprint = new List<BuildingBlueprintElement>();
    private readonly List<PlacementRequestCandidateElement> _candidates = new List<PlacementRequestCandidateElement>();
    private readonly List<BuildingToolPreviewItem> _preview = new List<BuildingToolPreviewItem>();
    private PlacementRequestCandidateElement _selected;
    private PlannedBuildingSettings _copiedSettings;
    private BuildingTypeEnum _settingsType;
    private bool _dragging;
    private bool _hasPointer;
    private int2 _start;
    private int2 _pointer;

    public BuildingToolModeEnum Mode { get; private set; }
    public string Status { get; private set; } = "건물을 선택하세요";
    public int CatalogVersion { get; private set; }
    public IReadOnlyList<BuildingToolCatalogEntry> Catalog => _catalog;
    public IReadOnlyList<BuildingToolPreviewItem> Preview => _preview;
    public bool HasSelectionRectangle => _dragging && (Mode == BuildingToolModeEnum.CopyRange || Mode == BuildingToolModeEnum.DemolitionRange);
    public int2 SelectionFirst => _start;
    public int2 SelectionLast => _pointer;
    public event Action Changed;

    private void OnEnable()
    {
        _input = GetComponent<WorldInputController>();
        _input.FrameReady += OnFrame;
        RefreshWorld();
    }

    private void OnDisable()
    {
        if (_input != null)
        {
            _input.FrameReady -= OnFrame;
        }
        CancelTool();
        _world = null;
        _catalog.Clear();
        _blueprint.Clear();
        _copiedSettings = default;
    }

    private bool RefreshWorld()
    {
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(current, _world))
        {
            _world = current;
            _blueprint.Clear();
            _copiedSettings = default;
            CancelTool();
        }
        if (!BuildingToolWorld.IsAvailable(_world))
        {
            _catalog.Clear();
            return false;
        }
        var next = BuildingToolWorld.ReadCatalog(_world.EntityManager);
        bool same = next.Count == _catalog.Count;
        for (int i = 0; same && i < next.Count; i++)
        {
            var a = next[i].Config;
            var b = _catalog[i].Config;
            same = a.BuildingType == b.BuildingType && a.Tier == b.Tier &&
                a.DisplayName == b.DisplayName && a.Footprint.Equals(b.Footprint);
        }
        if (!same)
        {
            _catalog = next;
            CatalogVersion++;
        }
        return true;
    }

    public void SelectBuilding(BuildingTypeEnum type, int tier)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        if (!RefreshWorld())
        {
            return;
        }
        foreach (var entry in _catalog)
        {
            if (entry.Config.BuildingType != type || entry.Config.Tier != tier)
            {
                continue;
            }
            if (Mode == BuildingToolModeEnum.Building && _selected.TargetType == type && _selected.TargetTier == tier)
            {
                CancelTool();
                return;
            }
            _selected = new PlacementRequestCandidateElement(type, tier, entry.Config.Footprint, int2.zero);
            Mode = BuildingToolModeEnum.Building;
            _dragging = false;
            Status = entry.Config.DisplayName.ToString() + " T" + tier;
            UpdatePreview();
            return;
        }
    }

    public void BeginCopyRange() => SetRangeMode(BuildingToolModeEnum.CopyRange, "복사할 범위를 드래그하세요");
    public void BeginDemolitionRange() => SetRangeMode(BuildingToolModeEnum.DemolitionRange, "철거/현장 취소/바닥 회수 범위를 드래그하세요");

    private void SetRangeMode(BuildingToolModeEnum mode, string status)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        Mode = mode;
        _dragging = false;
        _preview.Clear();
        Status = status;
        Changed?.Invoke();
    }

    public void CancelTool()
    {
        Mode = BuildingToolModeEnum.None;
        _dragging = false;
        _preview.Clear();
        _candidates.Clear();
        Status = "건물을 선택하세요";
        Changed?.Invoke();
    }

    private void OnFrame(WorldInputFrame frame)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        if (!RefreshWorld())
        {
            _preview.Clear();
            return;
        }
        if (frame.Construction.Has(WorldConstructionCommandEnum.Cancel))
        {
            CancelTool();
            return;
        }
        int slot = frame.Construction.SelectedSlot;
        if (slot > 0 && slot <= _catalog.Count)
        {
            var config = _catalog[slot - 1].Config;
            SelectBuilding(config.BuildingType, config.Tier);
        }
        if (frame.Construction.Has(WorldConstructionCommandEnum.Copy))
        {
            BeginCopyRange();
        }
        if (frame.Construction.Has(WorldConstructionCommandEnum.RangeDemolish))
        {
            BeginDemolitionRange();
        }
        if (frame.Construction.Has(WorldConstructionCommandEnum.Rotate))
        {
            Rotate();
        }

        bool cancelled = frame.Primary.HasFlag(WorldPointerButtonStateEnum.Cancelled) ||
            frame.Secondary.HasFlag(WorldPointerButtonStateEnum.Cancelled);
        _hasPointer = frame.HasWorldPosition;
        if (!_hasPointer || cancelled)
        {
            // UI/포커스 취소는 제스처만 버린다. 선택/복사본은 World 교체나 비활성화 때 정리한다.
            _dragging = false;
            _preview.Clear();
            Changed?.Invoke();
            return;
        }
        _pointer = frame.GridCell;
        if (frame.Secondary.HasFlag(WorldPointerButtonStateEnum.Pressed))
        {
            if (frame.Construction.SettingsModifier)
            {
                CopySettings();
            }
            else if (Mode != BuildingToolModeEnum.None)
            {
                CancelTool();
            }
            else
            {
                BuildingToolWorld.RequestTarget(_world.EntityManager, BuildingToolWorld.TargetAt(_world.EntityManager, _pointer));
            }
            return;
        }
        if (frame.Primary.HasFlag(WorldPointerButtonStateEnum.Pressed))
        {
            if (frame.Construction.SettingsModifier)
            {
                PasteSettings();
                return;
            }
        }
        ProcessPrimaryGesture(frame.Primary);
    }

    private void ProcessPrimaryGesture(WorldPointerButtonStateEnum buttons)
    {
        bool released = buttons.HasFlag(WorldPointerButtonStateEnum.Released);
        bool pressed = buttons.HasFlag(WorldPointerButtonStateEnum.Pressed);
        bool held = buttons.HasFlag(WorldPointerButtonStateEnum.Held);
        // 같은 갱신의 해제→새 누름은 이전 드래그부터 완료하고 마지막 Held를 새 제스처로 남긴다.
        if (released && _dragging)
        {
            UpdatePreview();
            CompleteGesture();
            _dragging = false;
        }
        if (pressed && Mode != BuildingToolModeEnum.None)
        {
            _start = _pointer;
            _dragging = true;
            if (released && !held)
            {
                UpdatePreview();
                CompleteGesture();
                _dragging = false;
            }
        }
        UpdatePreview();
    }

    private void Rotate()
    {
        if (Mode == BuildingToolModeEnum.Building)
        {
            _selected.Direction = (DirectionEnum)(((int)_selected.Direction + 1) % (int)DirectionEnum.Count);
        }
        if (Mode == BuildingToolModeEnum.Blueprint && !BuildingToolGeometry.TryRotate(_blueprint))
        {
            Status = "회전할 수 없는 좌표 범위입니다";
        }
    }

    private void UpdatePreview()
    {
        _preview.Clear();
        if (!_hasPointer || !BuildingToolWorld.IsAvailable(_world))
        {
            Changed?.Invoke();
            return;
        }
        bool valid = false;
        if (Mode == BuildingToolModeEnum.Building)
        {
            valid = BuildingToolGeometry.TryLine(_selected, _dragging ? _start : _pointer, _pointer, _maximumCandidates, _candidates);
        }
        else if (Mode == BuildingToolModeEnum.Blueprint)
        {
            valid = BuildingToolGeometry.TryBlueprint(_blueprint, _dragging ? _start : _pointer, _maximumCandidates, _candidates);
        }
        if (Mode == BuildingToolModeEnum.Building || Mode == BuildingToolModeEnum.Blueprint)
        {
            if (!valid)
            {
                Status = "배치 범위 또는 후보 수 제한을 초과했습니다";
            }
            else
            {
                BuildingToolWorld.Preview(_world.EntityManager, _candidates, _preview);
            }
        }
        Changed?.Invoke();
    }

    private void CompleteGesture()
    {
        if (Mode == BuildingToolModeEnum.CopyRange)
        {
            _blueprint = BuildingToolWorld.Copy(_world.EntityManager, _start, _pointer);
            Mode = _blueprint.Count > 0 ? BuildingToolModeEnum.Blueprint : BuildingToolModeEnum.None;
            Status = _blueprint.Count + "개 배치 정보 복사";
        }
        else if (Mode == BuildingToolModeEnum.DemolitionRange)
        {
            BuildingToolWorld.RequestRange(_world.EntityManager, _start, _pointer);
            Status = "범위 작업 요청을 접수했습니다";
        }
        else if (Mode == BuildingToolModeEnum.Building || Mode == BuildingToolModeEnum.Blueprint)
        {
            bool ready = BuildingToolWorld.Preview(_world.EntityManager, _candidates, _preview);
            if (ready && _candidates.Count > 0)
            {
                BuildingPlacementRequestUtility.Submit(_world.EntityManager,
                    new BuildingPlacementRequest(PlacementFlags.StrictAllOrNothing), _candidates.ToArray());
                Status = _candidates.Count + "개 묶음 요청 접수";
            }
            else
            {
                Status = "배치 불가능한 후보가 있어 전체 요청을 보내지 않았습니다";
            }
        }
    }

    private void CopySettings()
    {
        _dragging = false;
        Entity target = BuildingToolWorld.TargetAt(_world.EntityManager, _pointer);
        if (!BuildingToolWorld.TryCapture(_world.EntityManager, target, out var candidate))
        {
            Status = "복사할 설정이 없습니다";
            Changed?.Invoke();
            return;
        }
        if (candidate.Settings.HasValues)
        {
            _settingsType = candidate.TargetType;
            _copiedSettings = candidate.Settings;
            Status = "설정을 복사했습니다";
        }
        Changed?.Invoke();
    }

    private void PasteSettings()
    {
        _dragging = false;
        Entity target = BuildingToolWorld.TargetAt(_world.EntityManager, _pointer);
        Status = "설정을 적용할 수 없습니다";
        if (!BuildingToolWorld.TryCapture(_world.EntityManager, target, out var candidate))
        {
            Changed?.Invoke();
            return;
        }
        if (_world.EntityManager.HasComponent<ConstructionSite>(target))
        {
            Status = "설정 붙여넣기는 완공 건물만 지원합니다";
            Changed?.Invoke();
            return;
        }
        if (candidate.TargetType == _settingsType)
        {
            Entity request = BuildingSettingsUtility.Submit(_world.EntityManager, target, _copiedSettings);
            Status = request == Entity.Null ? "설정을 적용할 수 없습니다" : "설정 적용 요청 접수";
        }
        Changed?.Invoke();
    }
}
