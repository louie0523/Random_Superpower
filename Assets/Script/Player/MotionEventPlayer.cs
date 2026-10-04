using UnityEngine;
using System.Collections.Generic;

public class MotionEventPlayer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private FPArmRig armRig;
    [SerializeField] private FPSCamera fpsCamera;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource weaponAudio;

    private ComboStep _step;
    private float _duration;
    private float _t;
    private bool _isLocal;
    private readonly HashSet<int> _fired = new();
    private float _speed = 1f;

    void Awake()
    {
        if (playerRoot == null) playerRoot = transform;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    /// <summary>모션 시작 시 호출</summary>
    public void Play(ComboStep step, bool isLocal, float speed = 1f)
    {
        _step = step;
        _duration = step != null ? step.TotalTime : 0f;
        _t = 0f;
        _isLocal = isLocal;
        _speed = Mathf.Max(0.1f, speed);
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
            if (p < e.Time) continue;

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
                PlaySound(e);
                break;

            case MotionEventType.Effect:
                SpawnEffect(e);
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

            case MotionEventType.Custom:
                OnCustomEvent?.Invoke(e.Key);
                break;
        }
    }

    void PlaySound(MotionEvent e)
    {
        if (e.Clip == null) return;

        if (audioSource != null)
        {
            audioSource.pitch = e.Pitch;
            audioSource.PlayOneShot(e.Clip, e.Volume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(e.Clip, transform.position, e.Volume);
        }
    }

    void SpawnEffect(MotionEvent e)
    {
        if (e.Prefab == null) return;

        Transform parent = ResolveParent(e.Space);
        Vector3 pos;
        Quaternion rot;

        if (parent != null)
        {
            pos = parent.TransformPoint(e.PositionOffset);
            rot = parent.rotation * Quaternion.Euler(e.RotationOffset);
        }
        else
        {
            pos = playerRoot.TransformPoint(e.PositionOffset);
            rot = playerRoot.rotation * Quaternion.Euler(e.RotationOffset);
        }

        var go = Instantiate(e.Prefab, pos, rot);
        go.transform.localScale *= e.Scale;

        // World는 부모 안 붙임
        if (e.Space != EventSpace.World && parent != null)
            go.transform.SetParent(parent, true);

        if (e.Lifetime > 0f) Destroy(go, e.Lifetime);
    }

    Transform ResolveParent(EventSpace space) => space switch
    {
        EventSpace.AttachToPlayer => playerRoot,
        EventSpace.AttachToWeapon => armRig != null ? armRig.WeaponTip : null,
        EventSpace.AttachToCamera => cameraRoot,
        _ => null,
    };

    void SetTrail(bool on)
    {
        if (armRig == null) return;
        var tip = armRig.WeaponTip;
        if (tip == null) return;

        var trail = tip.GetComponentInChildren<TrailRenderer>(true);
        if (trail != null) trail.emitting = on;
    }

    public void PlayOneShot(AudioClip clip, float volume, float pitch = 1f)
    {
        if (clip == null) return;

        if (audioSource != null)
        {
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }

    public void PlayWeaponSound(AudioClip clip, float volume, float pitch = 1f)
    {
        if (clip == null) return;
        var src = weaponAudio != null ? weaponAudio : audioSource;
        if (src == null) { AudioSource.PlayClipAtPoint(clip, transform.position, volume); return; }

        src.pitch = pitch;
        src.PlayOneShot(clip, volume);
    }

    public System.Action<string> OnCustomEvent;
}