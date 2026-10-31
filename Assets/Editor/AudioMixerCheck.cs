#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Tools → 설정 → 오디오 믹서 확인 — 그룹 · 노출 파라미터가 맞게 만들어졌는지</summary>
public static class AudioMixerCheck
{
    [MenuItem("Tools/설정/오디오 믹서 확인")]
    static void Check()
    {
        var m = Resources.Load<AudioMixer>(GameAudio.MixerName);
        if (m == null)
        {
            Debug.LogError($"[Audio] Resources 폴더에 '{GameAudio.MixerName}' 믹서가 없어 — 파일 이름 · 위치(Resources 폴더 안) 확인");
            return;
        }

        var sb = new StringBuilder($"[Audio] {AssetDatabase.GetAssetPath(m)}\n");
        bool ok = true;

        foreach (var name in GameAudio.GroupNames)
        {
            AudioMixerGroup found = null;
            foreach (var g in m.FindMatchingGroups(string.Empty))
                if (string.Equals(g.name, name, System.StringComparison.OrdinalIgnoreCase)) found = g;

            if (found == null)
            {
                sb.Append($"  ✘ 그룹 {name} 없음 — Master 아래에 추가\n");
                ok = false;
                continue;
            }

            // Master 바로 아래인지 (다른 그룹 안에 들어가 있으면 그 그룹 크기에 같이 줄어듦)
            bool direct = false;
            foreach (var g in m.FindMatchingGroups("Master/" + found.name))
                if (g == found) direct = true;

            if (direct) sb.Append($"  ✔ 그룹 {found.name}\n");
            else
            {
                sb.Append($"  ✘ 그룹 {found.name}이 다른 그룹 안에 있음 — Groups 목록에서 끌어다 Master 위에 놓기\n");
                ok = false;
            }
        }

        ok &= Param(m, GameAudio.MasterParam, "Master", sb);
        for (int i = 0; i < GameAudio.ParamNames.Length; i++)
            ok &= Param(m, GameAudio.ParamNames[i], GameAudio.GroupNames[i], sb);

        sb.Append(ok ? "→ 다 맞음" : "→ ✘ 고칠 것 있음");
        if (ok) Debug.Log(sb.ToString());
        else Debug.LogWarning(sb.ToString());

        Selection.activeObject = m;
    }

    static bool Param(AudioMixer m, string param, string group, StringBuilder sb)
    {
        bool found = m.GetFloat(param, out _);
        sb.Append(found
            ? $"  ✔ 파라미터 {param}\n"
            : $"  ✘ 파라미터 {param} 없음 — {group} 그룹의 Volume 우클릭 → Expose → 이름을 {param}로\n");
        return found;
    }
}
#endif