using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 쉐도우 워커
///   패시브 0  그림자        — 그림자가 따라다님. 전투 중이 아닐 때 '암흑'이 참.
///                            암흑이 가득 찬 상태로 무기로 적을 때리면 소모하고 대상 화면을 1초 암전
///   스킬 0    그림자 무기   — N초간 무기로 때리면 잠시 뒤 그림자가 같은 대상을 한 번 더 (피해 40%, 인챈트 100%, 둔화 10% 1초)
///   스킬 1    그림자 이동   — 그림자와 자리를 바꾸며 바라보는 곳으로 순간이동 (그림자는 원래 자리에 남음)
///   스킬 2    그림자 손     — 바라보는 바닥에 손을 남김. 근처에 적이 오면 뻗어서 피해 + 1초 속박.
///                            최대 8개, 한 번 맞은 적은 3초간 손의 대상이 안 됨
///   궁극기    완전한 암흑   — 주위를 그림자 영역으로. 안의 적은 시야가 매우 좁아지고, 밖에선 안이 안 보임.
///                            영역 안에서 쉐도우 워커는 그림자 이동 쿨 1초, 이속 +30%, 안의 적 위치가 보임
/// 수치는 레벨별(LevelValue) — 능력 데이터의 각 스킬·패시브 탭에서도 편집.
///   {x:dark} 암흑 상태 · {x:hands} 놓인 손 개수
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Shadow Walker")]
public class ShadowWalkerAbility : AbilityBehaviour
{
    [Header("항목 번호 (Passives / Skills 배열)")]
    public int ShadowPassive = 0;
    public int WeaponSkill = 0;
    public int MoveSkill = 1;
    public int HandSkill = 2;
    public int UltSkill = 3;

    [Header("그림자 · 암흑")]
    [Tooltip("그림자가 내 겉모습(팔·손·무기·몸통·머리)을 복제해서 내 모션을 그대로 따라 함.\n" +
             "손 IK·손가락·무기 휘두르기까지 전부 — 따로 리그를 만들 필요 없음")]
    public bool ShadowCopiesMe = true;
    [Tooltip("켜면 그림자는 그림자 무기(스킬) 동안에만 기본 공격을 따라 휘두름 — 피해가 없는 헛스윙이 안 보이게.\n" +
         "변신(용 · 거신병 등) 중엔 상관없이 다 따라 함")]
    public bool EchoOnlyWithWeapon = true;
    [Tooltip("그림자 재질 (검게 · 반투명 등). 비우면 검은 Unlit")]
    public Material ShadowMaterial;
    [Tooltip("그림자에 추가로 붙일 것 (연기 파티클 등) — 선택.\nShadowCopiesMe를 끄면 이게 그림자 모델 자체 (가만히 따라다니기만 함)")]
    public GameObject ShadowPrefab;
    [Tooltip("암흑 게이지 슬롯 (능력 데이터 '게이지'에 같은 번호로 추가)")]
    [Range(0, 3)] public int DarkChargeSlot = 0;
    [ForPassive(nameof(ShadowPassive), "암흑 충전 시간 (초, 비전투)")] public LevelValue DarkChargeTime = 4f;
    [ForPassive(nameof(ShadowPassive), "암전 시간 (초)")] public LevelValue BlindTime = 1f;
    [ForPassive(nameof(ShadowPassive), "암전 시야 (m, 최소 1)")] public LevelValue BlindRadius = 2f;
    const float DarkMax = 100f;

    [Header("그림자 무기")]
    [ForSkill(nameof(WeaponSkill), "지속 (초)")] public LevelValue WeaponDuration = 6f;
    [ForSkill(nameof(WeaponSkill), "그림자 피해 (원래 피해의 %)")] public LevelValue EchoPct = 40f;
    [ForSkill(nameof(WeaponSkill), "그림자 공격 지연 (초)")] public LevelValue EchoDelay = 0.25f;
    [ForSkill(nameof(WeaponSkill), "둔화 (%)")] public LevelValue EchoSlowPct = 10f;
    [ForSkill(nameof(WeaponSkill), "둔화 (초)")] public LevelValue EchoSlowTime = 1f;
    [Tooltip("그림자 무기 동안 무기에 붙일 파츠 (검은 기운 등, Attach = Weapon)")]
    public AlterData WeaponAlter;

    [Header("그림자 이동")]
    [ForSkill(nameof(MoveSkill), "최대 거리 (m)")] public LevelValue MoveRange = 12f;
    [Tooltip("순간이동을 막는 레이어 (벽·바닥)")]
    public LayerMask BlinkMask = 1;

    [Header("그림자 손")]
    [ForSkill(nameof(HandSkill), "놓는 최대 거리 (m)")] public LevelValue HandPlaceRange = 15f;
    [ForSkill(nameof(HandSkill), "감지 반경 (m)")] public LevelValue HandRadius = 4f;
    [ForSkill(nameof(HandSkill), "피해 계수 (공격력 %)")] public LevelValue HandCoef = 70f;
    [ForSkill(nameof(HandSkill), "고정 추가 피해")] public LevelValue HandFlat = 0f;
    [ForSkill(nameof(HandSkill), "속박 (초)")] public LevelValue HandRoot = 1f;
    [ForSkill(nameof(HandSkill), "같은 적 면역 (초)")] public LevelValue HandImmune = 3f;
    [ForSkill(nameof(HandSkill), "최대 개수")] public LevelValue HandMax = 8f;
    [Tooltip("한 번 붙잡으면 (속박이 끝난 뒤) 사라짐. 끄면 HandRearm 뒤 다시 뻗음")]
    public bool HandOneShot = true;
    [Tooltip("사라지는 시간 (초) — 손에 'Vanish' 상태가 있으면 그걸 재생, 없으면 작아지며 사라짐")]
    public float HandVanishTime = 0.3f;
    [Tooltip("손 하나가 다시 뻗을 때까지 (초)")]
    public float HandRearm = 1.5f;
    [Tooltip("손이 사라지는 시간 (초)")]
    public float HandLifetime = 45f;
    [Tooltip("그림자 손 모델. RigAnimator를 붙이면 Idle · Appear(놓을 때) · Grab(붙잡을 때) 상태를 재생")]
    public GameObject HandPrefab;
    [Tooltip("붙잡을 때 손이 뻗는 높이 (대상 발밑 기준, m)")]
    public float GrabAimHeight = 0.9f;
    public GameObject HandGrabFx;

    [Header("궁극기 — 완전한 암흑")]
    [ForSkill(nameof(UltSkill), "반경 (m)")] public LevelValue ZoneRadius = 12f;
    [ForSkill(nameof(UltSkill), "지속 (초)")] public LevelValue ZoneDuration = 8f;
    [ForSkill(nameof(UltSkill), "영역 안 이속 증가 (%)")] public LevelValue ZoneMoveSpeedPct = 30f;
    [ForSkill(nameof(UltSkill), "영역 안 그림자 이동 쿨 (초)")] public LevelValue ZoneMoveCooldown = 1f;
    [ForSkill(nameof(UltSkill), "영역 안 상대 시야 (m, 최소 1)")] public LevelValue ZoneVisionRadius = 2f;
    [Tooltip("검은 돔 (비우면 검은 구를 자동으로 만듦). 반지름 1 기준 크기로 만들 것")]
    public GameObject ZoneDomePrefab;
    [Tooltip("쉐도우 워커 화면에서 영역 안 적 머리 위에 뜨는 표시 (벽 너머로 보이게 만든 것)")]
    public GameObject EnemyMarkerPrefab;

    [Header("연출")]
    public GameObject EchoFx;
    public GameObject BlinkFx;
    public GameObject BlindHitFx;

    const int EvEcho = 0, EvBlinkFrom = 1, EvBlinkTo = 2, EvHandPlace = 3, EvGrab = 4, EvZone = 5, EvBlind = 6, EvWeaponOn = 7;
    const int SrcZoneSpeed = 0;

    // ── 상태 (SA) ──
    private PlayerHealth _health;
    private PlayerAttack _attack;
    private PlayerEnchant _enchant;
    private PlayerAlter _alter;

    private float _weaponEnd;
    private bool _weaponOn;

    struct Echo { public float At; public PlayerHealth Target; public float Dmg; public bool Ench; public bool Melee; }
    private readonly List<Echo> _echoes = new();

    class Hand { public Vector3 Pos; public float Expire; public float ReadyAt; }
    private readonly List<Hand> _hands = new();
    private readonly Dictionary<PlayerHealth, float> _handImmune = new();

    private Vector3 _zonePos;
    private float _zoneEnd;
    private float _zoneRadius;

    // ── 연출 (모든 클라이언트) ──
    private ShadowFollower _shadow;
    private ShadowMimic _mimic;
    private readonly List<(GameObject go, float expire)> _handViews = new();

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool InZone => _zoneEnd > Now && ShadowZoneView.Inside(_zonePos, _zoneRadius, Ctx.transform.position);

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        _attack = Ctx.GetComponent<PlayerAttack>();
        _enchant = Ctx.GetComponent<PlayerEnchant>();
        _alter = Ctx.GetComponent<PlayerAlter>();

        // 그림자 — 모든 클라이언트
        if (ShadowCopiesMe || ShadowPrefab != null)
        {
            Vector3 at = Ctx.transform.position - Ctx.transform.forward * 1.4f;
            var go = new GameObject("Shadow");
            go.transform.SetPositionAndRotation(at, Ctx.transform.rotation);

            if (ShadowPrefab != null)
            {
                var extra = Instantiate(ShadowPrefab, go.transform, false);
                foreach (var c in extra.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }

            _shadow = go.AddComponent<ShadowFollower>();
            _shadow.Target = Ctx.transform;
            _shadow.Owner = _health;

            if (ShadowCopiesMe)
            {
                var rig = Ctx.GetComponentInChildren<FPArmRig>(true);
                if (rig != null)
                {
                    _mimic = go.AddComponent<ShadowMimic>();
                    // 다른 사람 화면: 몸 전체 · 내 화면: 팔 · 손 · 무기만
                    _mimic.Init(Ctx.transform, rig, ShadowMaterial, Lv(EchoDelay), armsOnly: Ctx.HasInputAuthority);
                    _mimic.MimicBasicAttack = !EchoOnlyWithWeapon;
                }
                else Debug.LogWarning("[쉐도우 워커] FPArmRig를 못 찾아서 그림자가 모습을 따라 할 수 없음");
            }
        }

        if (!Ctx.HasStateAuthority) return;

        Ctx.SetMaxCharge(DarkChargeSlot, DarkMax);
        if (_attack != null) _attack.WeaponHit += OnWeaponHit;
    }

    public override void OnUnequip()
    {
        if (_shadow != null) Destroy(_shadow.gameObject);
        foreach (var (go, _) in _handViews) if (go != null) Destroy(go);
        _handViews.Clear();

        if (!Ctx.HasStateAuthority) return;

        if (_attack != null) _attack.WeaponHit -= OnWeaponHit;
        EndWeapon();
        Ctx.Stats?.RemoveBySource(Src(SrcZoneSpeed));
        _hands.Clear();
    }

    public override void Tick(float dt)
    {
        if (_mimic != null) _mimic.Delay = Lv(EchoDelay);     // 그림자 공격과 같은 타이밍에 휘두름

        bool dead = _health != null && _health.IsDead;

        // 암흑 — 비전투 중 충전
        if (!dead && _health != null && !_health.InCombat)
            Ctx.AddCharge(DarkChargeSlot, DarkMax / Mathf.Max(0.1f, Lv(DarkChargeTime)) * dt);

        if (_weaponOn && (dead || Now >= _weaponEnd)) EndWeapon();

        TickEchoes();
        TickHands();
        TickZone(dead);
    }

    /// <summary>모든 클라이언트 — 손 연출 목록 정리 (각 손은 수명이 되면 스스로 사라짐)</summary>
    void CleanHandViews()
    {
        for (int i = _handViews.Count - 1; i >= 0; i--)
            if (_handViews[i].go == null || Time.time >= _handViews[i].expire)
            {
                if (_handViews[i].go != null) Destroy(_handViews[i].go);
                _handViews.RemoveAt(i);
            }
    }

    // ══════════════════ 스킬 ══════════════════

    int IndexOf(AbilityData.SkillDef s) => System.Array.IndexOf(Data.Skills, s);

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == WeaponSkill) { BeginWeapon(); return true; }
        if (i == MoveSkill) { Blink(skill); return true; }
        if (i == HandSkill) { PlaceHand(); return true; }
        if (i == UltSkill) { BeginZone(); return true; }
        return false;
    }

    // ── 무기 타격 (SA) — 암흑 · 그림자 무기 ──

    void OnWeaponHit(PlayerHealth target, float outgoing, bool enchants)
    {
        if (target == null) return;

        // 암흑이 가득 차 있으면 소모하고 암전
        if (Ctx.SpendCharge(DarkChargeSlot, DarkMax) && target.TryGetComponent<PlayerStatus>(out var st))
        {
            st.Apply(StatusType.Blind, Mathf.Max(PlayerStatus.MinBlindRadius, Lv(BlindRadius)), Lv(BlindTime), Ctx.Object.InputAuthority);
            Broadcast(EvBlind, target.transform.position + Vector3.up * 1.6f);
        }

        if (_weaponOn)
        {
            var w = Ctx.Equipment != null ? Ctx.Equipment.GetWeapon() : null;
            _echoes.Add(new Echo
            {
                At = Now + Lv(EchoDelay),
                Target = target,
                Dmg = outgoing * Lv(EchoPct) * 0.01f,
                Ench = enchants,
                Melee = w == null || w.Class == WeaponClass.Melee,
            });
        }
    }

    void BeginWeapon()
    {
        _weaponEnd = Now + Lv(WeaponDuration);
        if (!_weaponOn && WeaponAlter != null) _alter?.Add(WeaponAlter);
        _weaponOn = true;
        Broadcast(EvWeaponOn, Ctx.transform.position);
    }

    void EndWeapon()
    {
        if (!_weaponOn) return;
        _weaponOn = false;
        if (_mimic != null) _mimic.MimicAttackUntil = 0f;
        if (WeaponAlter != null) _alter?.Remove(WeaponAlter);
        ReleaseCooldown(WeaponSkill);
    }

    void TickEchoes()
    {
        var self = Ctx.Object.InputAuthority;
        for (int i = _echoes.Count - 1; i >= 0; i--)
        {
            var e = _echoes[i];
            if (Now < e.At) continue;
            _echoes.RemoveAt(i);
            if (e.Target == null || e.Target.IsDead) continue;

            // 그림자 공격 — 인챈트 100%, 흡혈 없음, OnHit 트리거 없음 (다시 그림자 공격이 생기지 않게)
            var info = DamageInfo.FromPlayerHit(Mathf.Max(1, Mathf.RoundToInt(e.Dmg)), self, ShadowPos());
            info.IsMelee = e.Melee;
            if (e.Ench) _enchant?.ModifyWeaponHit(ref info);
            e.Target.RPC_TakeDamage(info);

            if (e.Ench && _enchant != null)
            {
                int heal = 0;
                _enchant.OnWeaponHit(e.Target, e.Dmg, ref heal);
            }

            if (e.Target.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Slow, Lv(EchoSlowPct), Lv(EchoSlowTime), self);

            Broadcast(EvEcho, e.Target.transform.position + Vector3.up * 1.2f);
        }
    }

    Vector3 ShadowPos() => _shadow != null ? _shadow.transform.position : Ctx.transform.position;

    // ── 그림자 이동 ──

    void Blink(AbilityData.SkillDef skill)
    {
        var cam = Ctx.CameraRootTransform;
        if (cam == null || _health == null) return;

        Vector3 from = Ctx.transform.position;
        Vector3 eye = cam.position;
        Vector3 dir = cam.forward;
        float range = Lv(MoveRange);

        var self = Ctx.transform.root;
        Vector3 target = CastUtil.Raycast(eye, dir, range, BlinkMask, self, out var hit)
            ? hit.point - dir * 0.6f            // 벽 바로 앞
            : eye + dir * range;

        var cc = Ctx.GetComponent<CharacterController>();
        Vector3 dest = SpawnPoint.GroundedPosition(target, cc, 10f, self);

        Broadcast(EvBlinkFrom, from);          // 그림자가 원래 자리에 남음
        _health.Teleport(dest, Ctx.transform.rotation);
        Broadcast(EvBlinkTo, dest);

        // 영역 안이면 쿨 1초
        if (InZone) Ctx.SetCooldown(skill.Slot.Index(), Lv(ZoneMoveCooldown));
    }

    // ── 그림자 손 ──

    void PlaceHand()
    {
        var cam = Ctx.CameraRootTransform;
        if (cam == null) return;

        float range = Lv(HandPlaceRange);
        Vector3 p = Physics.Raycast(cam.position, cam.forward, out var hit, range, BlinkMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : cam.position + cam.forward * range;

        // 바닥에 붙임
        if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var g, 20f, BlinkMask, QueryTriggerInteraction.Ignore))
            p = g.point;

        _hands.Add(new Hand { Pos = p, Expire = Now + HandLifetime, ReadyAt = Now + 0.3f });
        int max = Mathf.Max(1, Mathf.RoundToInt(Lv(HandMax)));
        while (_hands.Count > max) _hands.RemoveAt(0);

        Broadcast(EvHandPlace, p);
    }

    void TickHands()
    {
        if (_hands.Count == 0) return;

        for (int i = _hands.Count - 1; i >= 0; i--)
            if (Now >= _hands[i].Expire) _hands.RemoveAt(i);

        var self = Ctx.Object.InputAuthority;
        float radius = Lv(HandRadius);

        Hand used = null;
        foreach (var hand in _hands)
        {
            if (used != null) break;                // 한 틱에 하나씩 (목록을 지우며 돌지 않게)
            if (Now < hand.ReadyAt) continue;

            PlayerHealth best = null;
            float bestD = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(hand.Pos, radius, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                if (_hits[k].transform.root == Ctx.transform.root) continue;
                var h = _hits[k].GetComponentInParent<PlayerHealth>();
                if (h == null || h.IsDead) continue;
                if (_handImmune.TryGetValue(h, out float until) && Now < until) continue;

                float d = (h.transform.position - hand.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            if (best == null) continue;

            hand.ReadyAt = Now + HandRearm;
            if (HandOneShot) used = hand;           // 한 번 붙잡으면 사라짐
            _handImmune[best] = Now + Lv(HandImmune);

            var stats = Ctx.Stats;
            float dmg = DamageCalc.CalcOutgoing(stats, stats.Get(StatType.AttackPower) * Lv(HandCoef) * 0.01f + Lv(HandFlat),
                                                DamageSource.Skill, true, out bool crit);
            var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), self, hand.Pos);
            info.IsCrit = crit;
            best.RPC_TakeDamage(info);

            if (best.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Root, 1f, Lv(HandRoot), self);

            Broadcast(EvGrab, best.transform.position);
        }
        if (used != null) _hands.Remove(used);
    }

    // ── 완전한 암흑 ──

    void BeginZone()
    {
        _zonePos = Ctx.transform.position;
        _zoneRadius = Lv(ZoneRadius);
        _zoneEnd = Now + Lv(ZoneDuration);
        Broadcast(EvZone, _zonePos);
    }

    void TickZone(bool dead)
    {
        bool active = _zoneEnd > Now;
        bool inside = active && !dead && ShadowZoneView.Inside(_zonePos, _zoneRadius, Ctx.transform.position);

        var stats = Ctx.Stats;
        stats?.RemoveBySource(Src(SrcZoneSpeed));
        if (inside) stats?.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Percent, Lv(ZoneMoveSpeedPct), Src(SrcZoneSpeed)));

        if (!active && _zoneEnd > 0f)
        {
            _zoneEnd = 0f;
            ReleaseCooldown(UltSkill);
        }
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        CleanHandViews();

        switch (key)
        {
            case EvEcho: Ctx.PlayEffectLocal(EchoFx, pos); break;
            case EvBlind: Ctx.PlayEffectLocal(BlindHitFx, pos); break;
            case EvBlinkFrom:
                Ctx.PlayEffectLocal(BlinkFx, pos);
                if (_shadow != null) _shadow.Snap(pos);     // 자리 교체 — 그림자가 원래 자리에
                break;
            case EvBlinkTo: Ctx.PlayEffectLocal(BlinkFx, pos); break;

            case EvHandPlace:
                if (HandPrefab != null)
                {
                    // 프리팹 루트의 기울기(세우기 등)는 유지하고 좌우 방향만 무작위
                    var go = Instantiate(HandPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * HandPrefab.transform.rotation);
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    Destroy(go, HandLifetime);
                    _handViews.Add((go, Time.time + HandLifetime));
                    var ra = go.GetComponentInChildren<RigAnimator>();
                    if (ra != null) ra.Play("Appear");          // 솟아오르는 상태가 있으면 (없으면 Idle)
                }
                int max = Mathf.Max(1, Mathf.RoundToInt(Lv(HandMax)));
                while (_handViews.Count > max)
                {
                    if (_handViews[0].go != null) Destroy(_handViews[0].go);
                    _handViews.RemoveAt(0);
                }
                break;

            case EvGrab:
                Ctx.PlayEffectLocal(HandGrabFx, pos);
                GrabAnim(pos);
                break;

            case EvZone:
                ShadowZoneView.Create(pos, Lv(ZoneRadius), Lv(ZoneDuration), Ctx.transform, Ctx.HasInputAuthority,
                                      ZoneDomePrefab, EnemyMarkerPrefab, Ctx.HitMask, Lv(ZoneVisionRadius));
                break;

            case EvWeaponOn:
                if (_mimic != null) _mimic.MimicAttackUntil = Time.time + Lv(WeaponDuration);
                break;
        }
    }

    /// <summary>붙잡은 손 — 대상과 가장 가까운 손이 그쪽을 보고 "Grab" 재생, 뻗는 IK가 대상으로</summary>
    void GrabAnim(Vector3 targetPos)
    {
        GameObject best = null;
        float bestD = float.MaxValue;
        float reach = Lv(HandRadius) + 1.5f;
        foreach (var (go, _) in _handViews)
        {
            if (go == null) continue;
            float d = (go.transform.position - targetPos).sqrMagnitude;
            if (d < bestD && d <= reach * reach) { bestD = d; best = go; }
        }
        if (best == null) return;

        float hold = Mathf.Max(0.3f, Lv(HandRoot));
        var ra = best.GetComponentInChildren<RigAnimator>();
        if (ra != null)
        {
            ra.Play("Grab", hold);
            ra.AimAt(targetPos + Vector3.up * GrabAimHeight, hold);
        }

        if (HandOneShot)
        {
            // 목록에서 빼서 다시 고르지 않게 → 속박이 끝나면 사라짐
            for (int i = _handViews.Count - 1; i >= 0; i--)
                if (_handViews[i].go == best) _handViews.RemoveAt(i);
            Ctx.StartCoroutine(VanishAfter(best, ra, hold));
        }
    }

    System.Collections.IEnumerator VanishAfter(GameObject go, RigAnimator ra, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go == null) yield break;

        float t = Mathf.Max(0.05f, HandVanishTime);
        var vanish = ra != null && ra.Data != null ? ra.Data.Find("Vanish") : null;
        if (vanish != null)
        {
            ra.Play("Vanish", vanish.Length / Mathf.Max(0.01f, vanish.Speed));
            yield return new WaitForSeconds(vanish.Length / Mathf.Max(0.01f, vanish.Speed));
        }
        else
        {
            Vector3 s0 = go.transform.localScale;
            for (float e = 0f; e < t && go != null; e += Time.deltaTime)
            {
                float k = 1f - e / t;
                go.transform.localScale = s0 * (k * k);
                yield return null;
            }
        }
        if (go != null) Destroy(go);
    }

    // ══════════════════ 설명 ══════════════════

    public override string DescToken(string key, int rank)
    {
        switch (key)
        {
            case "dark":
                float r = Ctx != null ? Ctx.GetChargeRatio(DarkChargeSlot) : 0f;
                return r >= 0.999f ? "<b>암흑 충전됨</b> — 다음 무기 공격이 대상을 암전" : $"암흑 {Mathf.FloorToInt(r * 100f)}%";
            case "hands":
                return _hands.Count > 0 ? $"놓인 손 {_hands.Count}개" : null;
        }
        return null;
    }

    // ══════════════════ 치트 (/job) ══════════════════

    public override string DevCommand(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "help";
        switch (sub)
        {
            case "help":
                return "쉐도우 워커: /job dark (암흑 충전) · hands (놓인 손 지우기) · zone (완전한 암흑)";
            case "dark":
            case "암흑":
                Ctx.SetMaxCharge(DarkChargeSlot, DarkMax);
                Ctx.AddCharge(DarkChargeSlot, DarkMax);
                return "암흑 충전됨";
            case "hands":
            case "손":
                int n = _hands.Count;
                _hands.Clear();
                return $"그림자 손 {n}개 판정 지움 (모양은 시간이 지나면 사라짐)";
            case "zone":
            case "궁":
                BeginZone();
                return "완전한 암흑";
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index) =>
        index == 0 ? new[] { ("dark", "암흑 충전"), ("hands", "놓인 손 지우기"), ("zone", "완전한 암흑") } : null;
}