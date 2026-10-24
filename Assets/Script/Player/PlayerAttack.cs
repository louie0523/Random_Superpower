using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private WeaponAnimator weaponAnim;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private LayerMask projectileMask;
    [Tooltip("도탄(히트스캔이 튕김)이 일어나는 표면 레이어 — 여기 없는 레이어(바닥 등)에 맞으면 그냥 멈춤.\n" +
             "Projectile Mask 안에서만 의미 있음 (애초에 맞는 레이어여야 튕길 수 있음)")]
    [SerializeField] private LayerMask ricochetMask = ~0;
    [SerializeField] private AudioSource weaponAudio;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logHits = false;



    // 컴포넌트
    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerEquipment _equip;
    private AbilityRuntime _ability;
    private PlayerAnimator _anim;
    private CharacterController _cc;
    private MotionEventPlayer _events;
    private PlayerShop _shop;
    private PlayerStatus _status;
    private PlayerEnchant _enchant;
    private FPArmRig _fpRig;
    private PlayerMovement _moveRef;

    /// <summary>변신 중 — 원거리 무기를 들고 있어도 좌클릭은 근접(형태의 공격)으로</summary>
    private bool _forceMelee;
    private PlayerAlter _alterRef;
    public bool AttackRooted =>
    IsAttacking && _overrideSource == AbilityRuntime.AlterAttackSource
    && AlterRef != null && AlterRef.ActiveAttack != null && AlterRef.ActiveAttack.RootDuringAttack;
    PlayerAlter AlterRef => _alterRef != null ? _alterRef : (_alterRef = GetComponent<PlayerAlter>());

    /// <summary>변신 중 — 원거리 무기를 들고 있어도 좌클릭은 근접(형태의 공격)으로. 변화에 공격 모션이 있어도 자동</summary>
    public bool ForceMelee
    {
        get => _forceMelee || (AlterRef != null && AlterRef.ActiveAttack != null);
        set => _forceMelee = value;
    }
    /// <summary>능력이 잠시 공격을 막음 (거신병 도약 중 등)</summary>
    public bool Locked { get; set; }
    private float _alterAtkReady;


    private float _atkSpd = 1f;         // 이번 공격의 공속 (시작 시점 고정)
    private int _overrideSource = -1;   // 공격을 대체한 Behaviour 번호. -1 = 일반 공격

    // 콤보 상태
    private int _comboIndex;
    private float _attackTimer = -1f;
    private float _comboExpire;
    private bool _queuedInput;
    private bool _attackQueued;
    private bool _hitApplied;
    private ComboStep _current;

    // 총기 상태
    private float _fireTimer;
    private int _shotsInBurst;      // 연사 반동 누적용
    private float _burstResetTimer;

    // ── 총기 보정 — 능력이 설정 (SA) ──
    /// <summary>탄퍼짐 배율 (무기 SpreadAngle에 곱함)</summary>
    public float SpreadMul { get; set; } = 1f;
    /// <summary>탄퍼짐 추가 각도 (°)</summary>
    public float SpreadAdd { get; set; }
    /// <summary>히트스캔이 벽에 튕기는 횟수</summary>
    public int Ricochets { get; set; }
    /// <summary>튕긴 탄이 근처 적에게 꺾이는 거리 (m) — 0이면 그냥 거울 반사</summary>
    public float RicochetSeekRange { get; set; } = 15f;
    /// <summary>튕긴 방향 기준 이 각도(°, 전체 폭) 안의 적만 노림</summary>
    public float RicochetSeekAngle { get; set; } = 90f;
    private readonly Collider[] _seekHits = new Collider[32];

    // ── 근접 판정 보정 — 능력이 설정 (SA) ──
    /// <summary>근접 판정 사거리 배율 (죽음의 기사 +50% → 1.5). 공격 시작 시점 값으로 고정되고, 이펙트 크기도 따라감</summary>
    public float HitRangeMul { get; set; } = 1f;
    private float _atkRangeMul = 1f;    // 이번 공격의 사거리 배율 (시작 시점 고정)

    // 다음 한 발 강화 (정밀사격 등)
    private float _armedBonusPct;
    private bool _armedCrit;
    private float _armedTimer = -1f;

    public bool HasArmedShot => _armedTimer > 0f;

    /// <summary>총을 쏠 때마다 (SA). 능력이 반응용으로 구독</summary>
    public event System.Action<EquipmentData> GunFired;

    /// <summary>무기로 맞힐 때마다 (SA) — (대상, 피해, 인챈트 적용 여부). 능력이 반응용으로 구독</summary>
    public event System.Action<PlayerHealth, float, bool> WeaponHit;

    /// <summary>다음 총알 한 발 — 추가 피해 %, 반드시 치명타. timeout 초 안에 안 쏘면 사라짐</summary>
    public void ArmNextShot(float bonusPct, bool forceCrit, float timeout)
    {
        _armedBonusPct = bonusPct;
        _armedCrit = forceCrit;
        _armedTimer = Mathf.Max(0.05f, timeout);
    }

    public void DisarmShot() => _armedTimer = -1f;

    /// <summary>
    /// 이 무기에 적용되는 공속. 무기의 AttackSpeedScale만큼만 반영
    /// 예) 공속 1.4, 반영 0.5 → 1 + 0.4 × 0.5 = 1.2
    /// </summary>
    float WeaponAtkSpd(EquipmentData weapon)
    {
        float raw = Mathf.Max(0.1f, _stats.Get(StatType.AttackSpeed));
        float scale = weapon != null ? weapon.AttackSpeedScale : 1f;
        return Mathf.Max(0.1f, 1f + (raw - 1f) * scale);
    }

    bool ConsumeArmedShot(out float bonusMul, out bool forceCrit)
    {
        bonusMul = 1f;
        forceCrit = false;
        if (_armedTimer <= 0f) return false;

        bonusMul = 1f + _armedBonusPct * 0.01f;
        forceCrit = _armedCrit;
        _armedTimer = -1f;
        return true;
    }

    /// <summary>CalcOutgoing + 치명타 강제</summary>
    float Outgoing(float baseDmg, DamageSource src, bool forceCrit, out bool crit)
    {
        float v = DamageCalc.CalcOutgoing(_stats, baseDmg, src, true, out crit);
        if (forceCrit && !crit)
        {
            v *= _stats.Get(StatType.CritDamage);
            crit = true;
        }
        return v;
    }

    /// <summary>원뿔 안에서 무작위로 흩뿌린 방향</summary>
    Vector3 ApplySpread(Vector3 dir, EquipmentData weapon)
    {
        float deg = (weapon != null ? weapon.SpreadAngle : 0f) * SpreadMul + SpreadAdd;
        if (deg <= 0.001f) return dir;

        Vector2 r = Random.insideUnitCircle * deg;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 0.0001f) right = cameraRoot.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(dir, right);

        return (Quaternion.AngleAxis(r.x, up) * Quaternion.AngleAxis(-r.y, right) * dir).normalized;
    }


    [SerializeField] private GameObject tracerPrefab;

    private float _pendingDamageMul = 1f;

    private readonly HashSet<int> _hitThisAttack = new();
    private bool _isDashAttack;
    private bool _dashStarted;
    private AttackOverride _override;
    public bool IsAttacking => _attackTimer >= 0f;

    // 원거리 상태
    private float _drawTime = -1f;      // 시위 당긴 시간. -1이면 안 당기는 중
    private bool _releaseQueued;
    public bool IsDrawing => _drawTime >= 0f;
    static bool IsChargeType(RangedType t) =>
    t == RangedType.Bow || t == RangedType.Staff;


    // ══════════════════ 초기화 ══════════════════

    public override void Spawned()
    {
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _equip = GetComponent<PlayerEquipment>();
        _ability = GetComponent<AbilityRuntime>();
        _anim = GetComponent<PlayerAnimator>();
        _cc = GetComponent<CharacterController>();
        _events = GetComponent<MotionEventPlayer>();
        _shop = GetComponent<PlayerShop>();
        _status = GetComponent<PlayerStatus>();
        _enchant = GetComponent<PlayerEnchant>();
        _moveRef = GetComponent<PlayerMovement>();
        if (weaponAnim == null) weaponAnim = GetComponentInChildren<WeaponAnimator>(true);

        _fpRig = weaponAnim != null && weaponAnim.ArmRig != null
            ? weaponAnim.ArmRig
            : GetComponentInChildren<FPArmRig>(true);

        if (cameraRoot == null) Debug.LogError("[Attack] cameraRoot 비었음", this);
        if (fpsCamera == null) Debug.LogError("[Attack] fpsCamera 비었음", this);
        if (_stats == null) Debug.LogError("[Attack] PlayerStats 없음", this);
    }

    /// <summary>들고 있는 무기 모델의 WeaponRig (투사체 미리보기)</summary>
    WeaponRig CurrentWeaponRig => _fpRig != null ? _fpRig.CurrentWeaponRig : null;

    /// <summary>모든 클라이언트 — 장전형 무기(RPG 등)는 탄약이 있으면 탄두를 보여줌</summary>
    public override void Render()
    {
        var wr = CurrentWeaponRig;
        if (wr != null && _equip != null) wr.SetLoaded(_equip.Ammo > 0);
    }



    void Update()
    {
        if (!HasInputAuthority) return;

        if (UIManager.InputBlocked)
        {
            _attackQueued = false;
            _releaseQueued = false;
            return;
        }

        var weapon = _equip != null ? _equip.GetWeapon() : null;
        bool ranged = !ForceMelee && weapon != null && weapon.Class == WeaponClass.Ranged;

        if (!ranged)
        {
            if (Input.GetMouseButtonDown(0)) _attackQueued = true;
            return;
        }

        if (weapon.RangedKind == RangedType.Gun)
        {
            if (weapon.FullAuto)
            {
                if (Input.GetMouseButton(0)) _attackQueued = true;
            }
            else if (Input.GetMouseButtonDown(0)) _attackQueued = true;
        }
        else
        {
            if (Input.GetMouseButtonUp(0)) _releaseQueued = true;
        }
    }

    // ══════════════════ 메인 루프 ══════════════════

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (RoundManager.Instance != null && RoundManager.Instance.GameOver)
        {
            ResetCombo();
            _attackQueued = false;
            CancelDraw();
            return;
        }

        if (UIManager.InputBlocked)
        {
            ResetCombo();
            _attackQueued = false;
            CancelDraw();
            return;
        }

        // 사망·기절 중엔 공격 불가 (차징·콤보도 끊음)
        if (_health.IsDead || (_status != null && _status.IsStunned))
        {
            ResetCombo();
            _attackQueued = false;
            CancelDraw();
            return;
        }

        // 스킬 시전 중이면 공격 불가
        if (_ability != null && _ability.IsCasting)
        {
            _attackQueued = false;
            return;
        }

        if (_equip != null && _equip.IsSwapping)
        {
            ResetCombo();
            _attackQueued = false;
            CancelDraw();
            return;
        }


        float dt = Runner.DeltaTime;

        // ── 원거리 ──
        var wpn = _equip.GetWeapon();
        if (!ForceMelee && wpn != null && wpn.Class == WeaponClass.Ranged)
        {
            TickRanged(wpn, dt);
            return;
        }

        // ── 근거리 ──
        bool pressed = _attackQueued;
        _attackQueued = false;

        if (_attackTimer < 0f && _comboIndex > 0 &&
            Runner.SimulationTime > _comboExpire)
            _comboIndex = 0;

        if (_attackTimer >= 0f) TickAttack(dt, pressed);
        else if (pressed) StartAttack();
    }

    void TickRanged(EquipmentData weapon, float dt)
    {
        if (weapon.RangedKind == RangedType.Gun) { TickGun(weapon, dt); return; }

        float spd = WeaponAtkSpd(weapon);

        if (_releaseQueued)
        {
            _releaseQueued = false;

            if (_drawTime >= 0f)
            {
                if (weapon.CanRelease(_drawTime)) Fire(weapon);
                CancelDraw();
            }
            return;
        }

        if (Input.GetMouseButton(0))
        {
            if (_drawTime < 0f) _drawTime = 0f;
            _drawTime += dt * spd;        // ★ 공속만큼 빨리 참
        }

        weaponAnim?.SetDraw(DrawRatio01);
    }


    /// <summary>한 틱에 쏠 수 있는 최대 발 수 (틱보다 연사 간격이 짧을 때)</summary>
    const int MaxShotsPerTick = 3;

    [Header("Debug — 연사 측정")]
    [Tooltip("켜면 1초마다 실제 발사 수·공속·간격·틱레이트를 콘솔에 출력")]
    [SerializeField] private bool logFireRate = false;
    private int _rateShots, _rateTicks, _rateCalls;
    private float _rateTime;

    void TickGun(EquipmentData weapon, float dt)
    {
        if (logFireRate) LogFireRate(weapon, dt);

        // 쉬는 동안 음수로 무한히 쌓이지 않게 — 한 틱만큼만 앞당겨 둠
        _fireTimer = Mathf.Max(_fireTimer - dt, -dt);
        if (_armedTimer > 0f) _armedTimer -= dt;

        // 연사 끊기면 반동 누적 초기화
        _burstResetTimer -= dt;
        if (_burstResetTimer <= 0f) _shotsInBurst = 0;

        if (!_attackQueued) return;
        _attackQueued = false;

        if (_fireTimer > 0f) return;

        float spd = WeaponAtkSpd(weapon);
        float interval = 60f / Mathf.Max(1f, weapon.RoundsPerMinute * spd);

        // 남은 시간을 버리지 않고 이어서 계산 — 틱 단위로 끊겨서 연사 속도가 계단처럼 튀는 것 방지
        // (예: 30틱이면 공속 1.1 → 1.2에서 초당 10발 → 15발로 갑자기 뛰던 문제)
        for (int n = 0; n < MaxShotsPerTick && _fireTimer <= 0f; n++)
        {
            if (!_equip.ConsumeAmmo())
            {
                if (weapon.EmptySound != null && weaponAudio != null)
                    weaponAudio.PlayOneShot(weapon.EmptySound, 0.5f);
                _fireTimer = 0.3f;      // 딸깍 소리가 연타되지 않게
                return;
            }

            FireGun(weapon);
            _rateShots++;
            _fireTimer += interval;
            _shotsInBurst++;
            _burstResetTimer = 0.35f;

            if (!weapon.FullAuto) break;   // 단발은 클릭당 한 발
        }
    }

    void LogFireRate(EquipmentData weapon, float dt)
    {
        _rateCalls++;
        int tick = Runner.Tick.Raw;
        if (tick != _lastRateTick) { _rateTicks++; _lastRateTick = tick; }

        _rateTime += dt;
        if (_rateTime < 1f) return;

        float spd = WeaponAtkSpd(weapon);
        float interval = 60f / Mathf.Max(1f, weapon.RoundsPerMinute * spd);

        Debug.Log($"[Gun] {_rateTime:F2}초 동안 {_rateShots}발 (기대 {_rateTime / interval:F1}발) | " +
                  $"공속 {spd:F2} · RPM {weapon.RoundsPerMinute} · 간격 {interval * 1000f:F0}ms | " +
                  $"틱 {Runner.TickRate}Hz · dt {dt * 1000f:F1}ms · TickGun 호출 {_rateCalls}회 / 틱 {_rateTicks}개 | " +
                  $"이 컴포넌트 {GetInstanceID()} ({name})", this);

        _rateShots = _rateCalls = _rateTicks = 0;
        _rateTime = 0f;
    }
    private int _lastRateTick = -1;

    void FireGun(EquipmentData weapon)
    {
        ConsumeArmedShot(out float bonusMul, out bool forceCrit);

        if (weapon.FireMode == GunFireMode.Hitscan)
            FireHitscan(weapon, bonusMul, forceCrit);
        else
            FireGunProjectile(weapon, bonusMul, forceCrit);

        PlayFireFx(weapon);
        ApplyGunRecoil(weapon);

        GunFired?.Invoke(weapon);
    }

    void ApplyGunRecoil(EquipmentData weapon)
    {
        float mul = Mathf.Min(
            Mathf.Pow(weapon.RecoilRamp, _shotsInBurst), weapon.RecoilMaxMul);

        if (fpsCamera != null)
        {
            fpsCamera.Recoil(weapon.RecoilVertical * mul, weapon.RecoilHorizontal * mul);
            fpsCamera.FovKick(-2f);
        }

        // 팔 반동
        var armRig = weaponAnim != null ? weaponAnim.ArmRig : null;
        armRig?.AddRecoil(mul);      // ★
    }

    void FireGunProjectile(EquipmentData weapon, float bonusMul = 1f, bool forceCrit = false)
    {
        if (weapon.ProjectilePrefab == null)
        {
            Debug.LogWarning($"[Attack] {weapon.DisplayName}에 투사체 프리팹이 없습니다.");
            return;
        }

        Vector3 origin = GetMuzzlePos();
        Vector3 dir = ApplySpread(GetAimDirection(origin), weapon);
        float baseDmg = _stats.Get(StatType.AttackPower) * bonusMul;

        SpawnProjectile(weapon, origin, dir, weapon.ProjectileSpeed, baseDmg, true, forceCrit);
        RPC_PlayProjectile(origin, dir, weapon.ProjectileSpeed);
    }

    void OnHitscanHit(RaycastHit hit, EquipmentData weapon, float bonusMul = 1f, bool forceCrit = false)
    {
        // 피격 콜라이더가 Body 등 자식에 있어도 찾음
        var target = hit.collider.GetComponentInParent<PlayerHealth>();
        if (target == null || target.IsDead) return;
        if (target.transform.root == transform.root) return;                        // 튕겨서 나한테 와도 X
        if (TeamUtil.IsFriendly(Object.InputAuthority, target)) return;

        float baseDmg = _stats.Get(StatType.AttackPower) * bonusMul;
        float outgoing = Outgoing(baseDmg, DamageSource.RangedWeapon, forceCrit, out bool crit);

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(outgoing), Object.InputAuthority, hit.point);
        info.IsCrit = crit;
        _enchant?.ModifyWeaponHit(ref info);
        target.RPC_TakeDamage(info);

        AfterWeaponHit(target, hit.collider.gameObject, outgoing);

        if (fpsCamera != null) fpsCamera.Shake(0.15f);
    }

    void FireHitscan(EquipmentData weapon, float bonusMul = 1f, bool forceCrit = false)
    {
        Vector3 tracerFrom = GetMuzzlePos();
        Vector3 from = cameraRoot.position;                  // 판정은 카메라 기준 — 조준선과 일치
        Vector3 dir = ApplySpread(cameraRoot.forward, weapon);

        float remaining = Mathf.Max(1f, weapon.HitscanRange);
        int bounces = Mathf.Max(0, Ricochets);
        bool first = true;

        while (true)
        {
            Vector3 endPoint = from + dir * remaining;
            // 내 몸(루트 · 자식 피격 콜라이더 전부)은 무시 — 튕긴 탄이 나한테 막히거나 내 몸에서 또 튕기지 않게
            bool hitSomething = CastUtil.Raycast(from, dir, remaining, projectileMask, transform.root, out var hit);

            bool hitPlayer = false;
            if (hitSomething)
            {
                endPoint = hit.point;
                hitPlayer = hit.collider.GetComponentInParent<PlayerHealth>() != null;     // 자식 피격 콜라이더도 사람으로
                if (hitPlayer) OnHitscanHit(hit, weapon, bonusMul, forceCrit);
            }

            // 연출은 총구 → 착탄점, 도탄이면 이어서 튕긴 구간
            SpawnTracer(weapon, tracerFrom, endPoint, first);   // 도탄 구간은 총구에 안 붙임
            RPC_PlayHitscan(tracerFrom, endPoint, first);
            first = false;

            // 벽에 맞았고 도탄이 남았으면 반사해서 한 번 더
            if (!hitSomething || hitPlayer || bounces <= 0) break;
            if ((ricochetMask.value & (1 << hit.collider.gameObject.layer)) == 0) break;   // 튕기지 않는 표면

            bounces--;
            remaining -= hit.distance;
            if (remaining <= 0.5f) break;

            dir = Vector3.Reflect(dir, hit.normal);
            from = hit.point + hit.normal * 0.02f;
            tracerFrom = hit.point;

            // 거울 반사로는 거의 안 맞음 → 튕긴 방향 앞쪽에 보이는 적이 있으면 그쪽으로 꺾임
            if (RicochetSeekRange > 0f && SeekRicochet(from, dir, Mathf.Min(remaining, RicochetSeekRange), out var aim))
                dir = aim;
        }
    }

    /// <summary>튕긴 지점에서 노릴 적 — 반사 방향 기준 각도 안 · 거리 안 · 벽에 안 가린 적 중 가장 가까운 쪽 (각도 가중)</summary>
    bool SeekRicochet(Vector3 from, Vector3 dir, float range, out Vector3 aim)
    {
        aim = dir;
        float best = float.MaxValue;
        bool found = false;
        float half = RicochetSeekAngle * 0.5f;

        int n = Physics.OverlapSphereNonAlloc(from, range, _seekHits, projectileMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = _seekHits[i].GetComponentInParent<PlayerHealth>();
            if (h == null || h.IsDead || h.transform.root == transform.root) continue;
            if (TeamUtil.IsFriendly(Object.InputAuthority, h)) continue;

            Vector3 c = h.transform.position + Vector3.up * 1.1f;
            Vector3 to = c - from;
            float d = to.magnitude;
            if (d < 0.1f || d > range) continue;
            float ang = Vector3.Angle(dir, to);
            if (ang > half) continue;

            // 가려졌는지 — 첫 번째로 맞는 게 그 사람이어야
            if (!CastUtil.Raycast(from, to, d + 0.5f, projectileMask, transform.root, out var los)) continue;
            if (los.collider.GetComponentInParent<PlayerHealth>() != h) continue;

            float score = d + ang * 0.1f;
            if (score < best) { best = score; aim = to / d; found = true; }
        }
        return found;
    }

    /// <param name="fromMuzzle">true = 시작점을 총구에 붙임 (첫 구간). 도탄 구간은 false — 튕긴 지점에서 시작</param>
    void SpawnTracer(EquipmentData weapon, Vector3 from, Vector3 to, bool fromMuzzle = true)
    {
        if (weapon == null || weapon.TracerPrefab == null) return;

        var armRig = weaponAnim != null && fromMuzzle ? weaponAnim.ArmRig : null;
        var muzzle = armRig != null ? armRig.Muzzle : null;

        var go = Instantiate(weapon.TracerPrefab);
        go.GetComponent<Tracer>()?.Show(from, to, muzzle);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayHitscan(Vector3 from, Vector3 to, NetworkBool fireFx)
    {
        if (HasStateAuthority) return;   // 발사자는 이미 로컬로 만들었음

        var weapon = _equip != null ? _equip.GetWeapon() : null;
        SpawnTracer(weapon, from, to, fireFx);     // fireFx = 첫 구간 — 도탄 구간은 튕긴 지점에서
        if (fireFx) PlayFireFx(weapon);  // 도탄 구간은 총구 연출 없이 궤적만
    }

    void PlayFireFx(EquipmentData weapon)
    {
        var armRig = weaponAnim != null ? weaponAnim.ArmRig : null;
        armRig?.MuzzleFlash?.Play();

        if (weapon.FireSound != null)
        {
            float pitch = 1f + Random.Range(
                -weapon.FirePitchVariance, weapon.FirePitchVariance);
            _events?.PlayWeaponSound(weapon.FireSound, weapon.FireVolume, pitch);
        }
    }

    void CancelDraw()
    {
        _drawTime = -1f;
        _releaseQueued = false;
        weaponAnim?.SetDraw(0f);
    }

    void Fire(EquipmentData weapon)
    {
        if (weapon.ProjectilePrefab == null)
        {
            Debug.LogWarning($"[Attack] {weapon.DisplayName}에 투사체 프리팹이 없습니다.");
            return;
        }

        float ratio = weapon.GetDrawRatio(_drawTime);
        float speed = weapon.GetDrawSpeed(ratio);
        float dmgMul = weapon.GetDrawDamageMul(ratio);

        Vector3 origin = GetMuzzlePos();
        Vector3 dir = GetAimDirection(origin);      // ★

        float baseDmg = _stats.Get(StatType.AttackPower) * dmgMul;

        SpawnProjectile(weapon, origin, dir, speed, baseDmg, true);
        RPC_PlayProjectile(origin, dir, speed);

        if (fpsCamera != null)
        {
            fpsCamera.FovKick(-3f * ratio);
            fpsCamera.Shake(0.15f * ratio);
        }

    }

    /// <summary>화면 중앙이 가리키는 지점을 향하는 방향</summary>
    Vector3 GetAimDirection(Vector3 from)
    {
        Vector3 camPos = cameraRoot.position;
        Vector3 camFwd = cameraRoot.forward;

        const float maxAim = 200f;

        Vector3 aimPoint;

        if (CastUtil.Raycast(camPos, camFwd, maxAim, projectileMask, transform.root, out var hit))
            aimPoint = hit.point;
        else
            aimPoint = camPos + camFwd * maxAim;

        Vector3 dir = aimPoint - from;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : camFwd;
    }

    Vector3 GetMuzzlePos()
    {
        var armRig = weaponAnim != null ? weaponAnim.ArmRig : null;
        var muzzle = armRig != null ? armRig.Muzzle : null;
        return muzzle != null ? muzzle.position : cameraRoot.position + cameraRoot.forward * 0.5f;
    }

    /// <summary>쏜 사람(authoritative)과 다른 사람 화면(RPC) 모두 여기를 거침</summary>
    void SpawnProjectile(EquipmentData weapon, Vector3 pos, Vector3 dir,
                     float speed, float baseDmg, bool authoritative, bool forceCrit = false)
    {
        // 무기에 붙어 있던 화살·탄두를 숨김 — 실제 투사체로 교체
        var wr = CurrentWeaponRig;
        if (wr != null) wr.OnFired();

        var go = Instantiate(weapon.ProjectilePrefab, pos, Quaternion.LookRotation(dir));
        var proj = go.GetComponent<Projectile>();

        if (proj == null)
        {
            Debug.LogWarning($"[Attack] {weapon.ProjectilePrefab.name}에 Projectile이 없습니다.");
            Destroy(go, 3f);
            return;
        }

        proj.Launch(
            pos, dir, speed,
            weapon.ProjectileGravity,
            weapon.ProjectileLifetime,
            true,
            projectileMask,
            weapon.PierceCount,
            weapon.PierceDamageFalloff,
            _cc,
            authoritative
                ? (col, point, mul) => OnProjectileHit(col, point, baseDmg * mul, forceCrit)
                : null);
    }

    void OnProjectileHit(Collider col, Vector3 point, float baseDmg, bool forceCrit = false)
    {
        if (col == null) return;
        var target = col.GetComponentInParent<PlayerHealth>();
        if (target == null || target.IsDead) return;                     // 지형이면 그냥 멈춤
        if (target.transform.root == transform.root) return;
        if (TeamUtil.IsFriendly(Object.InputAuthority, target)) return;
        if (target == _health) return;

        float outgoing = Outgoing(baseDmg, DamageSource.RangedWeapon, forceCrit, out bool crit);

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(outgoing), Object.InputAuthority, point);
        info.IsCrit = crit;
        _enchant?.ModifyWeaponHit(ref info);
        target.RPC_TakeDamage(info);

        AfterWeaponHit(target, col.gameObject, outgoing);

        if (fpsCamera != null) fpsCamera.Shake(0.2f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayProjectile(Vector3 pos, Vector3 dir, float speed)
    {
        if (HasStateAuthority) return;   // 발사자는 이미 로컬로 만들었음

        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null) return;

        SpawnProjectile(weapon, pos, dir, speed, 0f, false);
    }

    public float DrawRatio01
    {
        get
        {
            var w = _equip != null ? _equip.GetWeapon() : null;
            if (w == null || _drawTime < 0f) return 0f;
            return Mathf.Clamp01(_drawTime / Mathf.Max(0.01f, w.MaxDrawTime));
        }
    }

    void TickAttack(float dt, bool pressed)
    {
        _attackTimer += dt * _atkSpd;

        float activeStart = _current.WindUp;
        float activeEnd = _current.WindUp + _current.ActiveWindow;
        bool inWindow = _attackTimer >= activeStart && _attackTimer <= activeEnd;

        if (logHits)
            Debug.Log($"[Tick] 콤보{_comboIndex} t={_attackTimer:F3} " +
                      $"구간={activeStart:F3}~{activeEnd:F3} " +
                      $"window={inWindow} applied={_hitApplied} dash={_isDashAttack}");

        // ── 히트 판정 ──
        if (_isDashAttack)
        {
            if (inWindow && !_dashStarted)
            {
                _dashStarted = true;
                _ability.DoDash(_override.Dash);

            }

            if (inWindow || _ability.IsDashing) SweepDashHit(_current);
        }
        else
        {
            // 일반 공격: 한 번만. 구간을 놓쳤으면 늦게라도 발동
            bool missed = !_hitApplied && _attackTimer > activeEnd
                          && _attackTimer < _current.TotalTime;

            if (inWindow || missed) DoHitCheck(_current);
        }

        // ── 전진 (돌진 공격은 DoDash가 담당) ──
        if (!_isDashAttack && _current.LungeDistance > 0f && inWindow)
        {
            float speed = _current.LungeDistance / Mathf.Max(0.01f, _current.ActiveWindow);
            _cc.Move(transform.forward * speed * dt * _atkSpd);
        }

        // ── 다음 입력 예약 (판정 종료 후부터) ──
        if (pressed && _attackTimer >= activeEnd)
            _queuedInput = true;

        // ── 종료 판정 ──
        var weapon = _equip.GetWeapon();
        bool isLastHit = weapon != null && _comboIndex >= weapon.ComboLength;

        // 돌진 공격과 마지막 타는 캔슬 불가
        float cancelAt = (_isDashAttack || isLastHit)
            ? _current.TotalTime
            : _current.CancelTime;

        if (_attackTimer >= _current.TotalTime ||
            (_attackTimer >= cancelAt && _queuedInput))
        {
            _attackTimer = -1f;


            if (_queuedInput)
            {
                _queuedInput = false;
                StartAttack();
            }
            else
            {
                _comboExpire = Runner.SimulationTime + _current.ComboResetTime;
            }
        }
    }

    void StartAttack()
    {
        var weapon = _equip != null ? _equip.GetWeapon() : null;
        if (weapon == null || (weapon.ComboLength == 0 && !ForceMelee)) return;

        if (Locked) return;
        var alterAtk = AlterRef != null ? AlterRef.ActiveAttack : null;
        if (alterAtk != null && alterAtk.AttackCooldown > 0f && Runner.SimulationTime < _alterAtkReady) return;   // 변화 공격 내부 쿨
        if (AlterRef != null && AlterRef.GroundOnlyAttack)
        {
            if (_moveRef == null) _moveRef = GetComponent<PlayerMovement>();
            if (_moveRef != null && (_moveRef.IsAirborne || Time.time < _moveRef.LockMoveUntil)) return;   // 공중 · 착지 중
        }
        _pendingDamageMul = 1f;
        _isDashAttack = false;
        _dashStarted = false;
        _override = AttackOverride.None;
        _overrideSource = -1;
        _hitThisAttack.Clear();

        // 공속·사거리 배율은 시작 시점 값으로 고정 (휘두르는 도중 버프가 바뀌어도 모션·판정이 안 튐)
        _atkSpd = WeaponAtkSpd(weapon);
        _atkRangeMul = Mathf.Max(0.1f, HitRangeMul);

        if (_ability != null && _ability.TryOverrideAttack(out _override, out _overrideSource))
        {
            if (_overrideSource == AbilityRuntime.AlterAttackSource && alterAtk != null)
                _alterAtkReady = Runner.SimulationTime + alterAtk.AttackCooldown;

            _pendingDamageMul = _override.DamageMul;
            _current = _override.Step ?? weapon.GetStep(0);
            if (_current == null) return;

            _isDashAttack = _override.HasDash;
            _comboIndex = 0;
        }
        else
        {
            _comboIndex %= weapon.ComboLength;
            _current = weapon.GetStep(_comboIndex);
            if (_current == null) return;
            _comboIndex++;
        }

        _attackTimer = 0f;
        _hitApplied = false;

        if (fpsCamera != null && !_isDashAttack)
            fpsCamera.FovKick(_current.CameraKickFov);

        // 대체 공격이면 콤보 번호 대신 Behaviour 번호로 (대시 여부와 무관)
        RPC_PlaySwing(_overrideSource >= 0 ? -1 : _comboIndex - 1, _overrideSource, _atkSpd, _atkRangeMul);
    }

    /// <summary>이 타의 실제 판정 값 — 무기·타별 설정 + 능력 사거리 배율. 판정·기즈모·이펙트가 모두 이걸 씀</summary>
    void ResolveHit(ComboStep step, EquipmentData weapon, out WeaponHitMode mode, out float range,
                    out float angle, out float height, out float minRange)
    {
        step.ResolveHitShape(weapon, out mode, out range, out angle, out height, out minRange);
        range *= _atkRangeMul;
    }

    /// <summary>무기 타격 후 공통 처리 — OnHit 트리거, 특수 인챈트, 흡혈</summary>
    void AfterWeaponHit(PlayerHealth target, GameObject targetObj, float outgoing, bool weaponEnchants = true)
    {
        if (target.TryGetComponent<PlayerStatus>(out var ts) && ts.IsEvading) return;

        _ability?.FireTrigger(TriggerType.OnHit, targetObj);
        WeaponHit?.Invoke(target, outgoing, weaponEnchants);

        int heal = Mathf.RoundToInt(outgoing * _stats.Get(StatType.LifeSteal));

        if (weaponEnchants)
            _enchant?.OnWeaponHit(target, outgoing, ref heal);

        if (heal > 0) _health.RPC_Heal(heal);
    }

    /// <summary>
    /// 대리 타격 (조커 분신 등) — 내 무기로 때린 것과 완전히 같은 처리:
    /// 능력치 · 치명 · 인챈트(관통 · 처형 · 부여 효과) · 흡혈 · 능력 '맞혔을 때' 반응. SA에서만.
    /// 나 자신 · 같은 팀(TeamUtil)은 절대 안 때림. 준 피해를 돌려줌
    /// </summary>
    public float ProxyWeaponHit(PlayerHealth target, float damageMul, ComboStep step, bool ranged, Vector3 from)
    {
        if (!HasStateAuthority || target == null || target.IsDead || _stats == null) return 0f;
        if (target.transform.root == transform.root) return 0f;
        if (TeamUtil.IsFriendly(Object.InputAuthority, target)) return 0f;

        var src = ranged ? DamageSource.RangedWeapon : DamageSource.MeleeWeapon;
        float mul = step != null ? step.DamageMultiplier : 1f;
        float outgoing = DamageCalc.CalcOutgoing(_stats, _stats.Get(StatType.AttackPower) * mul * damageMul, src, true, out bool crit);

        bool ench = step == null || !step.NoWeaponEnchants;
        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(outgoing), Object.InputAuthority, from);
        info.IsCrit = crit;
        info.IsMelee = !ranged;
        if (ench) _enchant?.ModifyWeaponHit(ref info);
        target.RPC_TakeDamage(info);

        AfterWeaponHit(target, target.gameObject, outgoing, ench);
        return outgoing;
    }

    void ResetCombo()
    {
        _attackTimer = -1f;
        _comboIndex = 0;
        _queuedInput = false;
        _hitApplied = false;
        _pendingDamageMul = 1f;
        _current = null;
        _dashStarted = false;
    }

    // ══════════════════ 히트 판정 ══════════════════

    void DoHitCheck(ComboStep step)
    {
        if (_hitApplied) return;
        _hitApplied = true;

        var weapon = _equip.GetWeapon();
        if (weapon == null || cameraRoot == null) return;

        ResolveHit(step, weapon, out var mode, out float range,
                   out float angle, out float height, out float minRange);

        Vector3 origin = transform.position;          // 캐릭터 중심
        Vector3 forward = cameraRoot.forward;         // 조준 방향

        Collider[] cols = Physics.OverlapSphere(origin, range, hitMask);
        if (cols.Length == 0) return;

        System.Array.Sort(cols, (a, b) =>
            (a.transform.position - origin).sqrMagnitude
            .CompareTo((b.transform.position - origin).sqrMagnitude));

        float halfAngle = angle * 0.5f;
        Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z).normalized;
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = transform.forward;

        int hits = 0;
        var done = new System.Collections.Generic.HashSet<PlayerHealth>();   // 한 사람에 콜라이더가 여러 개여도 한 번만

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;
            var target = col.GetComponentInParent<PlayerHealth>();     // 자식 피격 콜라이더(변신 범위)도
            if (target == null || target == _health || !done.Add(target)) continue;
            if (target.IsDead) continue;

            Vector3 toTarget = col.transform.position - origin;

            // 수직
            if (Mathf.Abs(toTarget.y) > height)
            {
                if (logHits) Debug.Log($"[Hit] {col.name} 높이초과 {toTarget.y:F2}");
                continue;
            }

            // 수평 거리
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float flatDist = flat.magnitude;

            if (flatDist < minRange) continue;
            if (flatDist > range) continue;

            // 수평 각도
            if (mode == WeaponHitMode.ForwardArc && flatDist > 0.01f)
            {
                float a = Vector3.Angle(flatFwd, flat.normalized);
                if (a > halfAngle)
                {
                    if (logHits) Debug.Log($"[Hit] {col.name} 각도밖 {a:F0}° > {halfAngle:F0}°");
                    continue;
                }
            }

            ApplyHit(target, col.gameObject, step);
            if (step.MaxTargets > 0 && ++hits >= step.MaxTargets) break;      // 0 = 제한 없음
        }
    }

    void ApplyHit(PlayerHealth target, GameObject targetObj, ComboStep step)
    {
        var weapon = _equip.GetWeapon();
        var src = (weapon != null && weapon.Class == WeaponClass.Ranged)
            ? DamageSource.RangedWeapon
            : DamageSource.MeleeWeapon;

        float baseDmg = _stats.Get(StatType.AttackPower)
                      * step.DamageMultiplier
                      * _pendingDamageMul;

        float outgoing = DamageCalc.CalcOutgoing(_stats, baseDmg, src, true, out bool crit);

        bool ench = !step.NoWeaponEnchants;

        var info = DamageInfo.FromPlayerHit(Mathf.RoundToInt(outgoing), Object.InputAuthority, cameraRoot.position);
        info.IsCrit = crit;
        info.IsMelee = src == DamageSource.MeleeWeapon;
        if (ench) _enchant?.ModifyWeaponHit(ref info);     // 관통·처형
        target.RPC_TakeDamage(info);

        AfterWeaponHit(target, targetObj, outgoing, ench);

        if (fpsCamera != null) fpsCamera.Shake(step.CameraShake);
        if (step.HitStopDuration > 0f)
            HitStop.Do(crit ? step.HitStopDuration * 1.5f : step.HitStopDuration);
    }

    //int CalcDamage(float mult)
    //{
    //    float atk = _stats.Get(StatType.AttackPower) * mult;

    //    if (Random.value < _stats.Get(StatType.CritChance))
    //        atk *= _stats.Get(StatType.CritDamage);

    //    return Mathf.Max(1, Mathf.RoundToInt(atk));
    //}

    // ══════════════════ 연출 동기화 ══════════════════

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlaySwing(int comboIndex, int overrideSource, float speed, float rangeMul)
    {
        ComboStep step;

        if (comboIndex < 0)
        {
            step = _ability != null ? _ability.GetOverrideStep(overrideSource) : null;
        }
        else
        {
            var weapon = _equip != null ? _equip.GetWeapon() : null;
            step = weapon?.GetStep(comboIndex);
        }

        if (step == null) return;
        if (comboIndex < 0 && overrideSource == AbilityRuntime.AlterAttackSource && AlterRef != null)
            AlterRef.PlayAttackAnim(speed);      // 용 물어뜯기 등 — 모든 화면, 공속 반영

        weaponAnim?.Play(step, speed);

        // 판정 범위를 같이 넘김 — Vfx 이벤트가 사거리(버프 포함)·각도에 맞춰 크기를 정함
        var weapon2 = _equip != null ? _equip.GetWeapon() : null;
        _events?.Play(step, HasStateAuthority, speed, MotionHitFit.From(step, weapon2, rangeMul));
    }

    void SweepDashHit(ComboStep step)
    {
        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        ResolveHit(step, weapon, out var mode, out float range,
                   out float angle, out float height, out float minRange);

        Vector3 origin = transform.position;
        Vector3 flatFwd = new Vector3(cameraRoot.forward.x, 0f, cameraRoot.forward.z).normalized;
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = transform.forward;

        Collider[] cols = Physics.OverlapSphere(origin, range, hitMask);
        float halfAngle = angle * 0.5f;

        foreach (var col in cols)
        {
            if (col.gameObject == gameObject) continue;



            var target = col.GetComponentInParent<PlayerHealth>();
            if (target == null || target == _health) continue;
            if (target.IsDead) continue;

            int id = target.GetInstanceID();
            if (_hitThisAttack.Contains(id)) continue;

            Vector3 to = col.transform.position - origin;
            if (Mathf.Abs(to.y) > height) continue;

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float d = flat.magnitude;
            if (d > range || d < minRange) continue;

            if (mode == WeaponHitMode.ForwardArc && d > 0.01f)
                if (Vector3.Angle(flatFwd, flat.normalized) > halfAngle) continue;

            _hitThisAttack.Add(id);
            ApplyHit(target, col.gameObject, step);

        }
    }

    // ══════════════════ 디버그 ══════════════════

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || cameraRoot == null || _equip == null) return;

        var weapon = _equip.GetWeapon();
        if (weapon == null) return;

        var step = _current ?? weapon.GetStep(0);
        if (step == null) return;

        ResolveHit(step, weapon, out var mode, out float range,
                   out float angle, out float height, out float minRange);

        Vector3 o = transform.position;
        Vector3 f = new Vector3(cameraRoot.forward.x, 0f, cameraRoot.forward.z).normalized;
        if (f.sqrMagnitude < 0.001f) f = transform.forward;

        bool active = _attackTimer >= 0f && _hitApplied;
        Gizmos.color = active ? Color.red : new Color(1f, 0.5f, 0.2f, 0.6f);

        if (mode == WeaponHitMode.ForwardSphere)
        {
            Gizmos.DrawWireSphere(o, range);
        }
        else
        {
            float half = angle * 0.5f;

            Gizmos.DrawLine(o, o + Quaternion.Euler(0f, -half, 0f) * f * range);
            Gizmos.DrawLine(o, o + Quaternion.Euler(0f, +half, 0f) * f * range);

            int seg = 20;
            Vector3 prev = o + Quaternion.Euler(0f, -half, 0f) * f * range;
            for (int i = 1; i <= seg; i++)
            {
                float a = -half + angle * i / seg;
                Vector3 p = o + Quaternion.Euler(0f, a, 0f) * f * range;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        // 수직 경계
        if (height < 50f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(o + Vector3.up * height, range * 0.1f);
            Gizmos.DrawWireSphere(o - Vector3.up * height, range * 0.1f);
            Gizmos.DrawLine(o + Vector3.up * height, o - Vector3.up * height);
        }

        // 최소 사거리
        if (minRange > 0.01f)
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            Gizmos.DrawWireSphere(o, minRange);
        }
    }


}