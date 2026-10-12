using System;
using UnityEngine;

namespace ToonVfx
{
    /// <summary>
    /// 이펙트 루트. 자식의 VfxLayer들을 하나의 시계로 재생.
    /// 순수 시각 효과 — 각 클라이언트가 로컬로 재생 (NetworkObject 아님, Runner.Spawn 금지).
    /// 게임에서는 Vfx.Play(...)로 재생 (풀링)
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("VFX/VFX Effect")]
    public class VfxEffect : MonoBehaviour
    {
        [Header("재생")]
        public VfxPlayMode PlayMode = VfxPlayMode.Once;
        [Tooltip("0 = 완전 부드럽게. 30 = 살짝 끊김(기본), 12 = 애니메이션처럼 크게 끊김 — 낮을수록 짧은 동작이 툭 생겨 보임")]
        [Range(0, 60)] public int FrameRate = 30;
        [Tooltip("플레이 중 켜지면 바로 재생 (씬에 직접 놓은 이펙트용)")]
        public bool PlayOnEnable = true;
        [Tooltip("'시작 → 반복 → 끝' 모드에서 이 시간(초)이 지나면 스스로 끝 구간으로. 0 = Stop()을 부를 때까지 계속")]
        [Min(0f)] public float AutoStopAfter;

        [Header("색")]
        public VfxPalette Palette;

        [Header("느낌 다이얼")]
        [Range(0.1f, 3f)] public float Size = 1f;
        [Range(0.1f, 3f)] public float Speed = 1f;
        [Tooltip("밝기")]
        [Range(0f, 3f)] public float Intensity = 1f;
        [Range(0f, 3f)] public float Wobble = 1f;
        [Range(0.2f, 3f)] public float Thickness = 1f;
        [Tooltip("사라지는 시간 배율 — 클수록 오래 남음")]
        [Range(0.2f, 3f)] public float Linger = 1f;

        [Header("판정 범위에 맞추기")]
        [Tooltip("이 이펙트를 만들 때 기준으로 삼은 사거리(m). 재생 시 실제 사거리에 맞춰 호 반지름을 늘리고 줄임.\n0 = 가장 큰 호의 반지름")]
        [Min(0f)] public float DesignRange;
        [Tooltip("기준 각도(°). 재생 시 실제 판정 각도에 맞춰 호 각도를 늘리고 줄임.\n0 = 가장 큰 호의 각도")]
        [Min(0f)] public float DesignAngle;

        [Header("1인칭 (본인 화면)")]
        [Tooltip("본인 화면에서의 크기 배율 — 시야를 가리면 줄이기")]
        [Range(0.1f, 2f)] public float OwnerScale = 1f;
        [Tooltip("본인 화면에서는 안 보이게 (다른 사람만 봄)")]
        public bool HideForOwner;
        [Tooltip("본인 화면에서만 이만큼 옮김 (이펙트 기준 — Y+ 위, Z+ 앞). 내 시야에서 아래로 치우쳐 보이면 Y를 올리기")]
        public Vector3 OwnerOffset;
        [Tooltip("본인 화면에서만 추가로 돌림 (°)")]
        public Vector3 OwnerRotation;
        [Tooltip("본인 화면에서만 호의 '카메라 향함'을 바꿈")]
        public VfxOwnerFacing OwnerFacing = VfxOwnerFacing.Keep;

        [Header("미리보기 (에디터)")]
        [Tooltip("VFX Studio에서 열 때 더미 캐릭터의 어디에 놓을지. 게임 재생 위치와는 무관")]
        public VfxPreviewAnchor PreviewAnchor = VfxPreviewAnchor.Chest;

        /// <summary>
        /// 에디터 스크럽용 — 켜면 '시작 → 반복 → 끝'도 등장 → 유지 → 사라짐을 한 번 순서대로 보여줌 (반복 안 함).
        /// 타임라인의 블록 배치와 화면이 일치하게
        /// </summary>
        [NonSerialized] public bool LinearPreview;

        /// <summary>재생이 끝났거나 Stop 됐을 때 (반복 모드는 끝나지 않으므로 Stop에서만)</summary>
        public event Action<VfxEffect> Finished;

        /// <summary>풀 없이 Instantiate 했을 때 끝나면 스스로 삭제</summary>
        [NonSerialized] public bool DestroyWhenFinished;

        /// <summary>풀이 관리 — 어느 프리팹에서 나왔는지</summary>
        [NonSerialized] internal VfxEffect SourcePrefab;

        /// <summary>에디터 미리보기용 — 씬 뷰 카메라. 플레이 중에는 무시하고 Camera.main 사용</summary>
        public static Camera CameraOverride;

        VfxLayer[] _layers;
        VfxLight[] _lights;
        float _time = 0.15f;            // 에디터에서 처음 열었을 때 번쩍 프레임 대신 보통 색이 보이도록
        bool _playing;
        float _speedMul = 1f;
        float _radiusMul = 1f;
        float _angleMul = 1f;
        int _faceOverride = -1;         // -1 레이어대로, 0 끔, 1 켬 (본인 화면 설정)
        VfxPalette _paletteOverride;
        float _stopTime = -1f;          // '시작 → 반복 → 끝'에서 끝 구간에 들어간 시각. -1 = 아직 반복 중

        public bool IsPlaying => _playing;
        /// <summary>Play 할 때마다 바뀌는 번호 — 풀에서 재사용된 인스턴스가 '아직 내 것인지' 확인용</summary>
        public int PlayId { get; private set; }
        /// <summary>끝 구간 재생 중 (Stop을 불렀고 아직 사라지는 중)</summary>
        public bool IsStopping => _playing && _stopTime >= 0f;
        public float CurrentTime => _time;
        public int LayerCount { get { if (_layers == null) Collect(); return _layers.Length; } }

        public float Duration
        {
            get
            {
                if (_layers == null) Collect();
                float d = 0f;
                foreach (var l in _layers)
                    if (l != null && l.isActiveAndEnabled) d = Mathf.Max(d, l.EndTime(Linger));
                foreach (var li in _lights)
                    if (li != null && li.isActiveAndEnabled) d = Mathf.Max(d, li.EndTime);
                return d;
            }
        }

        /// <summary>판정 맞춤 기준 사거리 — 비워 두면 가장 큰 호 반지름</summary>
        public float ResolvedDesignRange
        {
            get
            {
                if (DesignRange > 0.01f) return DesignRange;
                if (_layers == null) Collect();
                float r = 0f;
                foreach (var l in _layers)
                    if (l != null && l.Shape == VfxShape.Arc) r = Mathf.Max(r, l.Arc.Radius);
                return r;
            }
        }

        /// <summary>판정 맞춤 기준 각도 — 비워 두면 가장 큰 호 각도</summary>
        public float ResolvedDesignAngle
        {
            get
            {
                if (DesignAngle > 0.01f) return DesignAngle;
                if (_layers == null) Collect();
                float a = 0f;
                foreach (var l in _layers)
                    if (l != null && l.Shape == VfxShape.Arc) a = Mathf.Max(a, l.Arc.Angle);
                return a;
            }
        }

        /// <summary>끝 구간 길이 — Stop 후 다 사라질 때까지 (레이어별 사라짐 시간 중 가장 긴 것)</summary>
        public float EndLength
        {
            get
            {
                if (_layers == null) Collect();
                float d = 0f;
                foreach (var l in _layers)
                    if (l != null && l.isActiveAndEnabled) d = Mathf.Max(d, l.EndLength(Linger));
                foreach (var li in _lights)
                    if (li != null && li.isActiveAndEnabled) d = Mathf.Max(d, li.EndLength);
                return d;
            }
        }

        void OnEnable()
        {
            Collect();
            if (Application.isPlaying)
            {
                if (PlayOnEnable) Play();
                else HideAll();
            }
            else Evaluate(_time);
        }

        void OnTransformChildrenChanged() => Collect();

        public void Collect()
        {
            _layers = GetComponentsInChildren<VfxLayer>(true);
            _lights = GetComponentsInChildren<VfxLight>(true);
        }

        // ══════════════ 재생 제어 ══════════════

        public void Play(float speed = 1f, VfxPalette palette = null) =>
            Play(new VfxPlayOptions { Speed = speed, Palette = palette });

        public void Play(in VfxPlayOptions o)
        {
            Collect();
            _speedMul = o.Speed > 0f ? o.Speed : 1f;
            _paletteOverride = o.Palette;
            _radiusMul = 1f;
            _angleMul = 1f;
            _faceOverride = -1;

            // 본인 화면 전용 보정 — 호출하는 쪽이 위치를 정한 뒤 Play를 부르므로 여기서 더함
            if (o.IsOwner)
            {
                transform.position += transform.rotation * OwnerOffset;
                transform.rotation *= Quaternion.Euler(OwnerRotation);
                if (OwnerFacing == VfxOwnerFacing.On) _faceOverride = 1;
                else if (OwnerFacing == VfxOwnerFacing.Off) _faceOverride = 0;
            }

            // 판정 범위에 맞추기 — 기준 대비 비율만큼 모든 호를 같이 늘림 (레이어끼리 간격 유지)
            if (o.FitRange > 0f)
            {
                float design = ResolvedDesignRange;
                if (design > 0.01f) _radiusMul = o.FitRange / design;
            }
            if (o.FitAngle > 0f)
            {
                float design = ResolvedDesignAngle;
                if (design > 0.01f) _angleMul = o.FitAngle / design;
            }

            foreach (var l in _layers)
                if (l != null) l.ResetRuntime();

            _time = 0f;
            _stopTime = -1f;
            _playing = true;
            PlayId++;
            Evaluate(0f);
        }

        /// <summary>
        /// 끝내기. '시작 → 반복 → 끝'이면 사라짐 구간을 재생한 뒤 끝나고, 그 외에는 즉시 끝남.
        /// 끝나면 Finished → 풀로 돌아감
        /// </summary>
        public void Stop()
        {
            if (PlayMode == VfxPlayMode.StartLoopEnd && _playing)
            {
                if (_stopTime < 0f) _stopTime = _time;
                return;
            }
            StopImmediate();
        }

        /// <summary>사라짐 구간 없이 즉시 숨기고 끝냄 (사망·라운드 초기화 등)</summary>
        public void StopImmediate()
        {
            bool was = _playing;
            _playing = false;
            _stopTime = -1f;
            HideAll();
            if (was) Finished?.Invoke(this);
        }

        /// <summary>현재 프레임을 남긴 채 멈춤 (에디터 미리보기용)</summary>
        public void Pause() => _playing = false;

        public void SetTime(float t)
        {
            _time = Mathf.Max(0f, t);
            _stopTime = -1f;
            Evaluate(_time);
        }

        /// <summary>값을 바꾼 뒤 현재 시각으로 다시 그림</summary>
        public void Refresh()
        {
            Collect();
            Evaluate(_time);
        }

        void Update()
        {
            if (!Application.isPlaying || !_playing) return;
            Tick(UnityEngine.Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (!_playing) return;

            _time += dt * Speed * _speedMul;

            if (PlayMode == VfxPlayMode.StartLoopEnd)
            {
                if (_stopTime < 0f && AutoStopAfter > 0f && _time >= AutoStopAfter) _stopTime = _time;

                if (_stopTime >= 0f && _time - _stopTime >= EndLength)
                {
                    _playing = false;
                    _stopTime = -1f;
                    HideAll();
                    Finished?.Invoke(this);
                    if (DestroyWhenFinished && Application.isPlaying) Destroy(gameObject);
                    return;
                }

                Evaluate(_time);
                return;
            }

            float dur = Duration;

            if (_time >= dur)
            {
                if (PlayMode == VfxPlayMode.Loop && dur > 0f)
                    _time %= dur;
                else
                {
                    _playing = false;
                    HideAll();
                    Finished?.Invoke(this);
                    if (DestroyWhenFinished && Application.isPlaying) Destroy(gameObject);
                    return;
                }
            }

            Evaluate(_time);
        }

        // ══════════════ 평가 ══════════════

        public void Evaluate(float t)
        {
            if (_layers == null) Collect();

            float stepped = FrameRate > 0 ? Mathf.Floor(t * FrameRate + 1e-4f) / FrameRate : t;

            Camera cam = !Application.isPlaying && CameraOverride != null ? CameraOverride : Camera.main;
            VfxPalette pal = _paletteOverride != null ? _paletteOverride : Palette;

            var ctx = new VfxContext
            {
                Size = Size,
                Thickness = Thickness,
                Linger = Linger,
                WobbleMul = Wobble,
                BrightnessMul = Intensity,
                RadiusMul = _radiusMul,
                AngleMul = _angleMul,
                FaceCameraOverride = _faceOverride,
                StopTime = _stopTime,
                Endless = PlayMode != VfxPlayMode.Once && !LinearPreview,
                Palette = pal,
                Camera = cam,
            };

            foreach (var l in _layers)
            {
                if (l == null) continue;
                if (!l.isActiveAndEnabled) { l.Hide(); continue; }

                float raw = l.UseFrameStep ? stepped : t;
                ctx.Time = raw;         // 셰이더 노이즈 — 반복 구간이 돌아도 끊기지 않게 실제 시간
                ctx.RawTime = raw;      // 회전·맥동 등 유지 효과 시계
                l.Evaluate(PlayMode == VfxPlayMode.StartLoopEnd && !LinearPreview ? MapLoop(l, raw) : raw, ctx);
            }

            ctx.Time = stepped;
            ctx.RawTime = stepped;
            foreach (var li in _lights)
            {
                if (li == null) continue;
                if (!li.isActiveAndEnabled) { li.Hide(); continue; }
                li.Evaluate(stepped, ctx);
            }
        }

        /// <summary>
        /// '시작 → 반복 → 끝' — 이펙트 시각을 레이어 시각으로 바꿈.
        /// 등장은 그대로, 유지 칸은 계속 반복, Stop 후에는 그 순간부터 사라짐 칸 재생.
        /// 뿌리기는 매핑하지 않음 (한 번 터지거나, '계속 뿜기'로 스스로 반복)
        /// </summary>
        float MapLoop(VfxLayer l, float raw)
        {
            if (l.Shape == VfxShape.Burst) return raw;

            float S = l.StartTime;
            float A = l.Blocks.AppearTime;
            float H = l.Blocks.HoldTime;
            float lt = raw - S;

            if (_stopTime >= 0f)
            {
                if (_stopTime - S < 0f) return -1e6f;               // 시작도 하기 전에 끝남 → 안 보임
                return S + A + H + Mathf.Max(0f, raw - _stopTime);  // 사라짐 칸
            }

            if (lt < A) return raw;                                 // 등장 중
            if (H <= 0.0001f) return S + A;                         // 유지 길이 0 → 등장 끝 모습 그대로
            return S + A + Mathf.Repeat(lt - A, H);                 // 유지 칸 반복
        }

        void HideAll()
        {
            if (_layers == null) Collect();
            foreach (var l in _layers)
                if (l != null) l.Hide();
            foreach (var li in _lights)
                if (li != null) li.Hide();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || Application.isPlaying || !gameObject.scene.IsValid()) return;
                Refresh();
            };
        }
#endif
    }
}
