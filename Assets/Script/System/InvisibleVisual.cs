using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몸 숨기기 연출 (모든 클라이언트) — PlayerStatus가 스폰될 때 자동으로 붙음.
///   투명(상태이상): 몸 · 손 숨김, 무기 · 변화 파츠는 보임
///   변신(AlterData.IsTransform): 몸 · 손 · 무기 숨김, 변신 모델(변화 파츠)만 보임
///     + 내 화면이면 그 변신의 시점 설정대로 3인칭
/// 렌더러의 enabled는 사망 · 3인칭 처리가 쓰고 있어서 건드리지 않고 forceRenderingOff로 숨김.
/// </summary>
[DisallowMultipleComponent]
public class InvisibleVisual : MonoBehaviour
{
    enum Mode { None, Invisible, Transform }

    private PlayerStatus _status;
    private PlayerHealth _health;
    private PlayerAlter _alter;
    private FPArmRig _rig;
    private ThirdPersonView _tpv;

    private readonly List<Renderer> _hidden = new();
    private Mode _mode;
    private AlterData _form;
    private float _rescan;
    private bool _tpOn;

    void Awake()
    {
        _status = GetComponent<PlayerStatus>();
        _health = GetComponent<PlayerHealth>();
        _alter = GetComponent<PlayerAlter>();
        _rig = GetComponentInChildren<FPArmRig>(true);
        _tpv = GetComponent<ThirdPersonView>();
    }

    /// <summary>이 오브젝트(또는 그 몸)가 지금 투명한가 — 머리 위 이름 등이 확인</summary>
    public static bool IsInvisible(Component c)
    {
        if (c == null) return false;
        var st = c.GetComponentInParent<PlayerStatus>();
        return st != null && st.Object != null && st.Object.IsValid && st.Has(StatusType.Invisible);
    }

    bool Valid => _status != null && _status.Object != null && _status.Object.IsValid;

    void LateUpdate()
    {
        bool dead = _health != null && _health.IsDead;
        AlterData form = Valid && _alter != null ? _alter.ActiveTransform : null;

        Mode want = Mode.None;
        if (Valid && !dead)
        {
            if (form != null) want = Mode.Transform;
            else if (_status.Has(StatusType.Invisible)) want = Mode.Invisible;
        }

        if (want != _mode || form != _form)
        {
            Show();
            _mode = want;
            _form = form;
            if (want != Mode.None) Hide();
        }
        else if (_mode != Mode.None && Time.time >= _rescan) Hide();       // 새로 생긴 렌더러 (무기 교체 등)

        TickCamera(form, dead);
    }

    void Hide()
    {
        _rescan = Time.time + 0.5f;
        bool keepWeapon = _mode == Mode.Invisible;
        Transform weapon = _rig != null ? _rig.GetWeaponTransform() : null;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.forceRenderingOff) continue;
            if (keepWeapon && weapon != null && r.transform.IsChildOf(weapon)) continue;     // 투명: 무기는 보임
            if (_alter != null && _alter.IsPart(r.transform)) continue;                      // 변화 파츠(변신 모델 · 말 등)는 보임
            r.forceRenderingOff = true;
            _hidden.Add(r);
        }
    }

    void Show()
    {
        foreach (var r in _hidden)
            if (r != null) r.forceRenderingOff = false;
        _hidden.Clear();
    }

    /// <summary>내 화면 — 변신의 시점 설정대로 3인칭 켜고 끄기</summary>
    void TickCamera(AlterData form, bool dead)
    {
        if (_tpv == null || !Valid || !_status.HasInputAuthority) return;

        bool want = form != null && form.TransformThirdPerson && !dead;
        if (want == _tpOn) return;
        _tpOn = want;
        if (want) _tpv.Enter(this, form.TransformCamera);
        else _tpv.Exit(this);
    }

    void OnDisable()
    {
        Show();
        if (_tpOn && _tpv != null) _tpv.Exit(this);
        _tpOn = false;
        _mode = Mode.None;
    }
}
