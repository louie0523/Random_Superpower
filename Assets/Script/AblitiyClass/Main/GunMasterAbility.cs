using UnityEngine;

/// <summary>
/// 건마스터
///   패시브 0  총기 인챈트       — 총도 인챈트 가능 (기존 Gunsmith)
///   패시브 1  총기의 달인       — 기초 무기가 글록 (스탯은 데이터 StatMod + 필요 무기 Gun)
///   스킬 0    구르기            — 굴러서 이동 + 1초 회피, 총이면 탄약 20% 보충(최소 1발)
///   스킬 1    정밀사격          — 줌 + 감속, 다음 탄환이 반드시 치명타 + 추가 피해
///   궁극기    화장실로 들어가   — 12초 난사: 공속 +100%, 흡혈 20%, 히트스캔 1회 도탄, 탄퍼짐 증가
/// 수치는 레벨별(LevelValue) — 능력 데이터의 각 스킬 탭에서도 같이 편집. 설명에서 {b:필드이름}
/// </summary>
[CreateAssetMenu(menuName = "Game/Ability Behaviour/Gun Master")]
public class GunMasterAbility : AbilityBehaviour
{
    [Header("패시브 — 총기의 달인")]
    [Tooltip("기초 무기 (글록). 라운드 시작·부활 시 이 무기로.\n" +
             "공속·이속·치확은 데이터의 패시브 StatMod에 '필요 무기 = Gun'으로 설정")]
    public EquipmentData StarterGun;

    [Header("스킬 번호 (Skills 배열)")]
    public int RollSkill = 0;
    public int AimSkill = 1;
    public int UltSkill = 2;

    [Header("구르기")]
    public DashParams Roll = new()
    {
        Distance = 5f,
        Duration = 0.3f,
        Direction = DashDirection.Horizontal,   // 이동 키 방향으로 구르려면 MoveInput
        PassThroughPlayers = true,
        StopOnContact = true,
    };
    [ForSkill(nameof(RollSkill), "구르기 거리 (m)")] public LevelValue RollDistance = 5f;
    [ForSkill(nameof(RollSkill), "회피 시간 (초)")] public LevelValue EvadeTime = 1f;
    [ForSkill(nameof(RollSkill), "탄약 보충 (0.2 = 20%)")] public LevelValue AmmoRefill = 0.2f;

    [Header("정밀사격")]
    [ForSkill(nameof(AimSkill), "추가 피해 (%)")] public LevelValue AimBonusPct = 50f;
    [ForSkill(nameof(AimSkill), "이속 감소 (%)")] public LevelValue AimSlowPct = 40f;
    [ForSkill(nameof(AimSkill), "유지 시간 (초)")] public LevelValue AimTimeout = 4f;
    [Tooltip("줌 (FOV 변화, 음수 = 확대)")]
    public float ZoomFov = -25f;

    [Header("궁극기 — 난사")]
    [ForSkill(nameof(UltSkill), "지속 (초)")] public LevelValue UltDuration = 12f;
    [ForSkill(nameof(UltSkill), "공속 증가 (%)")] public LevelValue UltAtkSpeedPct = 100f;
    [ForSkill(nameof(UltSkill), "흡혈 (0.2 = 20%)")] public LevelValue UltLifeSteal = 0.2f;
    [ForSkill(nameof(UltSkill), "도탄 횟수")] public LevelValue UltRicochets = 1f;
    [ForSkill(nameof(UltSkill), "탄퍼짐 배율 (1.3 = +30%)")] public LevelValue UltSpreadMul = 1.3f;
    [Tooltip("원래 탄퍼짐이 0인 총도 퍼지게 더하는 각도 (°)")]
    public float UltSpreadAdd = 1.5f;

    // 스탯 소스 — Src(0~9)
    const int SrcAim = 3, SrcUltAtk = 4, SrcUltLs = 5;

    private PlayerAttack _attack;
    private PlayerEquipment _equip;
    private PlayerHealth _health;
    private FPSCamera _cam;

    private bool _aiming;
    private float _aimTimer;
    private float _ultTimer;

    public bool IsAiming => _aiming;
    public bool InUlt => _ultTimer > 0f;
    public float UltRemaining => Mathf.Max(0f, _ultTimer);

    // ══════════════════ 생명주기 ══════════════════

    public override void OnEquip()
    {
        _attack = Ctx.GetComponent<PlayerAttack>();
        _equip = Ctx.GetComponent<PlayerEquipment>();
        _health = Ctx.GetComponent<PlayerHealth>();
        _cam = Ctx.GetComponentInChildren<FPSCamera>(true);

        Ctx.GetComponent<PlayerEnchant>()?.SetGunEnchant(true);
        if (StarterGun != null) _equip?.SetStartWeapon(StarterGun.Id);

        if (_attack != null) _attack.GunFired += OnGunFired;

        _aiming = false;
        _ultTimer = 0f;
    }

    public override void OnUnequip()
    {
        Ctx.GetComponent<PlayerEnchant>()?.SetGunEnchant(false);
        _equip?.SetStartWeapon(0);

        if (_attack != null) _attack.GunFired -= OnGunFired;

        EndAim();
        EndUlt();
    }

    public override void Tick(float dt)
    {
        bool dead = _health != null && _health.IsDead;

        if (_aiming)
        {
            _aimTimer -= dt;
            if (_aimTimer <= 0f || dead || !HoldingGun()) EndAim();
        }

        if (_ultTimer > 0f)
        {
            _ultTimer -= dt;
            if (_ultTimer <= 0f || dead) EndUlt();
        }
    }

    bool HoldingGun()
    {
        var w = _equip != null ? _equip.GetWeapon() : null;
        return w != null && w.Class == WeaponClass.Ranged && w.RangedKind == RangedType.Gun;
    }

    // ══════════════════ 스킬 ══════════════════

    public override bool OnSkillUsed(int slot, AbilityData.SkillDef skill)
    {
        int i = System.Array.IndexOf(Data.Skills, skill);

        if (i == RollSkill) { DoRoll(); return true; }
        if (i == AimSkill) { BeginAim(); return true; }
        if (i == UltSkill) { BeginUlt(); return true; }
        return false;
    }

    void DoRoll()
    {
        var roll = Roll;
        roll.Distance = Lv(RollDistance);
        Ctx.DoDash(roll);

        var status = Ctx.GetComponent<PlayerStatus>();
        status?.Apply(StatusType.Evade, 1f, Lv(EvadeTime), Ctx.Object.InputAuthority);

        if (HoldingGun() && _equip != null)
        {
            var w = _equip.GetWeapon();
            _equip.AddAmmo(Mathf.Max(1, Mathf.CeilToInt(w.MaxAmmo * Lv(AmmoRefill))));
        }
    }

    void BeginAim()
    {
        if (!HoldingGun() || _attack == null) return;

        EndAim();
        _aiming = true;
        _aimTimer = Lv(AimTimeout);

        _attack.ArmNextShot(Lv(AimBonusPct), true, _aimTimer);

        Ctx.Stats?.AddModifier(new StatModifier(StatType.MoveSpeed, ModType.Percent, -Lv(AimSlowPct), Src(SrcAim)));
        _cam?.SetZoom(ZoomFov);
    }

    void EndAim()
    {
        if (!_aiming) return;
        _aiming = false;

        _attack?.DisarmShot();
        Ctx.Stats?.RemoveBySource(Src(SrcAim));
        _cam?.SetZoom(0f);
        ReleaseCooldown(AimSkill);                // '효과 끝나고 쿨타임'이면 쏜 뒤부터 쿨
    }

    void OnGunFired(EquipmentData w)
    {
        if (_aiming) EndAim();      // 강화 탄환은 PlayerAttack이 이미 소모
    }

    void BeginUlt()
    {
        EndUlt();
        _ultTimer = Lv(UltDuration);

        var stats = Ctx.Stats;
        stats?.AddModifier(new StatModifier(StatType.AttackSpeed, ModType.Percent, Lv(UltAtkSpeedPct), Src(SrcUltAtk)));
        stats?.AddModifier(new StatModifier(StatType.LifeSteal, ModType.Flat, Lv(UltLifeSteal), Src(SrcUltLs)));

        if (_attack != null)
        {
            _attack.Ricochets = Mathf.RoundToInt(Lv(UltRicochets));
            _attack.SpreadMul = Lv(UltSpreadMul);
            _attack.SpreadAdd = UltSpreadAdd;
        }
    }

    void EndUlt()
    {
        bool was = _ultTimer > 0f;
        _ultTimer = 0f;
        if (was) ReleaseCooldown(UltSkill);       // '효과 끝나고 쿨타임'이면 지금부터 쿨

        Ctx.Stats?.RemoveBySource(Src(SrcUltAtk));
        Ctx.Stats?.RemoveBySource(Src(SrcUltLs));

        if (_attack != null)
        {
            _attack.Ricochets = 0;
            _attack.SpreadMul = 1f;
            _attack.SpreadAdd = 0f;
        }
    }
}