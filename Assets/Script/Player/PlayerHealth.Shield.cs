using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 쉴드 3종 — 체력보다 먼저 깎임 (소모품 → 재생 → 소울 순서). 방어력 · 받는 피해를 다 계산한 뒤의 피해를 막음.
///   재생 쉴드  — 최대치 = 스탯 Shield (방어구 · 특수 인챈트 · 상점 · 고유 특성). 피해를 안 받고 regenDelay초가 지나면 빠르게 다시 참
///   소울 쉴드  — 최대치 = 스탯 SoulShield. 저절로 안 참 — '소울 쉴드 충전' 키(기본 C)를 누르고 있으면 소울을 써서 채움 (맞으면 끊김)
///   소모품 쉴드 — 스킬 · 상점 소모품 · 특성이 주는 임시 쉴드. AddTempShield(양, 초) — 시간이 지나면 사라짐 (0초 = 깨질 때까지)
/// 죽으면 전부 0, 부활하면 재생 쉴드만 가득. 변신 중에도 똑같이 (스탯이라서)
/// </summary>
public partial class PlayerHealth
{
    [Header("쉴드 — 재생")]
    [Tooltip("마지막으로 피해를 받은 뒤 이 시간(초)이 지나면 재생 쉴드가 차기 시작")]
    [SerializeField] private float shieldRegenDelay = 3f;
    [Tooltip("0에서 가득까지 걸리는 시간 (초). 0 = 바로 가득")]
    [SerializeField] private float shieldRegenFullTime = 1.5f;

    [Header("쉴드 — 소울 충전")]
    [Tooltip("소울 1로 채우는 쉴드 양")]
    [SerializeField] private float soulShieldPerSoul = 4f;
    [Tooltip("누르고 있을 때 초당 채우는 양")]
    [SerializeField] private float soulShieldChargeRate = 40f;
    [Tooltip("맞은 뒤 이 시간(초) 동안은 충전 안 됨")]
    [SerializeField] private float soulShieldHitLock = 1f;

    [Header("쉴드 — 소모품")]
    [Tooltip("소모품 쉴드 합계 최대 = 최대 체력 × 이 값")]
    [SerializeField] private float tempShieldCapOfMaxHp = 1f;

    [Networked] public int ShieldRegen { get; set; }
    [Networked] public int ShieldRegenMax { get; set; }
    [Networked] public int ShieldSoul { get; set; }
    [Networked] public int ShieldSoulMax { get; set; }
    [Networked] public int ShieldTemp { get; set; }
    /// <summary>소울 쉴드를 충전하는 중 (연출 · UI)</summary>
    [Networked] public NetworkBool ShieldCharging { get; set; }

    public int ShieldTotal => ShieldRegen + ShieldSoul + ShieldTemp;

    /// <summary>쉴드가 피해를 막았을 때 (SA) — (막은 양)</summary>
    public event System.Action<int> ShieldAbsorbed;
    /// <summary>쉴드가 전부 깨졌을 때 (SA)</summary>
    public event System.Action ShieldBroken;

    struct TempShield { public int Amount; public float Expire; public int Source; }
    private readonly List<TempShield> _temp = new();
    private float _lastHurt = -999f;
    private float _regenFrac, _soulFrac2, _soulCost;

    // ══════════════════ 주기 (SA) ══════════════════

    void TickShield()
    {
        float now = Runner.SimulationTime;
        float dt = Runner.DeltaTime;

        if (IsDead)
        {
            if (ShieldTotal > 0) ClearShields();
            ShieldCharging = false;
            return;
        }

        // 최대치 — 스탯 (방어구 · 인챈트 · 상점 · 특성)
        int rMax = Mathf.Max(0, Mathf.RoundToInt(_stats.Get(StatType.Shield)));
        int sMax = Mathf.Max(0, Mathf.RoundToInt(_stats.Get(StatType.SoulShield)));
        if (ShieldRegenMax != rMax) ShieldRegenMax = rMax;
        if (ShieldSoulMax != sMax) ShieldSoulMax = sMax;
        if (ShieldRegen > rMax) ShieldRegen = rMax;
        if (ShieldSoul > sMax) ShieldSoul = sMax;

        // 재생 — 안 맞은 지 regenDelay초 지나면
        if (ShieldRegen < rMax && now - _lastHurt >= shieldRegenDelay)
        {
            if (shieldRegenFullTime <= 0f) ShieldRegen = rMax;
            else
            {
                _regenFrac += rMax / shieldRegenFullTime * dt;
                int add = Mathf.FloorToInt(_regenFrac);
                _regenFrac -= add;
                if (add > 0) ShieldRegen = Mathf.Min(rMax, ShieldRegen + add);
            }
        }
        else _regenFrac = 0f;

        // 소모품 — 시간 끝난 것 지우기
        bool changed = false;
        for (int i = _temp.Count - 1; i >= 0; i--)
            if (_temp[i].Amount <= 0 || (_temp[i].Expire > 0f && now >= _temp[i].Expire)) { _temp.RemoveAt(i); changed = true; }
        if (changed) SyncTemp();

        TickSoulCharge(now, dt, sMax);
    }

    /// <summary>소울 쉴드 충전 — 키를 누르고 있는 동안 (내 캐릭터만)</summary>
    void TickSoulCharge(float now, float dt, int sMax)
    {
        bool want = HasInputAuthority && !UIManager.InputBlocked && KeyBinds.Held(GameAction.ShieldCharge)
                    && sMax > 0 && ShieldSoul < sMax && Soul > 0
                    && now - _lastHurt >= soulShieldHitLock
                    && (_status == null || !_status.IsStunned);

        if (ShieldCharging != want) ShieldCharging = want;
        if (!want) { _soulFrac2 = 0f; return; }

        _soulFrac2 += soulShieldChargeRate * dt;
        int add = Mathf.Min(Mathf.FloorToInt(_soulFrac2), sMax - ShieldSoul);
        if (add <= 0) return;
        _soulFrac2 -= add;

        // 소울 값 — 모아 뒀다가 1이 되면 뺌
        _soulCost += add / Mathf.Max(0.01f, soulShieldPerSoul);
        int pay = Mathf.FloorToInt(_soulCost);
        if (pay > Soul) { add = Mathf.FloorToInt(Soul * soulShieldPerSoul); pay = Soul; _soulCost = 0f; }
        else _soulCost -= pay;

        if (pay > 0) Soul -= pay;
        ShieldSoul = Mathf.Min(sMax, ShieldSoul + add);
    }

    // ══════════════════ 피해 막기 (RPC_TakeDamage에서) ══════════════════

    /// <summary>최종 피해에서 쉴드만큼 빼고 막은 양을 돌려줌 — 소모품 → 재생 → 소울</summary>
    int AbsorbShield(ref int dmg, in DamageInfo info)
    {
        if (dmg > 0) _lastHurt = Runner.SimulationTime;
        if (dmg <= 0 || info.BypassShield || ShieldTotal <= 0) return 0;

        int before = dmg;

        // 소모품 — 먼저 사라질 것부터
        if (ShieldTemp > 0 && _temp.Count > 0)
        {
            _temp.Sort((a, b) => Key(a).CompareTo(Key(b)));
            for (int i = 0; i < _temp.Count && dmg > 0; i++)
            {
                var t = _temp[i];
                int take = Mathf.Min(t.Amount, dmg);
                t.Amount -= take;
                dmg -= take;
                _temp[i] = t;
            }
            _temp.RemoveAll(t => t.Amount <= 0);
            SyncTemp();
        }

        if (dmg > 0 && ShieldRegen > 0)
        {
            int take = Mathf.Min(ShieldRegen, dmg);
            ShieldRegen -= take;
            dmg -= take;
        }

        if (dmg > 0 && ShieldSoul > 0)
        {
            int take = Mathf.Min(ShieldSoul, dmg);
            ShieldSoul -= take;
            dmg -= take;
        }

        int absorbed = before - dmg;
        if (absorbed > 0) ShieldAbsorbed?.Invoke(absorbed);
        if (absorbed > 0 && ShieldTotal <= 0) ShieldBroken?.Invoke();
        return absorbed;

        static float Key(TempShield t) => t.Expire > 0f ? t.Expire : float.MaxValue;
    }

    // ══════════════════ 소모품 쉴드 API ══════════════════

    /// <summary>
    /// 임시 쉴드 주기 (SA). seconds ≤ 0 = 깨질 때까지. source ≠ 0이면 같은 출처는 새로 덮어씀(쌓이지 않음).
    /// 다른 사람에게 줄 때는 RPC_AddTempShield
    /// </summary>
    public void AddTempShield(int amount, float seconds, int source = 0)
    {
        if (!HasStateAuthority || IsDead || amount <= 0) return;

        float expire = seconds > 0f ? Runner.SimulationTime + seconds : 0f;
        if (source != 0) _temp.RemoveAll(t => t.Source == source);
        _temp.Add(new TempShield { Amount = amount, Expire = expire, Source = source });

        // 합계 상한 — 넘치면 오래된(먼저 끝나는) 것부터 깎음
        int cap = Mathf.Max(1, Mathf.RoundToInt(_stats.Get(StatType.MaxHP) * tempShieldCapOfMaxHp));
        int sum = 0;
        foreach (var t in _temp) sum += t.Amount;
        for (int i = 0; sum > cap && i < _temp.Count; i++)
        {
            var t = _temp[i];
            int cut = Mathf.Min(t.Amount, sum - cap);
            t.Amount -= cut;
            sum -= cut;
            _temp[i] = t;
        }
        _temp.RemoveAll(t => t.Amount <= 0);
        SyncTemp();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddTempShield(int amount, float seconds, int source) => AddTempShield(amount, seconds, source);

    /// <summary>그 출처의 임시 쉴드 없애기 (SA)</summary>
    public void RemoveTempShield(int source)
    {
        if (!HasStateAuthority || source == 0) return;
        if (_temp.RemoveAll(t => t.Source == source) > 0) SyncTemp();
    }

    /// <summary>재생 쉴드를 바로 가득 (SA) — 스킬 · 특성용</summary>
    public void RefillRegenShield()
    {
        if (!HasStateAuthority || IsDead) return;
        ShieldRegen = ShieldRegenMax;
    }

    void SyncTemp()
    {
        int sum = 0;
        foreach (var t in _temp) sum += t.Amount;
        if (ShieldTemp != sum) ShieldTemp = sum;
    }

    void ClearShields()
    {
        _temp.Clear();
        ShieldTemp = 0;
        ShieldRegen = 0;
        ShieldSoul = 0;
        _regenFrac = _soulFrac2 = _soulCost = 0f;
    }

    /// <summary>부활 · 새 라운드 — 재생 쉴드만 가득</summary>
    void ResetShieldsOnSpawn()
    {
        ClearShields();
        ShieldRegenMax = Mathf.Max(0, Mathf.RoundToInt(_stats.Get(StatType.Shield)));
        ShieldSoulMax = Mathf.Max(0, Mathf.RoundToInt(_stats.Get(StatType.SoulShield)));
        ShieldRegen = ShieldRegenMax;
        _lastHurt = -999f;
    }
}
