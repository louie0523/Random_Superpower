using System.Collections;
using System.Collections.Generic;
using ToonVfx;
using UnityEngine;

/// <summary>이 타격의 실제 판정 범위 — Vfx 이벤트의 '판정 범위에 맞추기'에 씀</summary>
public struct MotionHitFit
{
    public float Range;     // m (사거리 버프 반영된 값)
    public float Angle;     // ° (구체 판정이면 0 → 각도는 안 맞춤)

    public bool IsValid => Range > 0f;

    public static MotionHitFit From(ComboStep step, EquipmentData weapon, float rangeMul)
    {
        if (step == null || weapon == null) return default;
        step.ResolveHitShape(weapon, out var mode, out float range, out float angle, out _, out _);
        return new MotionHitFit
        {
            Range = range * rangeMul,
            Angle = mode == WeaponHitMode.ForwardArc ? angle : 0f,
        };
    }
}

public class MotionEventPlayer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource weaponAudio;

    [Header("Ground Effect")]
    [Tooltip("지면 이펙트가 바닥을 찾을 레이어")]
    [SerializeField] private LayerMask groundMask = ~0;

    private ComboStep _step;
    private float _duration;
    private float _t;
    private bool _isLocal;
    private readonly HashSet<int> _fired = new();
    private float _speed = 1f;
    private MotionHitFit _fit;
    private readonly RaycastHit[] _groundHits = new RaycastHit[8];

    /// <summary>
    /// 화면 번쩍 (본인만) — (색, 지속). ScreenFlash 쪽에서 구독하거나, 아래 Fire의 해당 줄을 직접 호출로 바꿔도 됨
    /// </summary>
    public static System.Action<Color, float> OnScreenFlash;

    public System.Action<string> OnCustomEvent;

    void Awake()
    {
        if (playerRoot == null) playerRoot = transform;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    /// <summary>모션 시작 시 호출</summary>
    public void Play(ComboStep step, bool isLocal, float speed = 1f) => Play(step, isLocal, speed, default);

    /// <summary>모션 시작 시 호출 — fit: 이 타격의 실제 판정 범위 (Vfx 이벤트가 크기를 맞춤)</summary>
    public void Play(ComboStep step, bool isLocal, float speed, MotionHitFit fit)
    {
        _step = step;
        _duration = step != null ? step.TotalTime : 0f;
        _t = 0f;
        _isLocal = isLocal;
        _speed = Mathf.Max(0.1f, speed);
        _fit = fit;
        _fired.Clear();
    }

    public void Stop()
    {
        _step = null;
        _t = 0f;
        _fired.Clear();
    }

    void Update()
    {
        if (_step == null || _step.Events == null) return;

        _t += Time.deltaTime * _speed;
        float p = _duration > 0f ? _t / _duration : 1f;

        for (int i = 0; i < _step.Events.Length; i++)
        {
            if (_fired.Contains(i)) continue;

            var e = _step.Events[i];
            if (e == null || p < e.Time) continue;

            _fired.Add(i);
            Fire(e);
        }

        if (p >= 1f) _step = null;
    }

    void Fire(MotionEvent e)
    {
        // 시점 필터
        if (_isLocal && !e.ShowFirstPerson) return;
        if (!_isLocal && !e.ShowThirdPerson) return;

        switch (e.Type)
        {
            case MotionEventType.Sound:
                PlayClip(audioSource, e.Clip, e.Volume, e.Pitch);
                break;

            case MotionEventType.RandomSound:
                if (e.Clips != null && e.Clips.Length > 0)
                    PlayClip(audioSource, e.Clips[Random.Range(0, e.Clips.Length)], e.Volume,
                             e.Pitch + Random.Range(-e.PitchJitter, e.PitchJitter));
                break;

            case MotionEventType.Effect:
                SpawnEffect(e);
                break;

            case MotionEventType.GroundEffect:
                SpawnGroundEffect(e);
                break;

            case MotionEventType.Vfx:
                SpawnVfx(e);
                break;

            case MotionEventType.TrailStart:
                SetTrail(true);
                break;

            case MotionEventType.TrailEnd:
                SetTrail(false);
                break;

            case MotionEventType.CameraShake:
                if (_isLocal) fpsCamera?.Shake(e.ShakeAmount);
                break;

            case MotionEventType.FovKick:
                if (_isLocal) fpsCamera?.FovKick(e.FovAmount);
                break;

            case MotionEventType.ScreenFlash:
                if (_isLocal) OnScreenFlash?.Invoke(e.FlashColor, e.FlashDuration);
                break;

            case MotionEventType.LightFlash:
                StartCoroutine(LightFlash(e));
                break;

            case MotionEventType.Custom:
                OnCustomEvent?.Invoke(e.Key);
                break;
        }
    }

    // ══════════════════ 위치 ══════════════════

    Transform ResolveParent(EventSpace space) => space switch
    {
        EventSpace.AttachToPlayer => playerRoot,
        EventSpace.AttachToWeapon => armRig != null ? armRig.WeaponTip : null,
        EventSpace.AttachToCamera => cameraRoot,
        EventSpace.AttachToMuzzle => armRig != null ? armRig.Muzzle : null,
        EventSpace.AttachToBody => armRig != null ? armRig.Body : null,
        EventSpace.AttachToHead => armRig != null ? armRig.Head : null,
        EventSpace.AttachToRightHand => armRig != null ? armRig.RightHand : null,
        EventSpace.AttachToLeftHand => armRig != null ? armRig.LeftHand : null,
        _ => null,
    };

    /// <summary>이벤트 위치·회전 계산. follow = 따라갈 대상 (World면 null)</summary>
    void Place(MotionEvent e, out Vector3 pos, out Quaternion rot, out Transform follow)
    {
        Transform basis = ResolveParent(e.Space);
        if (basis == null) basis = playerRoot;

        pos = basis.TransformPoint(e.PositionOffset);
        rot = basis.rotation * Quaternion.Euler(e.RotationOffset);
        follow = e.Space == EventSpace.World ? null : ResolveParent(e.Space);
    }

    /// <summary>발밑에서 바닥을 찾음 — 내 콜라이더는 건너뜀</summary>
    bool FindGround(Vector3 from, out RaycastHit ground)
    {
        int n = Physics.RaycastNonAlloc(from + Vector3.up * 1f, Vector3.down, _groundHits, 4f,
                                        groundMask, QueryTriggerInteraction.Ignore);
        ground = default;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = _groundHits[i];
            if (h.collider.transform.IsChildOf(playerRoot)) continue;
            if (h.distance < best) { best = h.distance; ground = h; }
        }
        return best < float.MaxValue;
    }

    // ══════════════════ 이펙트 ══════════════════

    void SpawnEffect(MotionEvent e)
    {
        if (e.Prefab == null) return;

        Place(e, out var pos, out var rot, out var follow);

        var go = Instantiate(e.Prefab, pos, rot);
        go.transform.localScale *= e.Scale;
        if (follow != null) go.transform.SetParent(follow, true);

        if (!string.IsNullOrEmpty(e.RigState))
        {
            var ra = go.GetComponentInChildren<RigAnimator>();
            if (ra != null) ra.Play(e.RigState);       // 한 번짜리면 끝나고 기본 상태로
        }

        if (e.Lifetime > 0f) Destroy(go, e.Lifetime);
    }

    void SpawnGroundEffect(MotionEvent e)
    {
        Vector3 at = playerRoot.TransformPoint(new Vector3(e.PositionOffset.x, 0f, e.PositionOffset.z));
        Quaternion yaw = Quaternion.Euler(0f, playerRoot.eulerAngles.y, 0f);

        Vector3 pos = at;
        Quaternion rot = yaw;
        if (FindGround(at, out var ground))
        {
            pos = ground.point + ground.normal * (0.02f + e.PositionOffset.y);
            rot = Quaternion.FromToRotation(Vector3.up, ground.normal) * yaw;
        }
        rot *= Quaternion.Euler(e.RotationOffset);

        // VFX Studio 이펙트를 넣었으면 그걸로 (풀링)
        if (e.Vfx != null)
        {
            Vfx.Play(e.Vfx, pos, rot, null, MakeVfxOptions(e));
            return;
        }

        if (e.Prefab == null) return;
        var go = Instantiate(e.Prefab, pos, rot);
        go.transform.localScale *= e.Scale;
        if (e.Lifetime > 0f) Destroy(go, e.Lifetime);
    }

    void SpawnVfx(MotionEvent e)
    {
        if (e.Vfx == null) return;
        Place(e, out var pos, out var rot, out var follow);
        Vfx.Play(e.Vfx, pos, rot, follow, MakeVfxOptions(e));
    }

    VfxPlayOptions MakeVfxOptions(MotionEvent e)
    {
        bool fit = e.FitToHit && _fit.IsValid;
        return new VfxPlayOptions
        {
            Speed = _speed,                       // 공속만큼 이펙트도 빨라짐
            Scale = e.Scale,
            Palette = e.VfxPalette,
            FitRange = fit ? _fit.Range : 0f,
            FitAngle = fit ? _fit.Angle : 0f,
            IsOwner = _isLocal,
        };
    }

    IEnumerator LightFlash(MotionEvent e)
    {
        Place(e, out var pos, out _, out var follow);

        var go = new GameObject("MotionLight");
        go.transform.position = pos;
        if (follow != null) go.transform.SetParent(follow, true);

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = e.FlashColor;
        light.range = e.LightRange;
        light.intensity = e.LightIntensity;
        light.shadows = LightShadows.None;

        float dur = Mathf.Max(0.01f, e.FlashDuration);
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (light == null) yield break;
            light.intensity = e.LightIntensity * (1f - t / dur);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    void SetTrail(bool on)
    {
        if (armRig == null) return;
        var tip = armRig.WeaponTip;
        if (tip == null) return;

        var trail = tip.GetComponentInChildren<TrailRenderer>(true);
        if (trail != null) trail.emitting = on;
    }

    // ══════════════════ 사운드 ══════════════════

    void PlayClip(AudioSource src, AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;

        if (src != null)
        {
            src.pitch = pitch;
            src.PlayOneShot(clip, volume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }

    public void PlayOneShot(AudioClip clip, float volume, float pitch = 1f) =>
        PlayClip(audioSource, clip, volume, pitch);

    public void PlayWeaponSound(AudioClip clip, float volume, float pitch = 1f) =>
        PlayClip(weaponAudio != null ? weaponAudio : audioSource, clip, volume, pitch);
}
