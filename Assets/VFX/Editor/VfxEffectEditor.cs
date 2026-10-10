using UnityEditor;
using UnityEngine;

namespace ToonVfx.EditorTools
{
    /// <summary>VfxEffect 인스펙터 — 플레이 없이 씬에서 재생·시간 스크럽</summary>
    [CustomEditor(typeof(VfxEffect))]
    public class VfxEffectEditor : UnityEditor.Editor
    {
        static VfxEffect _active;
        static double _last;
        static bool _loop;

        public override void OnInspectorGUI()
        {
            var fx = (VfxEffect)target;

            if (!Application.isPlaying && fx.gameObject.scene.IsValid())
                DrawPreview(fx);
            else if (!fx.gameObject.scene.IsValid())
                EditorGUILayout.HelpBox("미리보기는 씬에 놓인 이펙트에서만 됩니다. 프리팹을 열거나(더블클릭) 씬에 끌어다 놓으세요.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck() && !Application.isPlaying && fx.gameObject.scene.IsValid())
            {
                fx.Refresh();
                SceneView.RepaintAll();
            }
        }

        void OnDisable()
        {
            if (_active != null && _active == target) StopPreview();
        }

        static void DrawPreview(VfxEffect fx)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                float dur = fx.Duration;
                EditorGUILayout.LabelField($"미리보기 — 길이 {dur:0.00}초 · 레이어 {fx.LayerCount}개", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool playing = _active == fx && fx.IsPlaying;
                    if (GUILayout.Button(playing ? "■ 정지" : "▶ 재생", GUILayout.Height(26)))
                    {
                        if (playing) StopPreview();
                        else StartPreview(fx);
                    }
                    _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.miniButton, GUILayout.Width(48), GUILayout.Height(26));
                }

                // 버프: 반복 중에 '끝내기'를 눌러 사라지는 모습 확인
                if (fx.PlayMode == VfxPlayMode.StartLoopEnd)
                {
                    bool looping = _active == fx && fx.IsPlaying && !fx.IsStopping;
                    using (new EditorGUI.DisabledScope(!looping))
                        if (GUILayout.Button(fx.IsStopping ? "끝나는 중…" : "◼ 끝내기 (사라짐 재생)", GUILayout.Height(22)))
                            fx.Stop();
                    EditorGUILayout.LabelField(
                        fx.AutoStopAfter > 0f ? $"시작 → 반복 → 끝 · {fx.AutoStopAfter:0.#}초 뒤 자동으로 끝남"
                                              : "시작 → 반복 → 끝 · 재생 후 '끝내기'를 누르면 사라짐 구간 재생",
                        EditorStyles.miniLabel);
                }

                EditorGUI.BeginChangeCheck();
                float t = EditorGUILayout.Slider("시간", fx.CurrentTime, 0f, Mathf.Max(0.01f, dur));
                if (EditorGUI.EndChangeCheck())
                {
                    StopPreview();
                    VfxEffect.CameraOverride = SceneCam();
                    fx.SetTime(t);
                    SceneView.RepaintAll();
                }

                if (fx.FrameRate > 0)
                    EditorGUILayout.LabelField($"프레임 끊기 {fx.FrameRate}fps — 슬라이더도 끊겨 보이는 게 정상", EditorStyles.miniLabel);
            }
        }

        static void StartPreview(VfxEffect fx)
        {
            StopPreview();
            _active = fx;
            _last = EditorApplication.timeSinceStartup;
            VfxEffect.CameraOverride = SceneCam();
            fx.Play();
            EditorApplication.update += Tick;
        }

        static void StopPreview()
        {
            EditorApplication.update -= Tick;
            if (_active != null) _active.Pause();
            _active = null;
        }

        static void Tick()
        {
            if (_active == null || Application.isPlaying)
            {
                StopPreview();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - _last), 0.1f);
            _last = now;

            VfxEffect.CameraOverride = SceneCam();
            _active.Tick(dt);

            if (!_active.IsPlaying)
            {
                if (_loop) _active.Play();
                else
                {
                    var fx = _active;
                    StopPreview();
                    fx.SetTime(Mathf.Min(0.15f, fx.Duration));      // 끝나면 보통 색 프레임으로 돌려놓음
                }
            }

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static Camera SceneCam() =>
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
    }
}
