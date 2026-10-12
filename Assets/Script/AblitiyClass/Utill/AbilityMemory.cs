using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 능력이 라운드 동안 들고 있어야 하는 값 (SA 전용 메모리).
/// Behaviour는 사망·랜덤 능력 교체 때마다 새로 만들어지므로, 쌓아 둔 값은 여기에 둠.
/// 플레이어 오브젝트에 자동으로 붙음 — 프리팹에 따로 넣을 필요 없음.
/// 라운드가 바뀌면 각 능력이 Ctx.GetSaved(능력ID) == 0 으로 알아채고 Remove로 비움.
/// </summary>
public class AbilityMemory : MonoBehaviour
{
    private readonly Dictionary<string, object> _data = new();

    public static AbilityMemory Of(Component c)
    {
        var m = c.GetComponent<AbilityMemory>();
        return m != null ? m : c.gameObject.AddComponent<AbilityMemory>();
    }

    public T GetOrCreate<T>(string key) where T : class, new()
    {
        if (_data.TryGetValue(key, out var v) && v is T t) return t;
        var n = new T();
        _data[key] = n;
        return n;
    }

    public void Remove(string key) => _data.Remove(key);
}
