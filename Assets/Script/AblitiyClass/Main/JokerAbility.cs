using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 조커
///   패시브 0  조커          — 랜덤 능력을 2개까지 (두 번째는 V 키)
///   패시브 1  환락          — 소울 획득 +25%
///   스킬 0    조커 뽑기     — 뒤로 순간이동, 원래 자리에 조커 카드. 누가 밟으면 잠시 뒤 폭발 (피해 + 둔화 20% 2초)
///   스킬 1    원카드!       — 주위 모든 적의 일반 스킬(궁극기·랜덤 제외) 쿨타임 +3초
///   스킬 2    대타카드      — 쿨타임 중인 랜덤 능력 하나의 쿨을 초기화하고, 그 남은 쿨을 대타카드가 대신 짊어짐
///   궁극기    깜짝선물      — 앞에 분신 소환 (N초). 가장 가까운 적을 쫓아가 들고 있던 무기로 공격 (피해 50%).
///                            궁극기 키를 다시 누르거나, 시간이 다 되거나, 내가 죽으면 폭발 (조커 뽑기와 같은 폭발)
/// {x:cards} 깔린 카드 수 · {x:clone} 분신 상태
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Joker")]
public class JokerAbility : AbilityBehaviour
{
    [Header("항목 번호 (Passives / Skills 배열)")]
    public int JokerPassive = 0;
    public int RevelPassive = 1;
    public int DrawSkill = 0;
    public int OneCardSkill = 1;
    public int SubSkill = 2;
    public int UltSkill = 3;

    [Header("환락")]
    [ForPassive(nameof(RevelPassive), "소울 획득 배율 (1.25 = +25%)")] public LevelValue SoulGain = 1.25f;

    [Header("조커 뽑기 — 순간이동 · 카드")]
    [ForSkill(nameof(DrawSkill), "뒤로 거리 (m)")] public LevelValue BackDistance = 7f;
    [ForSkill(nameof(DrawSkill), "카드 밟는 반경 (m)")] public LevelValue CardTriggerRadius = 1.3f;
    [ForSkill(nameof(DrawSkill), "밟고 터지기까지 (초)")] public LevelValue CardDelay = 0.6f;
    [ForSkill(nameof(DrawSkill), "카드 유지 (초)")] public LevelValue CardLifetime = 30f;
    [ForSkill(nameof(DrawSkill), "최대 카드 수")] public LevelValue CardMax = 3f;
    [Tooltip("벽 레이어 — 순간이동이 벽을 넘지 않게")]
    public LayerMask WallMask = 1;

    [Header("폭발 (카드 · 분신 공통)")]
    [ForSkill(nameof(DrawSkill), "폭발 피해 계수 (공격력 %)")] public LevelValue BlastCoef = 120f;
    [ForSkill(nameof(DrawSkill), "폭발 고정 피해")] public LevelValue BlastFlat = 0f;
    [ForSkill(nameof(DrawSkill), "폭발 반경 (m)")] public LevelValue BlastRadius = 3.5f;
    [ForSkill(nameof(DrawSkill), "둔화 (%)")] public LevelValue BlastSlowPct = 20f;
    [ForSkill(nameof(DrawSkill), "둔화 (초)")] public LevelValue BlastSlowTime = 2f;

    [Header("원카드!")]
    [ForSkill(nameof(OneCardSkill), "반경 (m)")] public LevelValue OneCardRadius = 15f;
    [ForSkill(nameof(OneCardSkill), "쿨타임 증가 (초)")] public LevelValue OneCardSeconds = 3f;

    [Header("깜짝선물 — 분신")]
    [Tooltip("JokerClone 프리팹 (NetworkObject — Fusion 프리팹 목록에 등록)")]
    public NetworkObject ClonePrefab;
    [ForSkill(nameof(UltSkill), "분신 유지 (초)")] public LevelValue CloneDuration = 10f;
    [ForSkill(nameof(UltSkill), "분신 폭발 배율 (1 = 카드와 같음)")] public LevelValue CloneBlastMul = 1f;

    [Header("연출")]
    public GameObject CardPrefab;
    public GameObject CardArmFx;
    public GameObject BlastFx;
    public GameObject BlinkFx;
    public GameObject OneCardFx;
    public GameObject SubCardFx;

    const int EvCardPlace = 0, EvCardArm = 1, EvCardGone = 2, EvBlast = 3, EvBlink = 4, EvOneCard = 5, EvSub = 6;
    const int SrcNone = 0;

    private PlayerHealth _health;

    class Card { public Vector3 Pos; public float Expire; public float BoomAt = -1f; }
    private readonly List<Card> _cards = new();
    private readonly List<GameObject> _cardViews = new();

    private NetworkObject _clone;
    private float _cloneEnd;

    [System.NonSerialized] private readonly Collider[] _hits = new Collider[32];

    float Now => Ctx.Runner != null ? Ctx.Runner.SimulationTime : Time.time;
    bool CloneAlive => _clone != null && _clone.IsValid;

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _health = Ctx.GetComponent<PlayerHealth>();
        if (!Ctx.HasStateAuthority) return;

        Ctx.SetDualRandom(true);
        if (_health != null) _health.SoulGainMul = Lv(SoulGain);
    }

    public override void OnUnequip()
    {
        foreach (var v in _cardViews) if (v != null) Destroy(v);
        _cardViews.Clear();

        if (!Ctx.HasStateAuthority) return;
        // 랜덤 2개는 끄지 않음 — 사망 때마다 재빌드되므로 (라운드 리셋이 끔)
        if (_health != null) _health.SoulGainMul = 1f;
        _cards.Clear();
        DespawnClone(false);
    }

    public override void Tick(float dt)
    {
        if (_health != null) _health.SoulGainMul = Lv(SoulGain);
        bool dead = _health != null && _health.IsDead;

        TickCards();

        if (_cloneEnd > 0f)
        {
            // 시간 끝 · 내가 죽음 → 궁극기 다시 누르기와 같은 폭발. 분신이 이미 없어졌으면 정리만
            if (!CloneAlive) DespawnClone(false);
            else if (dead || Now >= _cloneEnd) DespawnClone(true);
        }
    }

    // ══════════════════ 스킬 ══════════════════

    int IndexOf(AbilityData.SkillDef s) => System.Array.IndexOf(Data.Skills, s);

    public override bool CanUseSkill(AbilityData.SkillDef skill)
    {
        if (IndexOf(skill) == SubSkill) return BestRandomSlot(out _) >= 0;    // 쿨 중인 랜덤 능력이 있어야
        return true;
    }

    public override bool OnSkillEffect(AbilityData.SkillDef skill)
    {
        int i = IndexOf(skill);
        if (i == DrawSkill) { DrawJoker(); return true; }
        if (i == OneCardSkill) { OneCard(); return true; }
        if (i == SubSkill) { Substitute(skill); return true; }
        if (i == UltSkill) { SpawnClone(); return true; }
        return false;
    }

    // 궁극기 다시 누르기 → 분신 폭발
    public override bool CanRecast(AbilityData.SkillDef skill) => IndexOf(skill) == UltSkill && CloneAlive;

    public override bool OnSkillRecast(int slot, AbilityData.SkillDef skill)
    {
        if (!CanRecast(skill)) return false;
        DespawnClone(true);
        return true;
    }

    // ── 조커 뽑기 ──

    void DrawJoker()
    {
        if (_health == null) return;

        Vector3 from = Ctx.transform.position;
        Vector3 back = -LookFlat();
        var self = Ctx.transform.root;

        // 내 몸 콜라이더(등 뒤 피격 판정 등)는 무시 — 예전엔 여기 막혀서 제자리였음
        float dist = Lv(BackDistance);
        Vector3 chest = from + Vector3.up * 1.2f;
        if (CastUtil.SphereCast(chest, 0.3f, back, dist, WallMask, self, out var wall))
            dist = Mathf.Max(0f, wall.distance - 0.4f);

        var cc = Ctx.GetComponent<CharacterController>();
        Vector3 dest = SpawnPoint.GroundedPosition(from + back * dist, cc, 10f, self);

        // 카드 — 원래 자리 바닥
        Vector3 cardPos = from;
        if (CastUtil.Raycast(from + Vector3.up, Vector3.down, 5f, WallMask, self, out var g))
            cardPos = g.point;

        _cards.Add(new Card { Pos = cardPos, Expire = Now + Lv(CardLifetime) });
        int max = Mathf.Max(1, Mathf.RoundToInt(Lv(CardMax)));
        while (_cards.Count > max)
        {
            Broadcast(EvCardGone, _cards[0].Pos);
            _cards.RemoveAt(0);
        }

        Broadcast(EvCardPlace, cardPos);
        Broadcast(EvBlink, from);
        _health.Teleport(dest, Ctx.transform.rotation);
        Broadcast(EvBlink, dest);
    }

    void TickCards()
    {
        if (_cards.Count == 0) return;
        float radius = Lv(CardTriggerRadius);

        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            var c = _cards[i];

            if (c.BoomAt < 0f)
            {
                if (Now >= c.Expire)
                {
                    Broadcast(EvCardGone, c.Pos);
                    _cards.RemoveAt(i);
                    continue;
                }

                // 누가 밟았나 (조커 본인 제외)
                if (AnyEnemy(c.Pos, radius))
                {
                    c.BoomAt = Now + Lv(CardDelay);
                    Broadcast(EvCardArm, c.Pos);
                }
                continue;
            }

            if (Now >= c.BoomAt)
            {
                Explode(c.Pos, 1f);
                Broadcast(EvCardGone, c.Pos);
                _cards.RemoveAt(i);
            }
        }
    }

    bool AnyEnemy(Vector3 p, float r)
    {
        int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.5f, r, _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.root == Ctx.transform.root) continue;
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h != null && !h.IsDead) return true;
        }
        return false;
    }

    /// <summary>카드·분신 폭발 — 피해 + 둔화</summary>
    void Explode(Vector3 p, float mul)
    {
        var self = Ctx.Object.InputAuthority;
        var stats = Ctx.Stats;
        var seen = new HashSet<PlayerHealth>();

        int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * 0.5f, Lv(BlastRadius), _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.root == Ctx.transform.root) continue;
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead || !seen.Add(h)) continue;
            if (TeamUtil.IsFriendly(self, h)) continue;            // 나 · 같은 팀

            float dmg = DamageCalc.CalcOutgoing(stats, (stats.Get(StatType.AttackPower) * Lv(BlastCoef) * 0.01f + Lv(BlastFlat)) * mul,
                                                DamageSource.Skill, true, out bool crit);
            var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(dmg), self, p);
            info.IsCrit = crit;
            h.RPC_TakeDamage(info);

            if (h.TryGetComponent<PlayerStatus>(out var st))
                st.Apply(StatusType.Slow, Lv(BlastSlowPct), Lv(BlastSlowTime), self);
        }

        Broadcast(EvBlast, p);
    }

    // ── 원카드! ──

    void OneCard()
    {
        Vector3 o = Ctx.transform.position;
        float seconds = Lv(OneCardSeconds);
        var seen = new HashSet<AbilityRuntime>();

        int n = Physics.OverlapSphereNonAlloc(o, Lv(OneCardRadius), _hits, Ctx.HitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].transform.root == Ctx.transform.root) continue;
            var ar = _hits[i].GetComponentInParent<AbilityRuntime>();
            var h = _hits[i].GetComponentInParent<PlayerHealth>();
            if (ar == null || h == null || h.IsDead || !seen.Add(ar)) continue;

            ar.RPC_AddSkillCooldown(seconds);
            Broadcast(EvOneCard, ar.transform.position + Vector3.up * 2.2f);
        }
        Broadcast(EvOneCard, o + Vector3.up * 2.2f);
    }

    // ── 대타카드 ──

    /// <summary>쿨이 가장 많이 남은 랜덤 능력 칸 (F·V). 없으면 -1</summary>
    int BestRandomSlot(out float remain)
    {
        remain = 0f;
        int best = -1;
        foreach (var s in new[] { SkillSlot.Random, SkillSlot.Random2 })
        {
            int i = s.Index();
            if (Ctx.GetSlot(i) == null || Ctx.IsCooldownHeld(i)) continue;
            float r = Ctx.GetCooldownRemain(i);
            if (r > remain + 0.01f) { remain = r; best = i; }
        }
        return best;
    }

    void Substitute(AbilityData.SkillDef skill)
    {
        int rs = BestRandomSlot(out float remain);
        if (rs < 0) return;

        Ctx.ResetCooldown(rs);
        Ctx.SetCooldown(Ctx.SlotOf(skill), remain);     // 대타카드가 남은 쿨을 대신 짊어짐
        Broadcast(EvSub, Ctx.transform.position + Vector3.up * 1.8f);
    }

    /// <summary>보는 방향 (수평) — 카메라 기준, 없으면 몸 기준</summary>
    Vector3 LookFlat()
    {
        var cam = Ctx.CameraRootTransform;
        Vector3 f = cam != null ? cam.forward : Ctx.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.001f) { f = Ctx.transform.forward; f.y = 0f; }
        return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
    }

    // ── 깜짝선물 ──

    void SpawnClone()
    {
        if (ClonePrefab == null) { Debug.LogWarning("[Joker] ClonePrefab이 비어 있습니다", this); return; }
        DespawnClone(false);

        Vector3 fwd = LookFlat();
        var self = Ctx.transform.root;

        float d = 2f;
        if (CastUtil.Raycast(Ctx.transform.position + Vector3.up, fwd, d, WallMask, self, out var wall))
            d = Mathf.Max(0f, wall.distance - 0.5f);

        // 분신 몸 크기만큼 올려서 — 피벗이 몸 가운데여도 발이 바닥에 (땅에 박히지 않게)
        var cloneCc = ClonePrefab.GetComponent<CharacterController>();
        Vector3 pos = SpawnPoint.GroundedPosition(Ctx.transform.position + fwd * d, cloneCc, 10f, self);

        var equip = Ctx.Equipment;
        int weaponId = equip != null ? equip.WeaponId : 0;
        var owner = Ctx.Object.InputAuthority;

        _clone = Ctx.Runner.Spawn(ClonePrefab, pos, Quaternion.LookRotation(fwd), owner,
            (runner, obj) =>
            {
                var jc = obj.GetComponent<JokerClone>();
                if (jc != null) jc.Init(owner, weaponId);
            });
        _cloneEnd = Now + Lv(CloneDuration);
    }

    void DespawnClone(bool explode)
    {
        bool had = _cloneEnd > 0f;
        _cloneEnd = 0f;

        if (CloneAlive)
        {
            if (explode) Explode(_clone.transform.position, Lv(CloneBlastMul));
            Ctx.Runner.Despawn(_clone);
        }
        _clone = null;

        if (had) ReleaseCooldown(UltSkill);     // '효과 끝나고 쿨타임'이면 지금부터
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    public override void OnRemoteEvent(int key, Vector3 pos)
    {
        switch (key)
        {
            case EvCardPlace:
                if (CardPrefab != null)
                {
                    var go = Instantiate(CardPrefab, pos + Vector3.up * 0.02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    Destroy(go, Lv(CardLifetime) + 2f);
                    _cardViews.Add(go);
                }
                break;

            case EvCardArm: Ctx.PlayEffectLocal(CardArmFx, pos); break;

            case EvCardGone:
                // 그 자리에 가장 가까운 카드 연출 제거
                int best = -1;
                float bestD = 1f;
                for (int i = 0; i < _cardViews.Count; i++)
                {
                    if (_cardViews[i] == null) continue;
                    float d = (_cardViews[i].transform.position - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) { Destroy(_cardViews[best]); _cardViews.RemoveAt(best); }
                _cardViews.RemoveAll(v => v == null);
                break;

            case EvBlast: Ctx.PlayEffectLocal(BlastFx, pos); break;
            case EvBlink: Ctx.PlayEffectLocal(BlinkFx, pos); break;
            case EvOneCard: Ctx.PlayEffectLocal(OneCardFx, pos); break;
            case EvSub: Ctx.PlayEffectLocal(SubCardFx, pos); break;
        }
    }

    // ══════════════════ 설명 ══════════════════

    public override string DescToken(string key, int rank)
    {
        switch (key)
        {
            case "cards": return _cards.Count > 0 ? $"깔린 카드 {_cards.Count}장" : null;
            case "clone":
                return CloneAlive ? $"분신 {Mathf.CeilToInt(_cloneEnd - Now)}초 — 궁극기 키로 폭발" : null;
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
                return "조커: /job cards (깔린 카드 지우기) · clone (분신) · boom (분신 폭발) · dual on|off (랜덤 2개)";
            case "cards":
            case "카드":
                {
                    int n = _cards.Count;
                    foreach (var c in _cards) Broadcast(EvCardGone, c.Pos);
                    _cards.Clear();
                    return $"카드 {n}장 지움";
                }
            case "clone":
            case "분신":
                if (CloneAlive) return "이미 분신이 있음";
                SpawnClone();
                return CloneAlive ? "분신 소환" : "분신 소환 실패 (ClonePrefab 확인)";
            case "boom":
            case "폭발":
                if (!CloneAlive) return "분신 없음";
                DespawnClone(true);
                return "분신 폭발";
            case "dual":
                bool on = a.Length < 2 || a[1] != "off";
                Ctx.SetDualRandom(on);
                return on ? "랜덤 능력 2개" : "랜덤 능력 1개";
        }
        return null;
    }

    public override IEnumerable<(string value, string label)> DevComplete(string[] a, int index)
    {
        if (index == 0) return new[] { ("cards", "깔린 카드 지우기"), ("clone", "분신 소환"), ("boom", "분신 폭발"), ("dual", "랜덤 2개 켜기/끄기") };
        if (index == 1 && a.Length > 0 && a[0] == "dual") return new[] { ("on", ""), ("off", "") };
        return null;
    }
}