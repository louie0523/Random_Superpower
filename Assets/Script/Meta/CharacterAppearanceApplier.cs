using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 모델에 꾸미기 부위를 입힘 — 모델 루트(Animator 있는 곳)에 붙여 두면 됨.
/// 타이틀 · 꾸미기 미리보기 · 대기실 사진 · 게임 안(PlayerCosmetics) 모두 이걸로 → 어디서 봐도 같은 모습.
///   칸(머리 · 몸 · 안경 · 모자 · 손 치장 …)마다 하나 — 안 고른 칸은 칸의 기본 부위
///   리깅된 부위(SkinnedMesh) = 캐릭터 뼈에 이름으로 맞춰 입힘 / 일반 메시 = 뼈에 붙임 / 재질 = 기본 렌더러 재질 바꿈
/// 뼈 찾기: 부위의 Bone → 칸의 Bone → 사람형 뼈(칸의 Human Bone) → 이름에 그 뼈 이름
/// </summary>
public class CharacterAppearanceApplier : MonoBehaviour
{
    [Tooltip("비우면 GameMeta의 꾸미기 목록")]
    public CosmeticDatabase Database;

    readonly List<GameObject> _spawned = new();
    readonly List<Renderer> _hidden = new();
    readonly List<(Renderer r, Material[] mats)> _swapped = new();
    readonly Dictionary<string, Transform> _bones = new();
    string _applied;

    CosmeticDatabase Db
    {
        get
        {
            if (Database != null) return Database;
            var m = GameMeta.Get();
            return m != null ? m.Cosmetics : null;
        }
    }

    /// <summary>꾸미기 입히기 (같은 걸 또 부르면 안 다시 만듦)</summary>
    public void Apply(Appearance a)
    {
        var db = Db;
        if (db == null || db.Slots == null) { Clear(); _applied = null; return; }

        // 칸마다 하나 — 비었으면 칸 기본
        var pick = new List<(SlotDef slot, PartData part)>();
        var key = new System.Text.StringBuilder();
        foreach (var s in db.Slots)
        {
            if (s == null) continue;
            int id = a.Get(s.Key);
            if (id <= 0) id = s.DefaultPart;
            var p = db.Part(id);
            if (p != null && p.Slot != s.Key) p = null;
            pick.Add((s, p));
            key.Append(p != null ? p.Id : 0).Append('.');
        }
        string code = key.ToString();
        if (code == _applied) return;
        _applied = code;
        Clear();
        IndexBones();

        foreach (var (slot, p) in pick)
        {
            if (p == null) continue;
            if (p.Prefab != null) Spawn(p, slot);
            if (p.TargetRenderers != null && p.TargetRenderers.Length > 0 && p.Materials != null && p.Materials.Length > 0)
                SwapMaterials(p);
            Hide(slot.HideRenderers);
            Hide(p.HideRenderers);
        }
    }

    void Spawn(PartData p, SlotDef slot)
    {
        bool rigged = p.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        GameObject go;
        if (rigged)
        {
            // 같은 뼈 이름으로 리깅된 모델 → 캐릭터 뼈를 따라 움직이게
            go = Instantiate(p.Prefab, transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Rebind(smr);
        }
        else
        {
            var bone = FindBone(p, slot);
            go = Instantiate(p.Prefab, bone != null ? bone : transform);
            go.transform.localPosition = p.Position;
            go.transform.localRotation = Quaternion.Euler(p.Rotation);
            go.transform.localScale = p.Scale;
        }
        go.name = $"[꾸미기] {p.Name}";
        Strip(go);
        SetLayer(go.transform, gameObject.layer);
        _spawned.Add(go);
    }

    /// <summary>부위 프리팹의 뼈 → 캐릭터의 같은 이름 뼈 (없는 뼈는 그대로)</summary>
    void Rebind(SkinnedMeshRenderer smr)
    {
        var bones = smr.bones;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null && _bones.TryGetValue(bones[i].name, out var t)) bones[i] = t;
        smr.bones = bones;
        if (smr.rootBone != null && _bones.TryGetValue(smr.rootBone.name, out var root)) smr.rootBone = root;
        smr.updateWhenOffscreen = true;
    }

    /// <summary>스크립트 · 충돌 · 애니메이터 떼기 (모양만)</summary>
    static void Strip(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Kill(mb);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Kill(rb);
        foreach (var an in go.GetComponentsInChildren<Animator>(true)) Kill(an);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    static void Kill(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    void SwapMaterials(PartData p)
    {
        foreach (var r in BaseRenderers())
            foreach (var n in p.TargetRenderers)
            {
                if (string.IsNullOrEmpty(n) || r.name != n) continue;
                var old = r.sharedMaterials;
                var mats = (Material[])old.Clone();
                for (int i = 0; i < mats.Length && i < p.Materials.Length; i++)
                    if (p.Materials[i] != null) mats[i] = p.Materials[i];
                _swapped.Add((r, old));
                r.sharedMaterials = mats;
            }
    }

    void Hide(string[] names)
    {
        if (names == null || names.Length == 0) return;
        foreach (var r in BaseRenderers())
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && r.name == n && r.enabled)
                {
                    r.enabled = false;
                    _hidden.Add(r);
                }
    }

    /// <summary>기본 모델의 렌더러 (붙인 부위 제외)</summary>
    IEnumerable<Renderer> BaseRenderers()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            bool mine = false;
            foreach (var g in _spawned)
                if (g != null && r.transform.IsChildOf(g.transform)) { mine = true; break; }
            if (!mine) yield return r;
        }
    }

    void IndexBones()
    {
        _bones.Clear();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
    }

    public void Clear()
    {
        foreach (var g in _spawned)
            if (g != null)
            {
                g.transform.SetParent(null, false);       // 이번 프레임 안에 다시 찾을 때 안 걸리게
                if (Application.isPlaying) Destroy(g);
                else DestroyImmediate(g);
            }
        _spawned.Clear();
        foreach (var r in _hidden) if (r != null) r.enabled = true;
        _hidden.Clear();
        for (int i = _swapped.Count - 1; i >= 0; i--)
            if (_swapped[i].r != null) _swapped[i].r.sharedMaterials = _swapped[i].mats;
        _swapped.Clear();
    }

    /// <summary>다음 Apply는 같은 꾸미기여도 다시 만듦</summary>
    public void Invalidate() => _applied = null;

    Transform FindBone(PartData p, SlotDef slot)
    {
        string name = !string.IsNullOrEmpty(p.Bone) ? p.Bone : slot.Bone;
        if (!string.IsNullOrEmpty(name) && _bones.TryGetValue(name, out var byName)) return byName;

        var an = GetComponentInChildren<Animator>(true);
        if (an != null && an.isHuman)
        {
            var b = an.GetBoneTransform(slot.HumanBone);
            if (b != null) return b;
        }

        // 사람형이 아니면 이름으로 (예: LeftHand → "lefthand" → 없으면 "hand")
        string want = slot.HumanBone.ToString().ToLowerInvariant();
        string tail = want.Replace("left", "").Replace("right", "").Replace("upper", "").Replace("lower", "");
        foreach (var k in new[] { want, tail })
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Replace("_", "").Replace(" ", "").Contains(k)) return t;
        return null;
    }

    public static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}
