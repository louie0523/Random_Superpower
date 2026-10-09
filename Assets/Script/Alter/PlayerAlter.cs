using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 변화(파츠) — 네트워크 배열에 ID를 들고, 모든 클라이언트가 같은 파츠를 붙임.
/// 스탯은 SA(소유자)에서만. 어디서 불러도 됨 — SA가 아니면 RPC로 보냄.
/// </summary>
public class PlayerAlter : NetworkBehaviour
{
    public const int MaxAlters = 8;

    [SerializeField] private AlterDatabase database;
    [Tooltip("비우면 자식에서 찾음")]
    [SerializeField] private FPArmRig armRig;

    [Networked, Capacity(MaxAlters), OnChangedRender(nameof(OnAltersChanged))]
    public NetworkArray<int> Alters { get; }

    private readonly List<GameObject> _spawned = new();
    private readonly List<Renderer> _renderers = new();
    private PlayerStats _stats;
    private PlayerHealth _health;
    private Transform _weaponParent;
    private bool _wasDead;

    // 탈것 높이 — 피격 판정(CharacterController)을 늘려서 올라탄 몸까지 덮음
    private CharacterController _cc;
    private FPSCamera _cam;
    private float _ccHeight, _ccCenterY;
    private float _lift;

    public AlterDatabase Database => database;

    /// <summary>지금 올라타 있는 높이 (m)</summary>
    public float Lift => _lift;

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _cc = GetComponent<CharacterController>();
        _cam = GetComponentInChildren<FPSCamera>(true);
        if (armRig == null) armRig = GetComponentInChildren<FPArmRig>(true);

        if (_cc != null)
        {
            _ccHeight = _cc.height;
            _ccCenterY = _cc.center.y;
        }

        Rebuild();
        if (HasStateAuthority) ApplyStats();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearVisuals();
        ApplyLift(0f);
    }

    /// <summary>모든 클라이언트 — 몸·카메라를 올리고 피격 판정을 위로 늘림 (발 위치는 그대로)</summary>
    void ApplyLift(float lift)
    {
        if (Mathf.Approximately(lift, _lift)) return;
        _lift = lift;

        if (_cc != null)
        {
            float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
            float add = lift / scaleY;
            _cc.height = _ccHeight + add;
            var c = _cc.center;
            c.y = _ccCenterY + add * 0.5f;
            _cc.center = c;
        }

        if (armRig != null) armRig.BodyLift = lift;
        if (HasInputAuthority && _cam != null) _cam.SetHeightOffset(lift);
    }

    // ══════════════════ 조회 ══════════════════

    public int Count(int id)
    {
        int n = 0;
        for (int i = 0; i < MaxAlters; i++) if (Alters[i] == id) n++;
        return n;
    }

    public bool Has(int id) => id != 0 && Count(id) > 0;
    public bool Has(AlterData a) => a != null && Has(a.Id);

    // ══════════════════ 붙이기 · 떼기 ══════════════════

    public void Add(AlterData a) { if (a != null) Add(a.Id); }
    public void Remove(AlterData a, bool all = false) { if (a != null) Remove(a.Id, all); }

    public void Add(int id)
    {
        if (HasStateAuthority) AddLocal(id);
        else RPC_Add(id);
    }

    public void Remove(int id, bool all = false)
    {
        if (HasStateAuthority) RemoveLocal(id, all);
        else RPC_Remove(id, all);
    }

    public void ClearAll()
    {
        if (!HasStateAuthority) return;
        for (int i = 0; i < MaxAlters; i++) Alters.Set(i, 0);
        ApplyStats();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Add(int id) => AddLocal(id);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Remove(int id, NetworkBool all) => RemoveLocal(id, all);

    void AddLocal(int id)
    {
        var a = database != null ? database.Get(id) : null;
        if (a == null) return;
        if (_health != null && _health.IsDead) return;

        // 같은 그룹은 교체
        if (!string.IsNullOrEmpty(a.Group))
            for (int i = 0; i < MaxAlters; i++)
            {
                var other = database.Get(Alters[i]);
                if (other != null && other.Id != a.Id && other.Group == a.Group) Alters.Set(i, 0);
            }

        if (Count(id) >= a.MaxStack) return;

        for (int i = 0; i < MaxAlters; i++)
            if (Alters[i] == 0)
            {
                Alters.Set(i, id);
                ApplyStats();
                return;
            }

        Debug.LogWarning($"[PlayerAlter] 변화 칸이 가득 찼습니다 ({MaxAlters}). {a.name} 무시", this);
    }

    void RemoveLocal(int id, bool all)
    {
        bool changed = false;
        for (int i = MaxAlters - 1; i >= 0; i--)
            if (Alters[i] == id)
            {
                Alters.Set(i, 0);
                changed = true;
                if (!all) break;
            }

        if (changed) ApplyStats();
    }

    public void ResetForNewRound() => ClearAll();

    // ══════════════════ 스탯 (SA) ══════════════════

    void ApplyStats()
    {
        if (!HasStateAuthority || _stats == null) return;

        float maxBefore = _stats.Get(StatType.MaxHP);

        for (int i = 0; i < MaxAlters; i++)
            _stats.RemoveBySource(SourceId.AlterBase + i);

        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database != null ? database.Get(Alters[i]) : null;
            if (a?.Stats == null) continue;

            foreach (var s in a.Stats)
                if (s != null) _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, SourceId.AlterBase + i));
        }

        // 변화로 최대 체력이 오르면 현재 체력도 (상점·인챈트와 같은 규칙)
        _health?.ApplyMaxHpGain(maxBefore);
    }

    // ══════════════════ 파츠 (모든 클라이언트) ══════════════════

    void OnAltersChanged() => Rebuild();

    public override void Render()
    {
        // 무기를 바꾸면 무기 파츠를 새 무기로 옮김
        Transform tip = armRig != null ? armRig.WeaponTip : null;
        if (tip != _weaponParent && HasWeaponAlter()) Rebuild();

        // 사망·부활 시 보이기 다시 계산 (PlayerHealth가 렌더러를 끄고 켜는 것과 맞춤)
        bool dead = _health != null && _health.IsDead;
        if (dead != _wasDead) ApplyVisibility();
    }

    bool HasWeaponAlter()
    {
        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database != null ? database.Get(Alters[i]) : null;
            if (a != null && a.Attach == AlterAttach.Weapon && a.Prefab != null) return true;
        }
        return false;
    }

    void Rebuild()
    {
        ClearVisuals();
        if (database == null) return;

        _weaponParent = armRig != null ? armRig.WeaponTip : null;
        var stackIndex = new Dictionary<int, int>();

        float lift = 0f;
        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database.Get(Alters[i]);
            if (a != null) lift = Mathf.Max(lift, a.Lift);
        }
        ApplyLift(lift);

        for (int i = 0; i < MaxAlters; i++)
        {
            var a = database.Get(Alters[i]);
            if (a == null || a.Prefab == null) continue;

            stackIndex.TryGetValue(a.Id, out int k);
            stackIndex[a.Id] = k + 1;
            if (k > 0 && a.StackOffset == Vector3.zero) continue;      // 스탯만 쌓이는 변화

            Transform parent = Resolve(a);
            if (parent == null) continue;

            var go = Instantiate(a.Prefab, parent);
            go.name = $"Alter_{a.name}{(k > 0 ? $"_{k + 1}" : "")}";
            go.transform.localPosition = a.Position + a.StackOffset * k;
            go.transform.localRotation = Quaternion.Euler(a.Rotation);
            go.transform.localScale = a.Scale;

            // 파츠는 장식 — 충돌·물리가 판정을 막지 않게
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

            _spawned.Add(go);
            ConfigureSelfView(go, a);
        }

        ApplyVisibility();
    }

    Transform Resolve(AlterData a)
    {
        switch (a.Attach)
        {
            case AlterAttach.Body: return armRig != null && armRig.Body != null ? armRig.Body : transform;
            case AlterAttach.Head: return armRig != null && armRig.Head != null ? armRig.Head : (armRig != null && armRig.Body != null ? armRig.Body : transform);
            case AlterAttach.RightHand: return armRig != null ? armRig.RightHand : null;
            case AlterAttach.LeftHand: return armRig != null ? armRig.LeftHand : null;
            case AlterAttach.Weapon: return armRig != null ? armRig.WeaponTip : null;
            case AlterAttach.Root: return transform;
            case AlterAttach.Bone: return string.IsNullOrEmpty(a.BoneName) ? null : FindDeep(transform, a.BoneName);
        }
        return transform;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>본인 화면에서 시야를 가리는 파츠는 숨김 (그림자만 남길 수 있음)</summary>
    void ConfigureSelfView(GameObject go, AlterData a)
    {
        bool self = HasInputAuthority;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (self && !a.ShowForSelf)
            {
                if (a.SelfShadow) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else { r.enabled = false; continue; }
            }
            _renderers.Add(r);
        }
    }

    void ApplyVisibility()
    {
        _wasDead = _health != null && _health.IsDead;
        foreach (var r in _renderers)
            if (r != null) r.enabled = !_wasDead;
    }

    void ClearVisuals()
    {
        foreach (var go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();
        _renderers.Clear();
    }
}