using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>월드 카메라/건설 동작의 버튼 바인딩을 다룬다. 원본 에셋은 조회하고 비공개 복제본에만 override를 쓴다.</summary>
internal static class WorldInputBindingUtility
{
    private static readonly string[] ActionNames = BuildActionNames();

    private static string[] BuildActionNames()
    {
        var names = new string[WorldConstructionInput.ActionNames.Length + 2];
        names[0] = "Player/Move";
        names[1] = "Player/Sprint";
        for (int i = 0; i < WorldConstructionInput.ActionNames.Length; i++)
        {
            names[i + 2] = "Construction/" + WorldConstructionInput.ActionNames[i];
        }
        return names;
    }

    public static WorldInputBindingDescriptor[] Describe(InputActionAsset source, WorldInputBindingOverride[] overrides)
    {
        var descriptions = new List<WorldInputBindingDescriptor>();
        foreach (string actionName in ActionNames)
        {
            var action = source.FindAction(actionName, true);
            foreach (var binding in action.bindings)
            {
                if (!IsRebindable(binding))
                {
                    continue;
                }
                string id = binding.id.ToString();
                string path = binding.path;
                foreach (var replacement in overrides)
                {
                    if (Guid.TryParse(replacement.BindingId, out Guid overrideId) && overrideId == binding.id &&
                        replacement.OverridePath != null)
                    {
                        path = replacement.OverridePath;
                        break;
                    }
                }
                descriptions.Add(new WorldInputBindingDescriptor(id, actionName, binding.name, binding.path, path));
            }
        }
        return descriptions.ToArray();
    }

    public static bool TryApplyOverrides(InputActionAsset candidate, WorldInputBindingOverride[] overrides, out string error)
    {
        error = null;
        var seen = new HashSet<Guid>();
        foreach (var replacement in overrides)
        {
            if (!Guid.TryParse(replacement.BindingId, out Guid id) || !seen.Add(id))
            {
                error = "키 설정은 중복되지 않는 유효한 binding ID가 필요합니다.";
                return false;
            }
            if (!TryFindBinding(candidate, id, out var action, out int bindingIndex))
            {
                error = "현재 월드 입력의 Keyboard&Mouse 바인딩에 없는 ID입니다.";
                return false;
            }
            if (replacement.OverridePath == null)
            {
                continue;
            }
            if (replacement.OverridePath.Length > 0 && !IsButtonPath(replacement.OverridePath))
            {
                error = "키 설정은 Keyboard/Mouse의 단일 버튼 경로가 필요합니다.";
                return false;
            }
            action.ApplyBindingOverride(bindingIndex, replacement.OverridePath);
        }
        return true;
    }

    private static bool TryFindBinding(InputActionAsset source, Guid id, out InputAction action, out int index)
    {
        foreach (string name in ActionNames)
        {
            var candidate = source.FindAction(name, true);
            for (int i = 0; i < candidate.bindings.Count; i++)
            {
                if (candidate.bindings[i].id == id && IsRebindable(candidate.bindings[i]))
                {
                    action = candidate;
                    index = i;
                    return true;
                }
            }
        }
        action = null;
        index = -1;
        return false;
    }

    private static bool IsRebindable(InputBinding binding)
    {
        if (binding.isComposite)
        {
            return false;
        }
        if (string.IsNullOrEmpty(binding.groups))
        {
            return false;
        }
        foreach (string group in binding.groups.Split(';'))
        {
            if (string.Equals(group, "Keyboard&Mouse", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsButtonPath(string path)
    {
        if (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (path.IndexOfAny(new[] { '*', '?', '{', '}', '#' }) >= 0)
        {
            return false;
        }
        string layout = InputControlPath.TryGetControlLayout(path);
        if (string.IsNullOrEmpty(layout))
        {
            return false;
        }
        return InputSystem.IsFirstLayoutBasedOnSecond(layout, "Button");
    }
}
