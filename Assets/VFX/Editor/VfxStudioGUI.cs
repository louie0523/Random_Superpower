using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ToonVfx.EditorTools
{
    /// <summary>VFX Studio 공용 — 마스크 썸네일, 팔레트 목록·칩, 한글 이름, 색</summary>
    public static class VfxStudioGUI
    {
        // ══════════════ 색 ══════════════

        public static readonly Color AppearColor = new(0.36f, 0.72f, 0.46f);
        public static readonly Color HoldColor = new(0.36f, 0.55f, 0.85f);
        public static readonly Color VanishColor = new(0.9f, 0.56f, 0.3f);
        public static readonly Color LightColor = new(0.95f, 0.82f, 0.3f);
        public static readonly Color TailColor = new(0.6f, 0.6f, 0.6f, 0.35f);
        public static readonly Color Playhead = new(1f, 0.32f, 0.3f);
        public static readonly Color SelectedRow = new(0.26f, 0.42f, 0.66f, 0.45f);
        public static readonly Color DrawStroke = new(0.35f, 0.9f, 1f);

        public static Color Separator => EditorGUIUtility.isProSkin ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.6f, 0.6f, 0.6f);
        public static Color RowBg(int i) => EditorGUIUtility.isProSkin
            ? (i % 2 == 0 ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.22f, 0.22f, 0.22f))
            : (i % 2 == 0 ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.75f, 0.75f, 0.75f));

        // ══════════════ 한글 이름 ══════════════

        public static string ShapeName(VfxShape s) => s switch
        {
            VfxShape.Card => "카드",
            VfxShape.Arc => "호",
            VfxShape.Ring => "링",
            VfxShape.Burst => "뿌리기",
            VfxShape.Pillar => "기둥",
            VfxShape.Streak => "속도선",
            VfxShape.Trail => "잔상 줄",
            VfxShape.Cone => "원뿔",
            _ => s.ToString(),
        };

        public static string Short(VfxAppear a) => a switch
        {
            VfxAppear.Pop => "팍",
            VfxAppear.Grow => "커지며",
            VfxAppear.Sweep => "쓸기",
            VfxAppear.Rise => "솟아",
            VfxAppear.Fade => "서서히",
            _ => a.ToString(),
        };

        public static string Short(VfxHold h) => h switch
        {
            VfxHold.Still => "유지",
            VfxHold.Wobble => "일렁",
            VfxHold.Spin => "회전",
            VfxHold.Pulse => "맥동",
            _ => h.ToString(),
        };

        public static string Short(VfxVanish v) => v switch
        {
            VfxVanish.ErodeFromEnd => "갉아먹기",
            VfxVanish.Hole => "구멍",
            VfxVanish.Thin => "얇게",
            VfxVanish.Shrink => "줄며",
            VfxVanish.ScatterUp => "흩어짐",
            VfxVanish.CoolDown => "식으며",
            VfxVanish.TailChase => "꼬리",
            VfxVanish.ExpandFade => "퍼지며",
            _ => v.ToString(),
        };

        public static string MaskName(VfxBuiltinMask m) => m switch
        {
            VfxBuiltinMask.SlashStrip => "칼자국",
            VfxBuiltinMask.Radial => "원",
            VfxBuiltinMask.Flame => "불꽃 방울",
            VfxBuiltinMask.Star => "반짝 별",
            VfxBuiltinMask.Ring => "고리",
            VfxBuiltinMask.Spike => "가시",
            VfxBuiltinMask.Band => "띠",
            VfxBuiltinMask.Rock => "돌",
            VfxBuiltinMask.Smoke => "연기",
            VfxBuiltinMask.PillarFade => "기둥",
            VfxBuiltinMask.FlameTongue => "세 갈래 불꽃",
            VfxBuiltinMask.Snowflake => "눈송이",
            VfxBuiltinMask.Skull => "해골",
            VfxBuiltinMask.ArrowDown => "아래 화살표",
            VfxBuiltinMask.Star5 => "오각별",
            VfxBuiltinMask.Droplet => "물방울",
            VfxBuiltinMask.TrailFade => "잔상",
            _ => m.ToString(),
        };

        public static readonly VfxBuiltinMask[] AllMasks = (VfxBuiltinMask[])Enum.GetValues(typeof(VfxBuiltinMask));

        // ══════════════ 마스크 썸네일 (GUI용 회색조) ══════════════

        static readonly Dictionary<VfxBuiltinMask, Texture2D> Thumbs = new();

        public static Texture2D Thumb(VfxBuiltinMask k)
        {
            if (Thumbs.TryGetValue(k, out var t) && t != null) return t;

            const int n = 48;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(VfxMaskLibrary.Value(k, (x + 0.5f) / n, (y + 0.5f) / n)) * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            t.SetPixels32(px);
            t.Apply();
            Thumbs[k] = t;
            return t;
        }

        // ══════════════ 팔레트 ══════════════

        static List<VfxPalette> _palettes;

        public static void Invalidate() => _palettes = null;

        public static List<VfxPalette> Palettes
        {
            get
            {
                if (_palettes != null) return _palettes;
                _palettes = new List<VfxPalette>();
                foreach (var guid in AssetDatabase.FindAssets("t:VfxPalette"))
                {
                    var p = AssetDatabase.LoadAssetAtPath<VfxPalette>(AssetDatabase.GUIDToAssetPath(guid));
                    if (p != null) _palettes.Add(p);
                }
                _palettes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                return _palettes;
            }
        }

        public static string PaletteLabel(VfxPalette p) =>
            p == null ? "기본" : !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : p.name;

        /// <summary>코어·중간·외곽 세 줄무늬 + 이름. 눌렀으면 true</summary>
        public static bool PaletteChip(Rect r, VfxPalette p, bool selected)
        {
            var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 16);
            if (p != null)
            {
                float w = inner.width / 3f;
                EditorGUI.DrawRect(new Rect(inner.x, inner.y, w, inner.height), p.Core);
                EditorGUI.DrawRect(new Rect(inner.x + w, inner.y, w, inner.height), p.Mid);
                EditorGUI.DrawRect(new Rect(inner.x + w * 2, inner.y, w, inner.height), p.Edge);
                EditorGUI.DrawRect(new Rect(inner.x, inner.yMax - 3, inner.width, 3), p.Outline);
            }
            else
            {
                EditorGUI.DrawRect(inner, new Color(0.35f, 0.35f, 0.35f));
            }

            if (selected) Outline(new Rect(r.x, r.y, r.width, r.height - 12), Color.white, 2);

            var label = new Rect(r.x, r.yMax - 14, r.width, 14);
            GUI.Label(label, PaletteLabel(p), CenterMini);
            return GUI.Button(r, new GUIContent("", PaletteLabel(p)), GUIStyle.none);
        }

        public static void Outline(Rect r, Color c, float t = 1f)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ══════════════ 스타일 ══════════════

        static GUIStyle _centerMini, _blockLabel, _header, _hint, _sceneHint, _bigButton;

        public static GUIStyle CenterMini => _centerMini ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };

        public static GUIStyle BlockLabel => _blockLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white },
        };

        public static GUIStyle Header => _header ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

        public static GUIStyle Hint => _hint ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel);

        public static GUIStyle SceneHint => _sceneHint ??= new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 12,
            richText = true,
            padding = new RectOffset(8, 8, 6, 6),
        };

        public static GUIStyle BigButton => _bigButton ??= new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 30 };

        /// <summary>섹션 제목 + 위쪽 여백</summary>
        public static void Section(string title)
        {
            EditorGUILayout.Space(6);
            var r = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Separator);
            GUI.Label(r, title, EditorStyles.boldLabel);
        }
    }
}
