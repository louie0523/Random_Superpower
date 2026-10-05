using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 무기 슬롯에 딸린 데이터 — 인챈트(일반·특수), 처치 수, 진화.
/// 무기를 버리면 픽업에 실려 같이 이동하고, 누가 주워도 따라감.
/// </summary>
public class PlayerEnchant : NetworkBehaviour
{
    public const int MaxEnchants = 100;
    public const int MaxChoices = 3;

    [SerializeField] private EnchantDatabase database;

    [Tooltip("등급·단계별 스톤 비용, 한계 해제, 특수 등급 확률. 비우면 무기의 옛 비용 필드 사용")]
    [SerializeField] private EnchantRules rules;

    // 슬롯 s의 인챈트 i → [s * MaxEnchants + i]. 앞에서부터 채워짐. 값 규칙은 EnchantCode
    [Networked, Capacity(PlayerEquipment.WeaponSlots * MaxEnchants)]
    public NetworkArray<int> SlotEnchants { get; }

    [Networked, Capacity(PlayerEquipment.WeaponSlots)]
    public NetworkArray<int> EnchantedWeaponIds { get; }

    [Networked, Capacity(PlayerEquipment.WeaponSlots)]
    public NetworkArray<int> SlotKills { get; }

    [Networked] public int Stones { get; set; }

    /// <summary>최근 인챈트 결과(EnchantCode). HUD 연출용</summary>
    [Networked] public int LastRolled { get; set; }
    [Networked] public int LastRollTick { get; set; }

    [Networked] public NetworkBool IsGunsmith { get; set; }
    [Networked] public NetworkBool UnlimitedStage { get; set; }

    // ── 특수 단계 선택 대기 ──
    // 비용은 선택지를 뽑을 때 이미 냄. 창을 닫아도 같은 선택지가 유지됨 (다시 뽑기 방지)
    [Networked, Capacity(MaxChoices)] public NetworkArray<int> PendingSpecial { get; }
    [Networked] private int PendingSlotPlus1 { get; set; }      // 0 = 대기 없음
    [Networked] private int PendingWeaponId { get; set; }

    private PlayerEquipment _equip;
    private PlayerStats _stats;

    public EnchantDatabase Database => database;
    public EnchantRules Rules => rules;

    int SpecialInterval => rules != null ? rules.UnlockedSpecialInterval : 0;

    static readonly List<EnchantData> _pickNormals = new();

    public override void Spawned()
    {
        _equip = GetComponent<PlayerEquipment>();
        _stats = GetComponent<PlayerStats>();

        ApplyAll();
    }

    // ══════════════════ 조회 ══════════════════

    public int GetEnchantId(int slot, int index)
    {
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return 0;
        if (index < 0 || index >= MaxEnchants) return 0;
        return SlotEnchants[slot * MaxEnchants + index];
    }

    /// <summary>현재 강화 단계 (= 인챈트 개수)</summary>
    public int GetStage(int slot)
    {
        int n = 0;
        for (int i = 0; i < MaxEnchants; i++)
        {
            if (GetEnchantId(slot, i) == 0) break;
            n++;
        }
        return n;
    }

    public int ActiveStage => _equip != null ? GetStage(_equip.ActiveWeaponSlot) : 0;

    public int GetKills(int slot) =>
        (slot >= 0 && slot < PlayerEquipment.WeaponSlots) ? SlotKills[slot] : 0;

    public int GetMaxStage(EquipmentData w)
    {
        if (w == null || !w.CanEnchant) return 0;

        int max = UnlimitedStage
            ? (rules != null ? rules.UnlockedMaxStage : MaxEnchants)
            : w.MaxEnchantStage;

        return Mathf.Clamp(max, 0, MaxEnchants);
    }

    public int GetCost(EquipmentData w, int currentStage) =>
        rules != null ? rules.GetCost(w.Grade, currentStage + 1) : w.GetEnchantCost(currentStage);

    /// <summary>이 슬롯 무기에 붙은 특수 인챈트의 등급 (없으면 0)</summary>
    public int OwnedTier(int slot, int specialId)
    {
        int best = 0;
        for (int i = 0; i < MaxEnchants; i++)
        {
            int code = GetEnchantId(slot, i);
            if (code == 0) break;
            if (EnchantCode.IsSpecial(code) && EnchantCode.SpecialId(code) == specialId)
                best = Mathf.Max(best, EnchantCode.Tier(code));
        }
        return best;
    }

    /// <summary>다음 단계가 특수 단계이고, 아직 올릴 수 있는 후보가 하나라도 있는가</summary>
    public bool NextIsSpecial(int slot)
    {
        var t = WeaponAt(slot)?.EnchantTable;
        if (t == null) return false;
        if (!t.IsSpecialStage(GetStage(slot) + 1, UnlimitedStage, SpecialInterval)) return false;

        int next = GetStage(slot) + 1;
        foreach (var c in t.SpecialCandidates)
            if (c?.Special != null && c.Weight > 0f && c.Special.MaxTier > 0
                && c.AvailableAt(next)                                           // ★
                && OwnedTier(slot, c.Special.Id) < c.Special.MaxTier)
                return true;

        return false;   // 전부 최고 등급 → 일반 인챈트로
    }

    public bool HasPending(int slot) => PendingSlotPlus1 == slot + 1;
    public int GetPending(int i) => i >= 0 && i < MaxChoices ? PendingSpecial[i] : 0;

    public int[] ExportSlot(int slot)
    {
        var arr = new int[MaxEnchants];
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return arr;

        for (int i = 0; i < MaxEnchants; i++)
            arr[i] = GetEnchantId(slot, i);

        return arr;
    }

    EquipmentData WeaponAt(int slot) =>
        _equip != null && _equip.Database != null ? _equip.Database.Get(_equip.GetWeaponIdAt(slot)) : null;

    // ══════════════════ 인챈트 ══════════════════

    public bool CanEnchant(out string reason, out int cost)
    {
        reason = "";
        cost = 0;

        var w = _equip?.GetWeapon();
        if (w == null) { reason = "ui.enchant.reason.no_weapon"; return false; }

        if (w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun && !IsGunsmith)
        {
            reason = "ui.enchant.reason.gun_locked";
            return false;
        }

        if (!w.CanEnchant) { reason = "ui.enchant.reason.cannot"; return false; }

        int slot = _equip.ActiveWeaponSlot;
        if (HasPending(slot)) { reason = "ui.enchant.reason.choose"; return false; }

        int stage = GetStage(slot);
        if (stage >= GetMaxStage(w)) { reason = "ui.enchant.reason.max_stage"; return false; }

        cost = GetCost(w, stage);
        if (Stones < cost) { reason = "ui.enchant.reason.no_stone"; return false; }

        return true;
    }

    /// <summary>
    /// 인챈트 실행.
    ///   일반 단계 → 바로 일반 인챈트
    ///   특수 단계 → 선택지를 뽑아 대기 (RPC_ChooseSpecial / RPC_ChooseNormal로 확정)
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Enchant()
    {
        if (!CanEnchant(out _, out int cost)) return;

        var w = _equip.GetWeapon();
        int slot = _equip.ActiveWeaponSlot;
        int stage = GetStage(slot);

        if (NextIsSpecial(slot))
        {
            int n = RollSpecialOptions(slot, w.EnchantTable);
            if (n > 0)
            {
                Stones -= cost;
                for (int i = 0; i < MaxChoices; i++)
                    PendingSpecial.Set(i, i < n ? _options[i] : 0);
                PendingSlotPlus1 = slot + 1;
                PendingWeaponId = w.Id;
                return;
            }
        }

        var rolled = w.EnchantTable.Roll(stage + 1);
        if (rolled == null) return;

        Stones -= cost;
        Append(slot, w.Id, rolled.Id);
    }

    /// <summary>특수 선택지 중 하나로 확정</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ChooseSpecial(int index)
    {
        if (!ValidPending(out int slot)) return;
        if (index < 0 || index >= MaxChoices) return;

        int code = PendingSpecial[index];
        if (code == 0) return;

        int weaponId = PendingWeaponId;
        ClearPending();

        if (GetStage(slot) < MaxEnchants) Append(slot, weaponId, code);
    }

    /// <summary>특수 대신 일반 인챈트로 (비용은 이미 냄)</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ChooseNormal()
    {
        if (!ValidPending(out int slot)) return;

        var w = WeaponAt(slot);
        int weaponId = PendingWeaponId;
        ClearPending();

        var rolled = w?.EnchantTable?.Roll(GetStage(slot) + 1);
        if (rolled != null) Append(slot, weaponId, rolled.Id);
    }

    bool ValidPending(out int slot)
    {
        slot = PendingSlotPlus1 - 1;
        if (slot < 0) return false;

        // 그 사이 무기가 바뀌었으면 취소
        if (_equip.GetWeaponIdAt(slot) != PendingWeaponId)
        {
            ClearPending();
            return false;
        }
        return true;
    }

    void ClearPending()
    {
        for (int i = 0; i < MaxChoices; i++) PendingSpecial.Set(i, 0);
        PendingSlotPlus1 = 0;
        PendingWeaponId = 0;
    }

    void Append(int slot, int weaponId, int code)
    {
        int stage = GetStage(slot);
        if (stage >= MaxEnchants) return;

        SlotEnchants.Set(slot * MaxEnchants + stage, code);
        EnchantedWeaponIds.Set(slot, weaponId);
        LastRolled = code;
        LastRollTick = Runner.Tick;

        ApplyAll();
    }

    // ── 특수 선택지 추첨 ──

    static readonly List<(int code, float w)> _elig = new();
    readonly int[] _options = new int[MaxChoices];

    /// <summary>
    /// 아직 최고 등급이 아닌 후보만. 이미 가진 종류는 다음 등급(업그레이드),
    /// 새 종류는 공통 확률표로 등급 추첨. 가중치로 겹치지 않게 최대 N개.
    /// </summary>
    int RollSpecialOptions(int slot, EnchantTable t)
    {
        _elig.Clear();

        foreach (var c in t.SpecialCandidates)
        {
            var s = c?.Special;
            if (s == null || c.Weight <= 0f || s.MaxTier == 0) continue;
            int next = GetStage(slot) + 1;
            if (!c.AvailableAt(next)) continue;                                      // ★

            int owned = OwnedTier(slot, s.Id);
            if (owned >= s.MaxTier) continue;

            int tier = owned > 0 ? owned + 1 : (rules != null ? rules.RollTier(s.MaxTier) : 1);
            _elig.Add((EnchantCode.Make(s.Id, tier), c.Weight));
        }

        int want = Mathf.Clamp(t.SpecialChoiceCount, 1, MaxChoices);
        int n = 0;

        while (n < want && _elig.Count > 0)
        {
            float total = 0f;
            foreach (var (_, w) in _elig) total += w;

            float r = Random.value * total;
            int pick = _elig.Count - 1;
            for (int i = 0; i < _elig.Count; i++)
            {
                r -= _elig[i].w;
                if (r <= 0f) { pick = i; break; }
            }

            _options[n++] = _elig[pick].code;
            _elig.RemoveAt(pick);
        }

        return n;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddStones(int amount)
    {
        Stones = Mathf.Max(0, Stones + amount);
    }

    // ══════════════════ 타격 시 특수 효과 ══════════════════

    static readonly Dictionary<int, int> _effective = new();   // 특수ID → 등급

    /// <summary>들고 있는 무기의 특수 인챈트를 종류별 최고 등급으로 모음</summary>
    bool CollectEffective()
    {
        _effective.Clear();
        if (_equip == null || database == null) return false;

        int slot = _equip.ActiveWeaponSlot;
        int wid = _equip.GetWeaponIdAt(slot);
        if (wid == 0 || EnchantedWeaponIds[slot] != wid) return false;

        for (int i = 0; i < MaxEnchants; i++)
        {
            int code = GetEnchantId(slot, i);
            if (code == 0) break;
            if (!EnchantCode.IsSpecial(code)) continue;

            int id = EnchantCode.SpecialId(code);
            int tier = EnchantCode.Tier(code);
            if (!_effective.TryGetValue(id, out int cur) || tier > cur) _effective[id] = tier;
        }
        return _effective.Count > 0;
    }

    /// <summary>
    /// 타격 전 — 관통·처형을 피해 정보에 실음. 실제 계산은 맞는 쪽에서.
    /// PlayerAttack이 RPC_TakeDamage 직전에 호출
    /// </summary>
    public void ModifyWeaponHit(ref DamageInfo info)
    {
        if (!HasStateAuthority || !CollectEffective()) return;

        foreach (var kv in _effective)
        {
            var tier = database.GetSpecial(kv.Key)?.GetTier(kv.Value);
            if (tier?.Effects == null) continue;

            foreach (var e in tier.Effects)
            {
                if (e == null || Random.value > e.GetChance()) continue;

                switch (e.Kind)
                {
                    case SpecialEffectKind.DefensePen:
                        info.DefensePen = Mathf.Clamp01(info.DefensePen + e.Value * 0.01f);
                        break;

                    case SpecialEffectKind.Execute:
                        info.ExecuteBelow = Mathf.Max(info.ExecuteBelow, e.Value * 0.01f);
                        info.ExecuteBonus = Mathf.Max(info.ExecuteBonus, e.Value2 * 0.01f);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 타격 후 — 상태이상, 추가 피해, 흡혈.
    /// 순서: ① 탐식(흡혈 추가) → ② 나머지 → ③ 혈격(흡혈을 피해로) — 혈격이 늘어난 흡혈량까지 바꾸도록
    /// </summary>
    /// <param name="dealt">이번 타격 피해 (탐식 흡혈 계산용)</param>
    /// <param name="lifestealHeal">흡혈로 회복할 양. 혈격이면 0이 되고 피해로 바뀜</param>
    public void OnWeaponHit(PlayerHealth target, float dealt, ref int lifestealHeal)
    {
        if (!HasStateAuthority || target == null || !CollectEffective()) return;

        var attacker = Object.InputAuthority;

        for (int pass = 0; pass < 3; pass++)
            foreach (var kv in _effective)
            {
                var tier = database.GetSpecial(kv.Key)?.GetTier(kv.Value);
                if (tier?.Effects == null) continue;

                foreach (var e in tier.Effects)
                {
                    if (e == null || PassOf(e.Kind) != pass) continue;
                    if (Random.value > e.GetChance()) continue;
                    ApplySpecialEffect(e, target, attacker, dealt, ref lifestealHeal);
                }
            }
    }

    static int PassOf(SpecialEffectKind k) => k switch
    {
        SpecialEffectKind.Lifesteal => 0,
        SpecialEffectKind.LifestealToDamage => 2,
        SpecialEffectKind.DefensePen or SpecialEffectKind.Execute => -1,   // 타격 전에 처리됨
        _ => 1,
    };

    void ApplySpecialEffect(SpecialEffect e, PlayerHealth target, PlayerRef attacker, float dealt, ref int heal)
    {
        switch (e.Kind)
        {
            case SpecialEffectKind.ApplyStatus:
                if (target.TryGetComponent<PlayerStatus>(out var st))
                    st.Apply(e.Status, e.Value, e.Duration, attacker, e.GetMaxStacks());
                break;

            case SpecialEffectKind.MaxHpDamage:
                {
                    var info = DamageInfo.FromPlayerHit(0, attacker, transform.position);
                    info.MaxHpPercent = e.Value;
                    info.IgnoreDefense = e.IgnoreDefense;
                    info.IsDot = true;
                    target.RPC_TakeDamage(info);
                    break;
                }

            case SpecialEffectKind.Lifesteal:
                heal += Mathf.RoundToInt(dealt * e.Value * 0.01f);
                break;

            case SpecialEffectKind.LifestealToDamage:
                {
                    int dmg = Mathf.RoundToInt(heal * e.Value * 0.01f);
                    heal = 0;

                    if (dmg > 0)
                    {
                        var info = DamageInfo.FromPlayerHit(dmg, attacker, transform.position);
                        info.IgnoreDefense = e.IgnoreDefense;
                        info.IsDot = true;
                        target.RPC_TakeDamage(info);
                    }
                    break;
                }
        }
    }


    // ══════════════════ 처치 수 ══════════════════

    public void AddKillToActive()
    {
        if (!HasStateAuthority || _equip == null) return;

        int slot = _equip.ActiveWeaponSlot;
        if (_equip.GetWeaponIdAt(slot) == 0) return;

        SlotKills.Set(slot, SlotKills[slot] + 1);
    }

    // ══════════════════ 진화 ══════════════════

    public bool IsUpgradeReady(int slot, int path)
    {
        var w = WeaponAt(slot);
        if (w?.Upgrades == null || path < 0 || path >= w.Upgrades.Length) return false;

        var u = w.Upgrades[path];
        if (u == null || u.TargetId == 0) return false;
        if (_equip.Database.Get(u.TargetId) == null) return false;

        if (u.RequiredKills > 0 && GetKills(slot) < u.RequiredKills) return false;
        if (u.RequiredStage > 0 && GetStage(slot) < u.RequiredStage) return false;

        return true;
    }

    public bool AnyUpgradeReady(int slot)
    {
        var w = WeaponAt(slot);
        if (w?.Upgrades == null) return false;

        for (int i = 0; i < w.Upgrades.Length; i++)
            if (IsUpgradeReady(slot, i)) return true;
        return false;
    }

    public void RequestUpgrade(int slot, int path)
    {
        if (IsUpgradeReady(slot, path)) RPC_Upgrade(slot, path);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Upgrade(int slot, int path)
    {
        if (!IsUpgradeReady(slot, path)) return;

        var u = WeaponAt(slot).Upgrades[path];
        int[] carried = u.InheritEnchant ? ExportSlot(slot) : null;

        _equip.ReplaceWeaponAt(slot, u.TargetId);

        if (carried != null) ImportSlot(slot, u.TargetId, carried, 0);
    }

    // ══════════════════ 무기 이동 ══════════════════

    public void ImportSlot(int slot, int weaponId, int[] enchants, int kills = 0)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return;

        for (int i = 0; i < MaxEnchants; i++)
        {
            int id = (enchants != null && i < enchants.Length) ? enchants[i] : 0;
            SlotEnchants.Set(slot * MaxEnchants + i, id);
        }

        EnchantedWeaponIds.Set(slot, weaponId);
        SlotKills.Set(slot, Mathf.Max(0, kills));
        ApplyAll();
    }

    public void ClearSlot(int slot)
    {
        if (!HasStateAuthority) return;
        if (slot < 0 || slot >= PlayerEquipment.WeaponSlots) return;

        for (int i = 0; i < MaxEnchants; i++)
            SlotEnchants.Set(slot * MaxEnchants + i, 0);

        EnchantedWeaponIds.Set(slot, 0);
        SlotKills.Set(slot, 0);
        if (HasPending(slot)) ClearPending();
        ApplyAll();
    }

    public void OnWeaponChanged(int slot, int newWeaponId)
    {
        if (!HasStateAuthority) return;
        ClearSlot(slot);
    }

    // ══════════════════ 능력 연동 ══════════════════

    public void SetGunEnchant(bool on)
    {
        if (!HasStateAuthority) return;
        IsGunsmith = on;
    }

    public void SetGunsmith(bool on) => SetGunEnchant(on);

    public void SetUnlimitedStage(bool on)
    {
        if (!HasStateAuthority) return;
        UnlimitedStage = on;
    }

    // ══════════════════ 적용 ══════════════════

    /// <summary>일반 인챈트 스탯만. 특수 인챈트는 타격 시 OnWeaponHit에서</summary>
    public void ApplyAll()
    {
        if (!HasStateAuthority) return;
        if (_stats == null || _equip == null || database == null) return;

        _stats.RemoveBySource(SourceId.EnchantBase);

        int slot = _equip.ActiveWeaponSlot;
        int wid = _equip.GetWeaponIdAt(slot);
        if (wid == 0 || EnchantedWeaponIds[slot] != wid) return;

        for (int i = 0; i < MaxEnchants; i++)
        {
            int id = GetEnchantId(slot, i);
            if (id == 0) break;

            var e = database.Get(id);          // 특수 값이면 null
            if (e == null || e.Stats == null) continue;

            foreach (var s in e.Stats)
                _stats.AddModifier(new StatModifier(s.Stat, s.Type, s.Value, SourceId.EnchantBase));
        }
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        for (int s = 0; s < PlayerEquipment.WeaponSlots; s++) ClearSlot(s);
        ClearPending();
        Stones = 0;
        LastRolled = 0;
        LastRollTick = 0;
        IsGunsmith = false;
        UnlimitedStage = false;
    }

    /// <summary>대장장이 지정 소울 비용. 지금 단계에 지정할 수 없는 값이면 -1</summary>
    public int GetPickSoulCost(int slot, int code)
    {
        var t = WeaponAt(slot)?.EnchantTable;
        if (t == null || code == 0) return -1;

        if (EnchantCode.IsSpecial(code))
        {
            if (!NextIsSpecial(slot)) return -1;

            int id = EnchantCode.SpecialId(code);
            int tier = EnchantCode.Tier(code);

            SpecialEnchantData s = null;
            int next = GetStage(slot) + 1;
            foreach (var c in t.SpecialCandidates)
                if (c?.Special != null && c.Weight > 0f && c.Special.Id == id
                    && c.AvailableAt(next))                                          // ★
                { s = c.Special; break; }

            if (s == null) return -1;
            if (tier <= OwnedTier(slot, id) || tier > s.MaxTier) return -1;

            return rules != null ? rules.GetPickSpecialSoul(tier) : 0;
        }

        t.GetNormalCandidates(GetStage(slot) + 1, _pickNormals);
        foreach (var d in _pickNormals)
            if (d.Id == code) return rules != null ? rules.PickNormalSoul : 0;

        return -1;
    }

    /// <summary>대장장이 — 다음 인챈트를 직접 지정. 스톤 비용 + 소울</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_PickEnchant(int code)
    {
        if (!UnlimitedStage) return;
        if (!CanEnchant(out _, out int stoneCost)) return;     // 스톤·최대 단계·선택 대기 확인

        int slot = _equip.ActiveWeaponSlot;
        int soulCost = GetPickSoulCost(slot, code);
        if (soulCost < 0) return;

        var hp = GetComponent<PlayerHealth>();
        if (hp == null || hp.Soul < soulCost) return;

        Stones -= stoneCost;
        hp.SpendSoul(soulCost);
        Append(slot, _equip.GetWeapon().Id, code);
    }
}