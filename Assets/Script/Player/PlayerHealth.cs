using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
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



    [Header("Hit Flash")]
    [Tooltip("셰이더의 색 프로퍼티 이름. 비우면 자동 탐색")]
    [SerializeField] private string colorProperty = "";
    private Renderer _renderer;
    private int _colorId = -1;
    private Color _baseColor = Color.white;

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

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _cc = GetComponent<CharacterController>();
        _ability = GetComponent<AbilityRuntime>();
        _status = GetComponent<PlayerStatus>();
        _renderer = GetComponentInChildren<Renderer>();
        _hitVfx = GetComponent<HitVfx>();
        SetupColorProperty();

        if (HasStateAuthority)
        {
            HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
            Soul = 0;
            IsDead = false;
            _stats.OnStatsChanged += ClampHP;
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

    void SetupColorProperty()
    {
        if (_renderer == null) return;

        var mat = _renderer.sharedMaterial;
        if (mat == null) return;

        string[] candidates = string.IsNullOrEmpty(colorProperty)
            ? new[] { "_BaseColor", "_Color", "_MainColor", "_Tint", "_ColorTint" }
            : new[] { colorProperty };

        foreach (var name in candidates)
        {
            if (!mat.HasProperty(name)) continue;
            _colorId = Shader.PropertyToID(name);
            _baseColor = mat.GetColor(_colorId);
            return;
        }

        Debug.LogWarning($"[PlayerHealth] {mat.shader.name}에서 색 프로퍼티를 못 찾았습니다. 피격 플래시 비활성.", this);
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_HitFx(Vector3 from, NetworkBool crit)
    {
        if (_hitVfx != null) _hitVfx.Play(from, crit);
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

        HP = Mathf.RoundToInt(_stats.Get(StatType.MaxHP));
        IsDead = false;

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

        if (!IsDead && transform.position.y < -20f)
            Die(DeathCause.Environment, PlayerRef.None);

        TickRegen();
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

        float maxHp = _stats.Get(StatType.MaxHP);
        float raw = info.Amount;

        if (info.MissingHpPercent > 0f)
            raw += Mathf.Max(0f, maxHp - HP) * info.MissingHpPercent * 0.01f;

        // 처형 — 맞기 직전 체력 기준
        if (info.ExecuteBelow > 0f && HP <= maxHp * info.ExecuteBelow)
            raw *= 1f + info.ExecuteBonus;

        int final = info.IgnoreDefense
            ? Mathf.Max(1, Mathf.RoundToInt(raw))
            : DamageCalc.CalcIncoming(_stats, raw, info.DefensePen);

        LastHitFrom = info.SourcePos;
        HP -= final;

        NotifyNumber(info, final, KindOf(info));
        Damaged?.Invoke(info, final);

        if (!info.IsStatusTick && !info.IsDot) RPC_HitFx(info.SourcePos, info.IsCrit);

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

    static DamageNumberKind KindOf(in DamageInfo info)
    {
        if (info.IsStatusTick)
            return info.TickStatus == (int)StatusType.Poison ? DamageNumberKind.Poison : DamageNumberKind.Burn;
        if (info.IgnoreDefense) return DamageNumberKind.True;
        if (info.IsCrit) return DamageNumberKind.Crit;
        return DamageNumberKind.Normal;
    }

    // ══════════════════ 사망 ══════════════════

    /// <param name="noPenalty">소울을 잃지 않음 (대인기피증 자폭 등 의도치 않은 사망)</param>
    void Die(DeathCause cause, PlayerRef killer, bool noPenalty = false)
    {
        if (IsDead) return;

        IsDead = true;
        Deaths++;
        _status?.ClearAll();

        // 환경사인데 최근 넉백을 맞았으면 그 사람에게 킬 인정
        if (cause == DeathCause.Environment)
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
        kh.RPC_AddKill(soulPerKill);
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
    public void RPC_AddKill(int soul)
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

        StopAllCoroutines();
        StartCoroutine(FlashRed());

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

    System.Collections.IEnumerator FlashRed()
    {
        if (_renderer == null || _colorId < 0) yield break;

        _renderer.material.SetColor(_colorId, Color.red);
        yield return new WaitForSeconds(0.12f);
        if (_renderer != null) _renderer.material.SetColor(_colorId, _baseColor);
    }
}