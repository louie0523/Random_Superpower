using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// 스탯 색 · 아이콘 (StatStyle) 인스펙터 — 아래 버튼으로 칸마다 넣은 Icon 그림을 모아 TMP 스프라이트 에셋을 만듦.
/// 만든 뒤엔 칸의 Sprite Name이 스탯 이름으로 채워지고 Sprite Asset에 연결됨 → 글에 &lt;sprite="StatIcons" name="MaxHP"&gt;
/// 메뉴: Tools → UI → 스탯 색 · 아이콘 열기 (없으면 Assets/UI/Game/Resources/StatStyle.asset 만들기)
/// </summary>
[CustomEditor(typeof(StatStyle))]
public class StatStyleEditor : Editor
{
    const string Folder = "Assets/UI/Game";
    const string ResFolder = Folder + "/Resources";
    const string AssetPath = ResFolder + "/StatStyle.asset";
    const string AtlasPath = Folder + "/StatIcons.png";
    const string SpritePath = Folder + "/StatIcons.asset";
    const int Cell = 128;

    [MenuItem("Tools/UI/스탯 색 · 아이콘 열기")]
    static void Open()
    {
        var st = AssetDatabase.LoadAssetAtPath<StatStyle>(AssetPath);
        if (st == null)
        {
            Directory.CreateDirectory(ResFolder);
            st = CreateInstance<StatStyle>();
            AssetDatabase.CreateAsset(st, AssetPath);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = st;
        EditorGUIUtility.PingObject(st);
        StatStyle.Reload();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var st = (StatStyle)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("칸마다 Icon(그림)을 넣고 아래 버튼 → 그림을 한 장으로 모아 TMP 스프라이트 에셋을 만들고 Sprite Name · Sprite Asset을 채움.\n" +
                                "TMP 스프라이트를 직접 쓰면 버튼 없이 Sprite Asset · Sprite Name(또는 Index)만 넣으면 됨.", MessageType.None);
        if (GUILayout.Button("아이콘으로 TMP 스프라이트 에셋 만들기", GUILayout.Height(30))) Build(st);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("미리보기 (글자 그대로)", EditorStyles.miniBoldLabel);
        foreach (StatType s in System.Enum.GetValues(typeof(StatType)))
            EditorGUILayout.SelectableLabel(st.Wrap(s, s.ToString()), EditorStyles.miniLabel, GUILayout.Height(14));

        if (GUI.changed) StatStyle.Reload();
    }

    static void Build(StatStyle st)
    {
        var list = new List<StatStyle.Entry>();
        if (st.Entries != null)
            foreach (var e in st.Entries)
                if (e != null && e.Icon != null) list.Add(e);
        if (list.Count == 0)
        {
            EditorUtility.DisplayDialog("스탯 아이콘", "Icon을 넣은 칸이 없음", "확인");
            return;
        }

        // ── 그림 모으기 (읽기 설정 없이 — 화면에 그려서 읽음) ──
        int cols = Mathf.CeilToInt(Mathf.Sqrt(list.Count));
        int rows = Mathf.CeilToInt(list.Count / (float)cols);
        var atlas = new Texture2D(cols * Cell, rows * Cell, TextureFormat.RGBA32, false);
        var clear = new Color32[atlas.width * atlas.height];
        atlas.SetPixels32(clear);

        var rects = new List<RectInt>();
        for (int i = 0; i < list.Count; i++)
        {
            int cx = (i % cols) * Cell, cy = (rows - 1 - i / cols) * Cell;
            var px = ReadSprite(list[i].Icon, Cell);
            atlas.SetPixels(cx, cy, Cell, Cell, px);
            rects.Add(new RectInt(cx, cy, Cell, Cell));
        }
        atlas.Apply();

        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        imp.textureType = TextureImporterType.Default;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        // ── TMP 스프라이트 에셋 ──
        var sa = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpritePath);
        bool fresh = sa == null;
        if (fresh) sa = CreateInstance<TMP_SpriteAsset>();

        var mat = sa.material;
        if (mat == null)
        {
            var shader = Shader.Find("TextMeshPro/Sprite");
            mat = new Material(shader) { name = "StatIcons Material" };
        }
        mat.SetTexture(ShaderUtilities.ID_MainTex, tex);
        sa.spriteSheet = tex;
        sa.material = mat;

        sa.spriteGlyphTable.Clear();
        sa.spriteCharacterTable.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var r = rects[i];
            var g = new TMP_SpriteGlyph
            {
                index = (uint)i,
                glyphRect = new GlyphRect(r.x, r.y, r.width, r.height),
                metrics = new GlyphMetrics(Cell, Cell, 0f, Cell * 0.82f, Cell),
                scale = 1f,
            };
            sa.spriteGlyphTable.Add(g);
            var c = new TMP_SpriteCharacter(0xFFFE, g) { name = list[i].Stat.ToString() };
            sa.spriteCharacterTable.Add(c);
        }

        if (fresh)
        {
            AssetDatabase.CreateAsset(sa, SpritePath);
            AssetDatabase.AddObjectToAsset(mat, sa);
        }
        else if (!AssetDatabase.IsSubAsset(mat) && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))
            AssetDatabase.AddObjectToAsset(mat, sa);

        // 예전 형식으로 보고 표를 지우지 않게 — 버전 표시
        var so = new SerializedObject(sa);
        var ver = so.FindProperty("m_Version");
        if (ver != null) { ver.stringValue = "1.1.0"; so.ApplyModifiedPropertiesWithoutUndo(); }

        sa.UpdateLookupTables();
        EditorUtility.SetDirty(sa);

        // 칸 채우기
        foreach (var e in list)
        {
            e.SpriteName = e.Stat.ToString();
            e.SpriteIndex = -1;
        }
        st.SpriteAsset = sa;
        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
        StatStyle.Reload();

        Debug.Log($"[스탯 아이콘] {list.Count}개 → {SpritePath}", sa);
    }

    /// <summary>스프라이트 한 칸을 size×size로 (비율 유지, 가운데) — 텍스처 읽기 설정 없이 (Blit으로 복사해서 읽음)</summary>
    static Color[] ReadSprite(Sprite sp, int size)
    {
        var tex = sp.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var full = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        full.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        full.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        var r = sp.textureRect;
        float aspect = r.width / Mathf.Max(1f, r.height);
        float w = aspect >= 1f ? size : size * aspect;
        float h = aspect >= 1f ? size / aspect : size;
        float ox = (size - w) * 0.5f, oy = (size - h) * 0.5f;

        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f - ox) / w, v = (y + 0.5f - oy) / h;
                if (u < 0f || u > 1f || v < 0f || v > 1f) { px[y * size + x] = new Color(0f, 0f, 0f, 0f); continue; }
                px[y * size + x] = full.GetPixelBilinear((r.x + u * r.width) / tex.width, (r.y + v * r.height) / tex.height);
            }
        Object.DestroyImmediate(full);
        return px;
    }
}
