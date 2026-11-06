using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class PlayerHealth : NetworkBehaviour
{
    [Header("부활")]
    [SerializeField] private float respawnDelay = 3f;
    [Tooltip("자해·환경사일 때 부활 대기 배율")]
    [SerializeField] private float selfDeathDelayMul = 2f;
    [Tooltip("이 반경 안에 살아 있는 적이 없는 부활 지점을 우선")]
    [SerializeField] private float safeSpawnRadius = 15f;

    [Header("킬 인정")]
    [Tooltip("어시스트로 인정되는 시간 (초)")]
    [SerializeField] private float assistWindow = 8f;
    [Tooltip("넉백 후 이 시간 안에 낙사하면 막타로 인정")]
    [SerializeField] private float fallCreditWindow = 5f;
    [Tooltip("이 높이(Y) 아래로 떨어지면 낙사")]
    [SerializeField] private float fallDeathY = -20f;
    /// <summary>낙사 높이 (마지막으로 생긴 플레이어 기준) — 떨어진 무기가 사라지는 높이로도 씀</summary>
    public static float FallDeathYGlobal = -20f;
    public float FallDeathY => fallDeathY;

    [Header("소울")]
    [Tooltip("처치 시 고정 획득")]
    [SerializeField] private int soulPerKill = 20;
    [Tooltip("어시스트 시 고정 획득")]
    [SerializeField] private int soulPerAssist = 5;
    [Tooltip("플레이어에게 죽었을 때 잃는 비율 (처치자에게 가지 않고 사라짐). 0 = 안 잃음")]
    [Range(0f, 1f)][SerializeField] private float deathLossByPlayer = 0.1f;
    [Tooltip("낙사 등 환경사로 잃는 비율")]
    [Range(0f, 1f)][SerializeField] private float deathLossByEnv = 0.1f;

    [Header("전투 · 회복")]
    [Tooltip("피해를 주고받은 뒤 전투 중으로 유지되는 시간")]
    [SerializeField] private float combatDuration = 8f;
    [Tooltip("비전투 중 매초 최대 체력의 % 회복")]
    [SerializeField] private float regenPercent = 1f;
    [Tooltip("비전투 중 매초 고정 회복")]
    [SerializeField] private int regenFlat = 2;

    [Header("헤드샷")]
    [Tooltip("키(캐릭터 컨트롤러 높이) 중 위쪽 이 비율이 머리 — 이 높이 위에 맞으면 헤드샷 (총 · 투사체 무기)")]
    [Range(0.05f, 0.5f)][SerializeField] private float headFraction = 0.2f;
    [Tooltip("머리 높이 최소 (m) — 작은 몸에서도 이만큼은 머리")]
    [SerializeField] private float headMinHeight = 0.3f;

    /// <summary>이 높이(월드 Y) 위에 맞으면 머리 — 캐릭터 컨트롤러 꼭대기 기준 (탈것 변화로 키가 늘면 같이)</summary>
    public float HeadBottomY
    {
        get
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            if (_cc == null) return transform.position.y + 1.5f;
            float sy = Mathf.Abs(transform.lossyScale.y);
            float h = _cc.height * sy;
            float top = transform.position.y + (_cc.center.y + _cc.height * 0.5f) * sy;
            return top - Mathf.Max(headMinHeight, h * headFraction);
        }
    }

    [Networked] private TickTimer CombatTimer { get; set; }
    public bool InCombat => CombatTimer.IsRunning && !CombatTimer.Expired(Runner);

    [Networked, OnChangedRender(nameof(OnHPChanged))] public int HP { get; set; }
    [Networked, OnChangedRender(nameof(OnDeadChanged))] public NetworkBool IsDead { get; set; }
    [Networked] public int Soul { get; set; }

    [Networked] public int Kills { get; set; }
    [Networked] public int Assists { get; set; }
    [Networked] public int Deaths { get; set; }

    [Networked] private TickTimer RespawnTimer { get; set; }
    [Networked] private Vector3 LastHitFrom { get; set; }

    private HitVfx _hitVfx;

    struct DamageRecord
    {
        public PlayerRef Player;
        public int Amount;
        public float Time;
        public bool CanCauseFall;
    }

    private readonly List<DamageRecord> _recentDamage = new();
    private readonly List<Renderer> _hiddenOnDeath = new();

    private int _prevHP;
    private float _regenTimer;
    private PlayerStats _stats;
    private CharacterController _cc;
    private AbilityRuntime _ability;
    private PlayerStatus _status;



    [Header("피격 번쩍임 — 맞은 부위만 (HitFlash)")]
    [Tooltip("Both = 재질 색을 실제로 바꿈 + 테두리 덮개 (기본) · Tint = 색만 · Overlay = 덮개만")]
    [SerializeField] private HitFlash.FlashStyle flashStyle = HitFlash.FlashStyle.Both;
    [Tooltip("재질 색을 번쩍임 색으로 얼마나 바꿀지 (1 = 완전히)")]
    [Range(0f, 1f)][SerializeField] private float flashTint = 0.75f;
    [Tooltip("Both일 때 덮개 진하기 (색 변화와 겹쳐 과하지 않게)")]
    [Range(0f, 1f)][SerializeField] private float flashOverlayInBoth = 0.55f;
    [Tooltip("몸 피격 — 옅은 빨강 (흰색은 밝은 피부 · 밝은 면에서 거의 안 보여서)")]
    [SerializeField] private Color flashBody = new(1f, 0.42f, 0.38f, 0.8f);
    [Tooltip("치명타 — 데미지 숫자와 같은 노랑")]
    [SerializeField] private Color flashCrit = new(1f, 0.86f, 0.3f, 0.9f);
    [Tooltip("헤드샷 — 머리만 진한 빨강 · 조금 더 길게 (데미지 숫자와 같은 색)")]
    [SerializeField] private Color flashHead = new(1f, 0.12f, 0.08f, 1f);
    [Tooltip("쉴드가 다 막음 — 하늘색 (쉴드 줄과 같은 색)")]
    [SerializeField] private Color flashShield = new(0.55f, 0.82f, 1f, 0.8f);
    [Tooltip("화상 · 중독 틱 — 옅게")]
    [SerializeField] private Color flashBurn = new(1f, 0.55f, 0.2f, 0.45f);
    [SerializeField] private Color flashPoison = new(0.5f, 0.95f, 0.4f, 0.45f);
    [Tooltip("번쩍이는 시간 (초)")]
    [SerializeField] private float flashTime = 0.14f;
    private HitFlash _flash;

    /// <summary>소울 획득 배율 (조커 환락 1.25) — 처치·어시스트·획득 효과 모두. SA 전용</summary>
    public float SoulGainMul { get; set; } = 1f;
    private float _soulFrac;

    /// <summary>획득량에 배율 적용 — 소수점은 모아 뒀다가 1이 되면 줌</summary>
    int ScaledSoul(int amount)
    {
        if (amount <= 0 || Mathf.Approximately(SoulGainMul, 1f)) return amount;
        float v = amount * SoulGainMul + _soulFrac;
        int n = Mathf.FloorToInt(v);
        _soulFrac = v - n;
        return n;
    }

    /// <summary>피해를 받았을 때 (SA). 능력이 반응용으로 구독 — (정보, 최종 피해)</summary>
    public event System.Action<DamageInfo, int> Damaged;

    /// <summary>죽었을 때 (SA, 사망 처리가 다 끝난 뒤) — (처치자 · 없으면 None, 원인). 사망회귀 · 복수 · 갈땐 가더라도</summary>
    public event System.Action<PlayerRef, DeathCause> Died;

    /// <summary>내가 누군가를 처치했을 때 (SA) — 피해자 (더미면 None일 수 있음)</summary>
    public event System.Action<PlayerRef> KilledPlayer;

    /// <summary>처치 관여(어시스트)했을 때 (SA) — 체스의 거신병 폰</summary>
    public event System.Action Assisted;

    public int SoulPerKill => soulPerKill;
    public int SoulPerAssist => soulPerAssist;

    /// <summary>피해자별 마지막 처치로 얻은 소울 (사망회귀가 되돌릴 때)</summary>
    private readonly Dictionary<PlayerRef, int> _lastKillGain = new();

    public override void Spawned()
    {
        FallDeathYGlobal = fallDeathY;
        _stats = GetComponent<PlayerStats>();
        _cc = GetComponent<CharacterController>();
        _ability = GetComponent<AbilityRuntime>();
        _status = GetComponent<PlayerStatus>();
        _hitVfx = GetComponent<HitVfx>();
        _flash = HitFlash.For(gameObject);
        ApplyFlashStyle(_flash);

        if (HasStateAuthority)
        {
            HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
            Soul = RoomRules.StartSoul(0);
            IsDead = false;
            _stats.OnStatsChanged += ClampHP;
            ResetShieldsOnSpawn();
        }
        _prevHP = HP;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (HasInputAuthority) UIManager.Instance?.UnbindLocalPlayer();
    }

    void ClampHP()
    {
        if (!HasStateAuthority || IsDead) return;

        int max = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        if (HP > max) HP = max;
    }

    const byte FxBody = 0, FxHead = 1, FxShield = 2;

    /// <param name="part">0 몸 · 1 머리(헤드샷) · 2 쉴드가 다 막음</param>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_HitFx(Vector3 from, NetworkBool crit, byte part)
    {
        if (_hitVfx != null && part != FxShield) _hitVfx.Play(from, crit);
        if (_flash == null) return;

        switch (part)
        {
            case FxHead: _flash.Play(HitFlash.Part.Head, flashHead, flashTime * 1.3f); break;
            case FxShield: _flash.Play(HitFlash.Part.All, flashShield, flashTime); break;
            default: _flash.Play(HitFlash.Part.Body, crit ? flashCrit : flashBody, crit ? flashTime * 1.2f : flashTime); break;
        }
    }

    /// <summary>다른 몸(조커 분신 등)을 이 사람과 같은 색으로 번쩍 — 분신인 걸 티 안 나게</summary>
    void ApplyFlashStyle(HitFlash f)
    {
        if (f == null) return;
        f.Style = flashStyle;
        f.TintAmount = flashTint;
        f.OverlayInBoth = flashOverlayInBoth;
    }

    public void FlashOther(HitFlash f, bool crit, bool head)
    {
        if (f == null) return;
        ApplyFlashStyle(f);
        if (head) f.Play(HitFlash.Part.Head, flashHead, flashTime * 1.3f);
        else f.Play(HitFlash.Part.Body, crit ? flashCrit : flashBody, crit ? flashTime * 1.2f : flashTime);
    }

    /// <summary>화상 · 중독 틱 — 몸 전체 옅게</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_TickFlash(int status)
    {
        if (_flash == null) return;
        var c = status == (int)StatusType.Poison ? flashPoison : flashBurn;
        _flash.Play(HitFlash.Part.All, c, flashTime);
    }

    // ══════════════════ 라운드 ══════════════════

    /// <summary>RoundManager가 호출. 각자 자기 권한으로 처음 상태로</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ResetForNewRound()
    {
        // 순서 중요: 상태이상 → 스탯 비우기 → 각 시스템이 처음 상태로 다시 걸기 → HP·위치는 마지막
        _status?.ClearAll();
        _stats.ClearAll();

        GetComponent<PlayerEquipment>()?.ResetForNewRound();
        GetComponent<PlayerEnchant>()?.ResetForNewRound();
        GetComponent<PlayerShop>()?.ResetForNewRound();
        GetComponent<PlayerAlter>()?.ResetForNewRound();
        _ability?.ResetForNewRound();

        ResetForNewRound();

        Soul = RoomRules.StartSoul(0);
    }

    public void ResetForNewRound()
    {
        if (!HasStateAuthority) return;

        Soul = 0;
        Kills = 0;
        Assists = 0;
        Deaths = 0;
        CombatTimer = default;
        _soulFrac = 0f;
        _recentDamage.Clear();
        _lastKillGain.Clear();

        HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        IsDead = false;
        ResetShieldsOnSpawn();

        MoveToSpawn();
    }

    // ══════════════════ 진행 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (IsDead && RespawnTimer.Expired(Runner)) Respawn();

        float now = Runner.SimulationTime;
        for (int i = _recentDamage.Count - 1; i >= 0; i--)
            if (now - _recentDamage[i].Time > assistWindow)
                _recentDamage.RemoveAt(i);

        // 낙사 — 방 규칙이 '부활 지점으로'면 죽지 않고 옮기기만
        // (예전엔 이 규칙일 때 높이와 상관없이 매 틱 순간이동하고 회복도 건너뛰었음)
        if (!IsDead && transform.position.y < fallDeathY)
        {
            if (RoomRules.FallKills) Die(DeathCause.Environment, PlayerRef.None);
            else if (SpawnPoint.TryPick(Runner, Object.StateAuthority, safeSpawnRadius, out var p, out var r, _cc)) Teleport(p, r);
            else MoveToSpawn();
        }

        TickRegen();
        TickShield();
    }

    /// <summary>비전투 중 매초 최대 체력 N% + 고정 회복</summary>
    void TickRegen()
    {
        if (IsDead || InCombat || (regenPercent <= 0f && regenFlat <= 0))
        {
            _regenTimer = 0f;
            return;
        }

        _regenTimer += Runner.DeltaTime;
        if (_regenTimer < 1f) return;
        _regenTimer -= 1f;

        float max = _stats.Get(StatType.MaxHP);
        if (HP >= Mathf.RoundToInt(max)) return;

        HealInternal(Mathf.RoundToInt(max * regenPercent * 0.01f) + regenFlat);
    }

    // ══════════════════ 피해 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_MarkCombat() => MarkCombat();

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(DamageInfo info)
    {
        if (IsDead) return;

        // 회피 — 상태이상 틱만 통과
        if (_status != null && (_status.IsEvading || _status.Has(StatusType.Ghost)) && !info.IsStatusTick)
        {
            NotifyNumber(info, 0, DamageNumberKind.Miss);
            return;
        }

        // 규칙 배율 — 약화(애교부리기 · 투명 망토) · 암살자 등 (DamageRules)
        var atkNo = info.Attacker != PlayerRef.None ? Runner.GetPlayerObject(info.Attacker) : null;
        float ruleMul = DamageRules.Multiplier(this, atkNo, info);

        float maxHp = _stats.Get(StatType.MaxHP);
        float raw = info.Amount;

        // 최대 체력 % 피해 — 화상 · 중독 틱, 인챈트 등. 이 줄이 빠져 있으면 틱이 전부 1 피해가 됨
        if (info.MaxHpPercent > 0f)
            raw += maxHp * info.MaxHpPercent * 0.01f;

        if (info.MissingHpPercent > 0f)
            raw += Mathf.Max(0f, maxHp - HP) * info.MissingHpPercent * 0.01f;

        // 처형 — 맞기 직전 체력 기준
        if (info.ExecuteBelow > 0f && HP <= maxHp * info.ExecuteBelow)
            raw *= 1f + info.ExecuteBonus;

        // 헤드샷 — 투구(헤드샷 감소)를 넣고 곱함. 몸샷보다 덜 들어가진 않음
        if (info.HeadshotMul > 1f)
            raw *= Headshot.Incoming(info.HeadshotMul, _stats);
        else
            raw *= Headshot.BodyArmor(info.HeadshotMul, _stats);      // 갑옷 — 받는 피해 N% 감소 (헤드샷 제외)

        raw *= ruleMul;

        int final = info.IgnoreDefense
            ? Mathf.Max(1, Mathf.RoundToInt(raw))
            : DamageCalc.CalcIncoming(_stats, raw, info.DefensePen);
        if (ruleMul <= 0f) final = 0;           // 피해 0% (최소 1 피해 규칙 무시)

        // 쉴드 — 체력보다 먼저 (소모품 → 재생 → 소울)
        int absorbed = AbsorbShield(ref final, info);

        // 언데드 — 체력이 1 아래로 안 떨어짐
        if (_status != null && _status.Has(StatusType.Undead) && HP - final < 1)
            final = Mathf.Max(0, HP - 1);

        LastHitFrom = info.SourcePos;
        HP -= final;

        // 숫자는 막은 양까지 합쳐서 — 전부 쉴드로 막았으면 쉴드 색
        NotifyNumber(info, final + absorbed, absorbed > 0 && final <= 0 ? DamageNumberKind.Shield : KindOf(info));
        Damaged?.Invoke(info, final);

        if (!info.IsStatusTick && !info.IsDot)
            RPC_HitFx(info.SourcePos, info.IsCrit, absorbed > 0 && final <= 0 ? FxShield : info.IsHeadshot ? FxHead : FxBody);
        else if (info.IsStatusTick && final + absorbed > 0)
            RPC_TickFlash(info.TickStatus);

        if (info.FromPlayer && info.Attacker != PlayerRef.None)
        {
            RecordDamage(info.Attacker, final, info.CanCauseFall);
            MarkCombat();

            var atk = Runner.GetPlayerObject(info.Attacker);
            if (atk != null && atk.TryGetComponent<PlayerHealth>(out var ah))
                ah.RPC_MarkCombat();
        }

        if (!info.IsDot)
        {
            var atkObj = info.Attacker != PlayerRef.None ? Runner.GetPlayerObject(info.Attacker) : null;
            _ability?.FireTrigger(TriggerType.OnHitTaken, atkObj != null ? atkObj.gameObject : null);
        }

        if (HP <= 0)
        {
            HP = 0;
            Die(info.FromPlayer ? DeathCause.Player : DeathCause.Environment,
                info.Attacker, info.NoDeathPenalty);
        }
    }

    /// <summary>상태이상 지속 피해 — 최대 체력 N% 고정 피해. 건 사람에게 킬 인정</summary>
    public void TakeStatusDamage(float maxHpPercent, PlayerRef source, StatusType type)
    {
        if (!HasStateAuthority || IsDead) return;

        var info = source != PlayerRef.None && source != Object.InputAuthority
            ? DamageInfo.FromPlayerHit(0, source, transform.position)
            : DamageInfo.FromEnvironment(0, transform.position);

        info.MaxHpPercent = maxHpPercent;
        info.IgnoreDefense = true;
        info.IsDot = true;
        info.IsStatusTick = true;
        info.TickStatus = (int)type;

        RPC_TakeDamage(info);
    }

    void RecordDamage(PlayerRef attacker, int amount, bool canCauseFall)
    {
        float now = Runner.SimulationTime;

        for (int i = 0; i < _recentDamage.Count; i++)
        {
            if (_recentDamage[i].Player != attacker) continue;

            var r = _recentDamage[i];
            r.Amount += amount;
            r.Time = now;
            r.CanCauseFall = canCauseFall || r.CanCauseFall;
            _recentDamage[i] = r;
            return;
        }

        _recentDamage.Add(new DamageRecord
        {
            Player = attacker,
            Amount = amount,
            Time = now,
            CanCauseFall = canCauseFall,
        });
    }

    // ══════════════════ 데미지 숫자 ══════════════════

    /// <summary>최종 피해를 공격자 한 명에게만 알려 데미지 숫자를 띄움</summary>
    void NotifyNumber(in DamageInfo info, int amount, DamageNumberKind kind)
    {
        if (!info.FromPlayer || info.Attacker == PlayerRef.None) return;
        if (info.Attacker == Object.InputAuthority) return;

        RPC_DamageNumber(info.Attacker, amount, transform.position, (int)kind);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_DamageNumber([RpcTarget] PlayerRef target, int amount, Vector3 pos, int kind)
    {
        DamageParticleManager.Instance?.Show(pos, amount, (DamageNumberKind)kind);
    }

    public static DamageNumberKind KindOf(in DamageInfo info)
    {
        if (info.IsStatusTick)
            return info.TickStatus == (int)StatusType.Poison ? DamageNumberKind.Poison : DamageNumberKind.Burn;
        if (info.IgnoreDefense) return DamageNumberKind.True;
        if (info.IsHeadshot) return DamageNumberKind.Headshot;
        if (info.IsCrit) return DamageNumberKind.Crit;
        return DamageNumberKind.Normal;
    }

    // ══════════════════ 사망 ══════════════════

    /// <param name="noPenalty">소울을 잃지 않음 (대인기피증 자폭 등 의도치 않은 사망)</param>
    /// <param name="noCredit">낙사 막타 인정도 안 함 (데스 노트 등 자살 판정)</param>
    void Die(DeathCause cause, PlayerRef killer, bool noPenalty = false, bool noCredit = false)
    {
        if (IsDead) return;

        IsDead = true;
        Deaths++;
        _status?.ClearAll();

        // 환경사인데 최근 넉백을 맞았으면 그 사람에게 킬 인정
        if (cause == DeathCause.Environment && !noCredit)
        {
            var faller = FindFallCredit();
            if (faller != PlayerRef.None)
            {
                cause = DeathCause.Player;
                killer = faller;
            }
        }

        bool byPlayer = cause == DeathCause.Player
                        && killer != PlayerRef.None
                        && killer != Object.InputAuthority;

        if (!noPenalty)
        {
            float ratio = byPlayer ? deathLossByPlayer : deathLossByEnv;
            Soul -= Mathf.FloorToInt(Soul * ratio);
        }

        if (byPlayer)
        {
            GrantKill(killer);
            GrantAssists(killer);
        }

        float delay = byPlayer || noPenalty ? respawnDelay : respawnDelay * selfDeathDelayMul;
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, delay);

        CombatTimer = default;

        _ability?.RerollRandomAbility();
        _ability?.FireTrigger(TriggerType.OnDeath, null);
        _recentDamage.Clear();

        Died?.Invoke(byPlayer ? killer : PlayerRef.None, cause);
    }

    /// <summary>
    /// 즉사 — 회피 · 고스트 · 언데드 무시 (데스 노트 · 필연 · 갈땐 가더라도).
    /// killer가 있으면 그 사람의 처치 (어시스트 포함), asSuicide면 아무도 소울 못 받음
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ForceKill(PlayerRef killer, NetworkBool asSuicide)
    {
        if (IsDead) return;
        HP = 0;
        if (!asSuicide && killer != PlayerRef.None) Die(DeathCause.Player, killer);
        else Die(DeathCause.Suicide, PlayerRef.None, false, true);
    }

    /// <summary>즉사 — 소울을 잃지 않음 · 아무도 처치 인정 안 함 (미래로 가자 시전자 · 모두 동지 등)</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Sacrifice()
    {
        if (IsDead) return;
        HP = 0;
        Die(DeathCause.Suicide, PlayerRef.None, true, true);
    }

    /// <summary>그 자리에서 즉시 부활 (사망회귀) — 위치 그대로, 체력 가득</summary>
    public void ReviveHere()
    {
        if (!HasStateAuthority || !IsDead) return;

        RespawnTimer = default;
        HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        IsDead = false;
        ResetShieldsOnSpawn();
        if (_cc != null) _cc.enabled = true;

        _ability?.FireTrigger(TriggerType.OnRespawn, null);
    }

    /// <summary>남이 이 사람을 옮김 (시공의 폭풍) — 이 사람 쪽 권한자에서 순간이동</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TeleportTo(Vector3 pos)
    {
        if (IsDead) return;
        Teleport(pos, transform.rotation);
    }

    PlayerRef FindFallCredit()
    {
        float now = Runner.SimulationTime;
        PlayerRef best = PlayerRef.None;
        float bestTime = -1f;

        foreach (var r in _recentDamage)
        {
            if (!r.CanCauseFall) continue;
            if (now - r.Time > fallCreditWindow) continue;
            if (r.Time > bestTime) { bestTime = r.Time; best = r.Player; }
        }

        return best;
    }

    void GrantKill(PlayerRef killer)
    {
        var obj = Runner.GetPlayerObject(killer);
        if (obj == null || !obj.TryGetComponent<PlayerHealth>(out var kh)) return;
        kh.RPC_AddKillOf(soulPerKill, Object.InputAuthority);
    }

    void GrantAssists(PlayerRef killer)
    {
        float now = Runner.SimulationTime;

        foreach (var r in _recentDamage)
        {
            if (r.Player == killer) continue;
            if (r.Player == Object.InputAuthority) continue;
            if (now - r.Time > assistWindow) continue;

            var obj = Runner.GetPlayerObject(r.Player);
            if (obj == null || !obj.TryGetComponent<PlayerHealth>(out var ah)) continue;

            ah.RPC_AddAssist(soulPerAssist);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddKill(int soul) => AddKillLocal(soul);

    /// <summary>처치 — 피해자를 알 때 (복수 · 사망회귀가 씀)</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddKillOf(int soul, PlayerRef victim)
    {
        int before = Soul;
        AddKillLocal(soul);
        _lastKillGain[victim] = Mathf.Max(0, Soul - before);
        KilledPlayer?.Invoke(victim);
    }

    /// <summary>사망회귀 — 그 피해자를 처치해서 얻은 소울을 도로 뺏음</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RevokeKillGain(PlayerRef victim)
    {
        if (!_lastKillGain.TryGetValue(victim, out int gain)) return;
        _lastKillGain.Remove(victim);
        Soul = Mathf.Max(0, Soul - gain);
    }

    void AddKillLocal(int soul)
    {
        Kills++;
        Soul += ScaledSoul(soul);

        _ability?.FireTrigger(TriggerType.OnKill, null);
        _ability?.OnPlayerKill();

        var en = GetComponent<PlayerEnchant>();
        en?.AddKillToActive();
        en?.RPC_AddStones(1);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddAssist(int soul)
    {
        Assists++;
        Soul += ScaledSoul(soul);
        Assisted?.Invoke();
    }

    // ══════════════════ 회복 · 소울 ══════════════════

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Heal(int amount) => HealInternal(amount);

    /// <summary>회복 — 깊은 상처 반영</summary>
    void HealInternal(int amount)
    {
        if (!HasStateAuthority || IsDead || amount <= 0) return;

        int healed = Mathf.RoundToInt(amount * (_status != null ? _status.HealMultiplier : 1f));
        if (healed <= 0) return;

        HP = Mathf.Min(HP + healed, Mathf.RoundToInt(_stats.Get(StatType.MaxHP)));
    }

    /// <summary>
    /// 최대 체력이 오른 만큼 현재 체력도 올림.
    /// 상점·인챈트·스킬 레벨업에서 호출 (장비 교체는 번갈아 끼우기 악용 때문에 X)
    /// </summary>
    public void ApplyMaxHpGain(float maxBefore)
    {
        if (!HasStateAuthority || IsDead) return;

        float after = _stats.Get(StatType.MaxHP);
        int gain = Mathf.RoundToInt(after - maxBefore);
        if (gain > 0) HP = Mathf.Min(HP + gain, Mathf.RoundToInt(after));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_GainSoul(int amount) => Soul = Mathf.Max(0, Soul + (amount > 0 ? ScaledSoul(amount) : amount));

    public void SpendSoul(int amount)
    {
        if (!HasStateAuthority) return;
        Soul = Mathf.Max(0, Soul - amount);
    }

    public void MarkCombat()
    {
        if (!HasStateAuthority) return;
        CombatTimer = TickTimer.CreateFromSeconds(Runner, combatDuration);
    }

    // ══════════════════ 부활 · 이동 ══════════════════

    void Respawn()
    {
        HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        IsDead = false;
        ResetShieldsOnSpawn();

        MoveToSpawn();

        _ability?.FireTrigger(TriggerType.OnRespawn, null);
    }

    /// <summary>적이 없는 부활 지점 중 랜덤으로 이동. 지점이 없으면 원점 근처</summary>
    public void MoveToSpawn()
    {
        if (!HasStateAuthority) return;

        if (SpawnPoint.TryPick(Runner, Object.InputAuthority, safeSpawnRadius, out var pos, out var rot, _cc))
            Teleport(pos, rot);
        else
        {
            var p = new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
            Teleport(SpawnPoint.GroundedPosition(p, _cc), transform.rotation);
        }
    }

    /// <summary>순간이동 — 다른 클라이언트에서 미끄러지듯 보간되지 않게</summary>
    /// <summary>순간이동 직후 (이 화면) — 카메라 등이 부드럽게 따라가지 말고 바로 붙게</summary>
    public event System.Action Teleported;

    public void Teleport(Vector3 pos, Quaternion rot)
    {
        if (_cc != null) _cc.enabled = false;

        transform.SetPositionAndRotation(pos, rot);

        var nt = GetComponent<NetworkTransform>();
        if (nt != null)
        {
            nt.Teleport(pos, rot);
            // 내 화면: 옛 위치 ↔ 새 위치 사이가 보간돼 1인칭 카메라가 튀지 않게 잠깐 보간 끔
            if (HasStateAuthority) TeleportInterpolationFix.Apply(gameObject, nt, Runner);
        }

        if (_cc != null) _cc.enabled = !IsDead;
        Physics.SyncTransforms();           // 같은 틱에 이어지는 이동 · 판정이 옛 위치를 쓰지 않게

        Teleported?.Invoke();
    }

    // ══════════════════ 연출 (모든 클라이언트) ══════════════════

    void OnHPChanged()
    {
        int delta = _prevHP - HP;
        _prevHP = HP;

        if (delta <= 0) return;
        // 번쩍임은 RPC_HitFx · RPC_TickFlash (부위 · 종류별)

        if (!HasStateAuthority) return;

        var cam = GetComponentInChildren<FPSCamera>();
        if (cam == null) return;

        float intensity = Mathf.Clamp01(delta / 50f);

        Vector3 toAttacker = (LastHitFrom - transform.position).normalized;
        Vector3 local = transform.InverseTransformDirection(toAttacker);
        cam.HitReact(intensity, new Vector2(-local.x, local.z));
    }

    /// <summary>
    /// 사망 — 몸에 달린 렌더러 전부(무기·팔 포함) 끄고, 부활하면 끈 것만 다시 켬
    /// (원래 꺼져 있던 1인칭 몸 메시 등은 그대로)
    /// </summary>
    void OnDeadChanged()
    {
        if (IsDead)
        {
            _hiddenOnDeath.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.enabled)
                {
                    r.enabled = false;
                    _hiddenOnDeath.Add(r);
                }
        }
        else
        {
            foreach (var r in _hiddenOnDeath)
                if (r != null) r.enabled = true;
            _hiddenOnDeath.Clear();
        }

        if (_cc != null) _cc.enabled = !IsDead;
    }

}