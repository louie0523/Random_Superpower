using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ToonVfx.EditorTools
{
    /// <summary>
    /// 오른쪽 인스펙터 — 레이어 탭 (동작 블록·모양·마스크·색) / 이펙트 탭 (재생·팔레트·다이얼·1인칭).
    /// 값은 SerializedObject로 바꿔서 Undo·프리팹 변경 기록이 자동으로 됨
    /// </summary>
    public partial class VfxStudioWindow
    {
        static readonly string[] InspTabs = { "레이어", "이펙트" };

        void DrawInspector(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 4, r.width - 10, r.height - 6));
            using (new EditorGUILayout.HorizontalScope())
            {
                _inspTab = GUILayout.Toolbar(_inspTab, InspTabs, GUILayout.Height(20));
                _advanced = GUILayout.Toggle(_advanced, new GUIContent("고급", "세부 값까지 모두 보기"), EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(20));
            }

            float oldLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 100f;
            _inspScroll = EditorGUILayout.BeginScrollView(_inspScroll);

            if (_inspTab == 0)
            {
                if (_sel is VfxLayer l && l != null) DrawLayerInspector(l);
                else if (_sel is VfxLight li && li != null) DrawLightInspector(li);
                else
                {
                    EditorGUILayout.Space(8);
                    GUILayout.Label("타임라인에서 레이어를 고르거나, 위쪽 도구로 씬에 새로 그려 보세요.", VfxStudioGUI.Hint);
                }
            }
            else DrawEffectInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = oldLabel;
            GUILayout.EndArea();
        }

        // ══════════════════════ 레이어 ══════════════════════

        void DrawLayerInspector(VfxLayer l)
        {
            var so = new SerializedObject(l);
            so.Update();

            // ── 이름 · 스타일 ──
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.DelayedTextField("이름", l.gameObject.name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(n))
            {
                Undo.RecordObject(l.gameObject, "VFX 레이어 이름");
                l.gameObject.name = n;
                Changed(l.gameObject);
            }
            EditorGUILayout.LabelField("모양", VfxStudioGUI.ShapeName(l.Shape));

            var style = so.FindProperty("Style");
            var profile = so.FindProperty("Profile");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("스타일", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                bool glow = style.enumValueIndex == (int)VfxLayerStyle.Glow;
                if (GUILayout.Toggle(glow, new GUIContent("빛", "발광·부드러운 가장자리·더하기 블렌딩"), EditorStyles.miniButtonLeft) && !glow)
                    SetStyle(style, profile, VfxLayerStyle.Glow);
                if (GUILayout.Toggle(!glow, new GUIContent("카툰", "단색 3단·외곽선·딱 떨어지는 가장자리"), EditorStyles.miniButtonRight) && glow)
                    SetStyle(style, profile, VfxLayerStyle.Toon);
            }

            // ── 동작 ──
            VfxStudioGUI.Section("동작");
            Field(so.FindProperty("StartTime"), "시작 (초)", "이펙트가 시작하고 몇 초 뒤에 나올지");
            var blocks = so.FindProperty("Blocks");
            DrawBlock(blocks, "등장", "Appear", "AppearTime", "AppearStrength", VfxStudioGUI.AppearColor);
            DrawBlock(blocks, "유지", "Hold", "HoldTime", "HoldStrength", VfxStudioGUI.HoldColor);
            DrawBlock(blocks, "사라짐", "Vanish", "VanishTime", "VanishStrength", VfxStudioGUI.VanishColor);
            if (l.Shape == VfxShape.Burst)
                GUILayout.Label("뿌리기는 조각마다 등장 → 유지 → 사라짐을 따로 돌아요.", VfxStudioGUI.Hint);
            else if (_fx.PlayMode == VfxPlayMode.StartLoopEnd)
                GUILayout.Label("'시작 → 반복 → 끝' 이펙트 — 유지 칸이 반복 구간이에요.", VfxStudioGUI.Hint);

            // ── 모양 ──
            string shapeProp = l.Shape switch
            {
                VfxShape.Arc => "Arc",
                VfxShape.Ring => "Ring",
                VfxShape.Burst => "Burst",
                VfxShape.Pillar => "Pillar",
                VfxShape.Streak => "Streak",
                VfxShape.Trail => "Trail",
                VfxShape.Cone => "Cone",
                _ => "Card",
            };
            VfxStudioGUI.Section(VfxStudioGUI.ShapeName(l.Shape) + " 설정");
            DrawChildren(so.FindProperty(shapeProp));
            GUILayout.Label("씬의 노란 손잡이로도 크기를 바꿀 수 있어요. 위치·회전은 Unity 이동(W)·회전(E) 도구로.", VfxStudioGUI.Hint);

            // ── 그림 (마스크) ──
            VfxStudioGUI.Section("그림 모양");
            DrawMaskGrid(so, l);

            // ── 색 · 밝기 ──
            VfxStudioGUI.Section("색 · 밝기");
            Field(so.FindProperty("PaletteOverride"), "이 레이어만 색", "비우면 이펙트 팔레트");
            Field(so.FindProperty("Brightness"), "밝기");
            Field(so.FindProperty("Wobble"), "일렁임");
            var flame = so.FindProperty("FlameEdge");
            Field(flame, "불꽃 가장자리", "가장자리를 불꽃 혀처럼 찢음");
            if (flame.floatValue > 0.001f || _advanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Field(so.FindProperty("FlameScale"), "촘촘함");
                    Field(so.FindProperty("FlameSpeed"), "흐르는 속도");
                }
            }
            if (style.enumValueIndex == (int)VfxLayerStyle.Glow)
            {
                Field(so.FindProperty("Softness"), "가장자리 흐림");
                Field(so.FindProperty("CoreGlow"), "하얀 심지");
            }
            else
            {
                Field(so.FindProperty("OutlineWidth"), "외곽선 두께");
            }

            // ── 고급 ──
            if (_advanced)
            {
                VfxStudioGUI.Section("고급");
                Field(so.FindProperty("Shape"), "모양 바꾸기");
                Field(profile, "셰이더 프로필");
                Field(so.FindProperty("UseFrameStep"), "프레임 끊기 따름");
                Field(so.FindProperty("CoreThreshold"), "코어 색 경계");
                Field(so.FindProperty("MidThreshold"), "중간 색 경계");
                Field(so.FindProperty("WobbleScale"), "일렁임 크기");
                Field(so.FindProperty("WobbleSpeed"), "일렁임 속도");
                Field(so.FindProperty("UVScroll"), "무늬 흐름");
            }

            if (so.ApplyModifiedProperties()) Changed(l);
        }

        static void SetStyle(SerializedProperty style, SerializedProperty profile, VfxLayerStyle s)
        {
            style.enumValueIndex = (int)s;

            // 기본 프로필을 쓰고 있었으면 스타일에 맞게 같이 바꿈 (직접 넣은 프로필은 그대로)
            var clip = ProfileFor(VfxLayerStyle.Toon);
            var add = ProfileFor(VfxLayerStyle.Glow);
            var cur = profile.objectReferenceValue;
            if (cur == null || cur == clip || cur == add)
                profile.objectReferenceValue = ProfileFor(s);
        }

        static void DrawBlock(SerializedProperty blocks, string title, string method, string time, string strength, Color color)
        {
            EditorGUILayout.Space(4);
            var r = EditorGUILayout.GetControlRect();
            EditorGUI.DrawRect(new Rect(r.x, r.y + 2, 4, r.height - 4), color);
            FieldAt(new Rect(r.x + 8, r.y, r.width - 8, r.height), blocks.FindPropertyRelative(method), new GUIContent(title));
            using (new EditorGUI.IndentLevelScope())
            {
                Field(blocks.FindPropertyRelative(time), "길이 (초)");
                Field(blocks.FindPropertyRelative(strength), "세기");
            }
        }

        // ══════════════════════ 값 하나 그리기 ══════════════════════
        // EditorGUILayout.PropertyField는 [Header] 장식까지 그려서 제목이 겹치거나 중복됨 →
        // 장식 없이 값만 그림. [Range]는 슬라이더, [Min]은 하한으로 그대로 지킴

        static void Field(SerializedProperty p, string label, string tip = null)
        {
            if (p == null) return;
            var content = new GUIContent(label, tip ?? p.tooltip);

            switch (p.propertyType)
            {
                case SerializedPropertyType.Vector2:
                    EditorGUI.BeginChangeCheck();
                    var v2 = EditorGUILayout.Vector2Field(content, p.vector2Value);
                    if (EditorGUI.EndChangeCheck()) p.vector2Value = v2;
                    return;
                case SerializedPropertyType.Vector3:
                    EditorGUI.BeginChangeCheck();
                    var v3 = EditorGUILayout.Vector3Field(content, p.vector3Value);
                    if (EditorGUI.EndChangeCheck()) p.vector3Value = v3;
                    return;
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.Color:
                    FieldAt(EditorGUILayout.GetControlRect(), p, content);
                    return;
                default:
                    EditorGUILayout.PropertyField(p, content, true);
                    return;
            }
        }

        static void FieldAt(Rect r, SerializedProperty p, GUIContent content)
        {
            var fi = FieldOf(p);
            var range = fi?.GetCustomAttribute<RangeAttribute>();
            var min = fi?.GetCustomAttribute<MinAttribute>();

            EditorGUI.BeginProperty(r, content, p);
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                {
                    EditorGUI.BeginChangeCheck();
                    float v = range != null
                        ? EditorGUI.Slider(r, content, p.floatValue, range.min, range.max)
                        : EditorGUI.FloatField(r, content, p.floatValue);
                    if (EditorGUI.EndChangeCheck()) p.floatValue = min != null ? Mathf.Max(min.min, v) : v;
                    break;
                }
                case SerializedPropertyType.Integer:
                {
                    EditorGUI.BeginChangeCheck();
                    int v = range != null
                        ? EditorGUI.IntSlider(r, content, p.intValue, (int)range.min, (int)range.max)
                        : EditorGUI.IntField(r, content, p.intValue);
                    if (EditorGUI.EndChangeCheck()) p.intValue = min != null ? Mathf.Max((int)min.min, v) : v;
                    break;
                }
                case SerializedPropertyType.Boolean:
                {
                    EditorGUI.BeginChangeCheck();
                    bool v = EditorGUI.Toggle(r, content, p.boolValue);
                    if (EditorGUI.EndChangeCheck()) p.boolValue = v;
                    break;
                }
                case SerializedPropertyType.Enum:
                {
                    var names = p.enumDisplayNames;
                    var options = new GUIContent[names.Length];
                    for (int i = 0; i < names.Length; i++) options[i] = new GUIContent(names[i]);
                    EditorGUI.BeginChangeCheck();
                    int v = EditorGUI.Popup(r, content, p.enumValueIndex, options);
                    if (EditorGUI.EndChangeCheck()) p.enumValueIndex = v;
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var type = fi != null ? fi.FieldType : typeof(Object);
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUI.ObjectField(r, content, p.objectReferenceValue, type, false);
                    if (EditorGUI.EndChangeCheck()) p.objectReferenceValue = v;
                    break;
                }
                case SerializedPropertyType.Color:
                {
                    EditorGUI.BeginChangeCheck();
                    var v = EditorGUI.ColorField(r, content, p.colorValue);
                    if (EditorGUI.EndChangeCheck()) p.colorValue = v;
                    break;
                }
                default:
                    EditorGUI.PropertyField(r, p, content);
                    break;
            }
            EditorGUI.EndProperty();
        }

        /// <summary>SerializedProperty 경로를 따라가 실제 필드 정보 (Range·Min 읽기용)</summary>
        static FieldInfo FieldOf(SerializedProperty p)
        {
            var t = p.serializedObject.targetObject.GetType();
            FieldInfo fi = null;
            foreach (var part in p.propertyPath.Split('.'))
            {
                fi = t.GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null) return null;
                t = fi.FieldType;
            }
            return fi;
        }

        /// <summary>구조체 안의 값들을 접기 없이 바로 (Header·Tooltip은 그대로)</summary>
        static void DrawChildren(SerializedProperty prop)
        {
            if (prop == null) return;
            var it = prop.Copy();
            var end = it.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                EditorGUILayout.PropertyField(it, true);
            }
        }

        void DrawMaskGrid(SerializedObject so, VfxLayer l)
        {
            var auto = so.FindProperty("AutoMask");
            var builtin = so.FindProperty("BuiltinMask");
            var tex = so.FindProperty("Mask");

            Field(tex, "직접 넣은 그림", "흑백 이미지 (직접 그린 것·AI). 넣으면 아래 기본 모양보다 우선");
            if (tex.objectReferenceValue != null)
            {
                GUILayout.Label("직접 넣은 그림을 쓰는 중이에요. 기본 모양을 쓰려면 비우세요.", VfxStudioGUI.Hint);
                return;
            }

            const float cell = 50f;
            float avail = InspWidth - 36f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(avail / cell));
            int count = VfxStudioGUI.AllMasks.Length + 1;
            int rows = Mathf.CeilToInt(count / (float)perRow);
            var grid = GUILayoutUtility.GetRect(avail, rows * (cell + 12f));

            for (int i = 0; i < count; i++)
            {
                int cx = i % perRow, cy = i / perRow;
                var c = new Rect(grid.x + cx * cell, grid.y + cy * (cell + 12f), cell - 4f, cell - 4f);
                var label = new Rect(c.x - 2, c.yMax, cell, 12f);

                bool selected;
                string name;
                if (i == 0)
                {
                    selected = auto.boolValue;
                    name = "자동";
                    EditorGUI.DrawRect(c, new Color(0.25f, 0.25f, 0.25f));
                    GUI.Label(c, "자동", VfxStudioGUI.BlockLabel);
                }
                else
                {
                    var k = VfxStudioGUI.AllMasks[i - 1];
                    selected = !auto.boolValue && builtin.enumValueIndex == (int)k;
                    name = VfxStudioGUI.MaskName(k);
                    GUI.DrawTexture(c, VfxStudioGUI.Thumb(k), ScaleMode.StretchToFill);
                }

                if (selected) VfxStudioGUI.Outline(new Rect(c.x - 2, c.y - 2, c.width + 4, c.height + 4), new Color(1f, 0.8f, 0.2f), 2f);
                GUI.Label(label, name, VfxStudioGUI.CenterMini);

                if (GUI.Button(c, new GUIContent("", i == 0 ? "모양에 맞는 기본 그림 자동" : name), GUIStyle.none))
                {
                    if (i == 0) auto.boolValue = true;
                    else
                    {
                        auto.boolValue = false;
                        builtin.enumValueIndex = (int)VfxStudioGUI.AllMasks[i - 1];
                    }
                }
            }
        }

        // ══════════════════════ 조명 ══════════════════════

        void DrawLightInspector(VfxLight li)
        {
            var so = new SerializedObject(li);
            so.Update();

            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.DelayedTextField("이름", li.gameObject.name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(n))
            {
                Undo.RecordObject(li.gameObject, "VFX 조명 이름");
                li.gameObject.name = n;
                Changed(li.gameObject);
            }

            VfxStudioGUI.Section("조명");
            Field(so.FindProperty("Mode"), "방식");
            Field(so.FindProperty("StartTime"), "시작 (초)");
            Field(so.FindProperty("Duration"), li.Mode == VfxLightMode.Sustain ? "켜지고 꺼지는 시간" : "길이 (초)");
            Field(so.FindProperty("Intensity"), "밝기");
            Field(so.FindProperty("Range"), "범위 (m)");
            if (li.Mode == VfxLightMode.Sustain)
                Field(so.FindProperty("Flicker"), "일렁임");
            Field(so.FindProperty("UsePaletteColor"), "팔레트 색 쓰기");
            if (!li.UsePaletteColor)
                Field(so.FindProperty("Color"), "색");

            GUILayout.Label("조명은 10명이 동시에 쓰면 부담이 될 수 있어요. 꼭 필요한 이펙트에만.", VfxStudioGUI.Hint);

            if (so.ApplyModifiedProperties()) Changed(li);
        }

        // ══════════════════════ 이펙트 ══════════════════════

        void DrawEffectInspector()
        {
            var so = new SerializedObject(_fx);
            so.Update();

            // ── 이름 (프리팹 파일 이름) ──
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.DelayedTextField("이름", _fx.gameObject.name);
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(n)) RenameEffect(n);

            string path = CurrentPrefabPath;
            GUILayout.Label(string.IsNullOrEmpty(path) ? "아직 프리팹으로 저장되지 않음" : path, VfxStudioGUI.Hint);

            // ── 재생 ──
            VfxStudioGUI.Section("재생");
            var mode = so.FindProperty("PlayMode");
            Field(mode, "재생 방식", "1회: 슬래시·폭발 / 반복 / 시작 → 반복 → 끝: 버프·상태이상");
            if (mode.enumValueIndex == (int)VfxPlayMode.StartLoopEnd)
                Field(so.FindProperty("AutoStopAfter"), "자동 종료 (초)", "0 = Stop()을 부를 때까지 계속");
            Field(so.FindProperty("FrameRate"), "프레임 끊기", "0 = 부드럽게, 30 = 살짝 끊김, 12 = 애니메이션처럼");
            if (_advanced) Field(so.FindProperty("PlayOnEnable"), "켜지면 재생");

            // ── 색 ──
            VfxStudioGUI.Section("색");
            var pal = so.FindProperty("Palette");
            DrawPaletteChips(pal);
            Field(pal, "팔레트");

            // ── 느낌 다이얼 ──
            VfxStudioGUI.Section("느낌 다이얼");
            Field(so.FindProperty("Size"), "크기");
            Field(so.FindProperty("Speed"), "속도");
            Field(so.FindProperty("Intensity"), "강도 (밝기)");
            Field(so.FindProperty("Wobble"), "일렁임");
            Field(so.FindProperty("Thickness"), "두께");
            Field(so.FindProperty("Linger"), "여운", "사라지는 시간 배율 — 클수록 오래 남음");

            // ── 판정 범위 ──
            VfxStudioGUI.Section("판정 범위에 맞추기");
            Field(so.FindProperty("DesignRange"), "기준 사거리 (m)", "0 = 가장 큰 호의 반지름");
            Field(so.FindProperty("DesignAngle"), "기준 각도 (°)", "0 = 가장 큰 호의 각도");
            GUILayout.Label($"지금 기준: {_fx.ResolvedDesignRange:0.##}m · {_fx.ResolvedDesignAngle:0}° — Motion Setup의 VFX 이벤트에서 '판정 범위에 맞추기'를 켜면 실제 사거리에 맞춰 늘고 줄어요.", VfxStudioGUI.Hint);

            // ── 1인칭 ──
            VfxStudioGUI.Section("1인칭 (본인 화면)");
            Field(so.FindProperty("HideForOwner"), "본인에게 숨김");
            Field(so.FindProperty("OwnerScale"), "본인 크기 배율");
            Field(so.FindProperty("OwnerOffset"), "본인 위치 보정");
            Field(so.FindProperty("OwnerRotation"), "본인 회전 보정");
            Field(so.FindProperty("OwnerFacing"), "본인 카메라 향함");
            GUILayout.Label("1인칭 설정은 Motion Setup 미리보기에서 확인할 수 있어요.", VfxStudioGUI.Hint);

            // ── 미리보기 ──
            VfxStudioGUI.Section("미리보기");
            var anchor = so.FindProperty("PreviewAnchor");
            EditorGUI.BeginChangeCheck();
            Field(anchor, "놓는 곳", "스튜디오에서 더미의 어디에 놓을지 (게임과 무관)");
            bool moved = EditorGUI.EndChangeCheck();

            if (so.ApplyModifiedProperties()) Changed(_fx);
            if (moved) _fx.transform.position = VfxPresets.AnchorPosition(_fx.PreviewAnchor);
        }

        void DrawPaletteChips(SerializedProperty pal)
        {
            var list = VfxStudioGUI.Palettes;
            const float w = 74f, h = 34f;
            float avail = InspWidth - 36f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(avail / w));
            int rows = Mathf.CeilToInt(list.Count / (float)perRow);
            var grid = GUILayoutUtility.GetRect(avail, Mathf.Max(1, rows) * h);

            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rect(grid.x + (i % perRow) * w, grid.y + (i / perRow) * h, w - 4f, h - 2f);
                if (VfxStudioGUI.PaletteChip(r, list[i], pal.objectReferenceValue == list[i]))
                    pal.objectReferenceValue = list[i];
            }
            if (list.Count == 0) GUILayout.Label("팔레트가 없어요 — Tools → VFX Studio → 0. 초기 설정", VfxStudioGUI.Hint);
        }

        void RenameEffect(string newName)
        {
            newName = SafeName(newName);
            string path = CurrentPrefabPath;
            if (!string.IsNullOrEmpty(path))
            {
                string err = AssetDatabase.RenameAsset(path, newName);
                if (!string.IsNullOrEmpty(err))
                {
                    ShowNotification(new GUIContent("이름을 바꿀 수 없어요: " + err));
                    return;
                }
                _libDirty = true;
            }
            Undo.RecordObject(_fx.gameObject, "VFX 이름");
            _fx.gameObject.name = newName;
            Repaint();
        }
    }
}
