using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>낙사 규칙</summary>
public enum FallRule
{
    Death = 0,      // 떨어지면 죽음 (지금 방식)
    Return = 1,     // 떨어지면 부활 지점으로 옮기기만 (피해 없음)
}

/// <summary>
/// 방 설정 한 묶음. 방 상태(RoomState)에 실려 모두에게 공유되고,
/// 프리셋(클립보드 복사 · 파일 저장)도 이 모양 그대로 저장함.
/// </summary>
[Serializable]
public class RoomSettings
{
    [Tooltip("최대 인원")]
    public int MaxPlayers = 12;

    [Tooltip("맵 번호 (RoomLauncher의 맵 목록 순서)")]
    public int MapIndex = 0;

    [Tooltip("라운드 시간 (초). 0 = 무제한. 시간이 끝나면 제단 많이 연 사람 승리 (같으면 소울)")]
    public int RoundSeconds = 0;

    [Tooltip("시작 소울")]
    public int StartSoul = 0;

    [Tooltip("서브 제단 하나 여는 데 드는 소울")]
    public int SubAltarCost = 35;

    [Tooltip("중앙 제단 여는 데 드는 소울 (서브보다 낮게)")]
    public int CentralAltarCost = 20;

    [Tooltip("중앙 제단을 열려면 열어야 하는 서브 제단 수 (맵의 서브 제단보다 많으면 전부)")]
    public int AltarsNeeded = 4;

    [Tooltip("켜면 서브 제단을 전부 열어야 중앙 제단 (위 숫자 무시)")]
    public bool AltarsNeedAll = false;

    [Tooltip("중앙 제단 채널 시간 (초)")]
    public int CentralChannelSeconds = 8;

    [Tooltip("상자가 다시 채워지는 시간 (초)")]
    public float ChestRefillSeconds = 60f;

    [Tooltip("낙사 규칙")]
    public FallRule Fall = FallRule.Death;

    [Tooltip("팀전 (지금은 설정만 저장 — 팀 나누기는 나중)")]
    public bool TeamMode = false;

    [Tooltip("방 숨기기 — 목록에 안 나오고 코드로만 참가")]
    public bool Hidden = false;

    [Tooltip("못 고르게 막을 클래스 ID (6000번대)")]
    public List<int> BanClass = new();

    [Tooltip("안 나오게 막을 랜덤 능력 ID (8000번대)")]
    public List<int> BanRandom = new();

    const string SharePrefix = "SOULROOM1:";

    // ══════════════════ 기본 ══════════════════

    public RoomSettings Clone() => JsonUtility.FromJson<RoomSettings>(JsonUtility.ToJson(this));

    /// <summary>값을 허용 범위 안으로</summary>
    public void Clamp(int hardMaxPlayers, int mapCount)
    {
        MaxPlayers = Mathf.Clamp(MaxPlayers, 2, Mathf.Max(2, hardMaxPlayers));
        MapIndex = Mathf.Clamp(MapIndex, 0, Mathf.Max(0, mapCount - 1));
        RoundSeconds = Mathf.Clamp(RoundSeconds, 0, 3600);
        StartSoul = Mathf.Clamp(StartSoul, 0, 99999);
        SubAltarCost = Mathf.Clamp(SubAltarCost, 0, 99999);
        CentralAltarCost = Mathf.Clamp(CentralAltarCost, 0, 99999);
        AltarsNeeded = Mathf.Clamp(AltarsNeeded, 0, Altar.MaxSub);
        CentralChannelSeconds = Mathf.Clamp(CentralChannelSeconds, 1, 120);
        ChestRefillSeconds = Mathf.Clamp(ChestRefillSeconds, 1f, 3600f);
        if (!Enum.IsDefined(typeof(FallRule), Fall)) Fall = FallRule.Death;
        BanClass = CleanList(BanClass, RoomState.MaxBanClass);
        BanRandom = CleanList(BanRandom, RoomState.MaxBanRandom);
    }

    static List<int> CleanList(List<int> src, int cap)
    {
        if (src == null) return new List<int>();
        return src.Where(x => x != 0).Distinct().Take(cap).ToList();
    }

    // ══════════════════ 클립보드용 글자 ══════════════════

    /// <summary>다른 사람에게 붙여 넣어 줄 수 있는 한 줄 글자</summary>
    public string ToShareCode()
    {
        string json = JsonUtility.ToJson(this);
        return SharePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>클립보드 글자(공유 코드 또는 json) → 설정</summary>
    public static bool TryParse(string text, out RoomSettings result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        try
        {
            string json;
            if (text.StartsWith(SharePrefix))
                json = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(SharePrefix.Length)));
            else if (text.StartsWith("{"))
                json = text;
            else
                return false;

            result = JsonUtility.FromJson<RoomSettings>(json);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    // ══════════════════ 파일 프리셋 ══════════════════

    public static string PresetFolder => Path.Combine(Application.persistentDataPath, "RoomPresets");

    public static string[] ListPresets()
    {
        if (!Directory.Exists(PresetFolder)) return Array.Empty<string>();
        return Directory.GetFiles(PresetFolder, "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        .OrderBy(x => x)
                        .ToArray();
    }

    public bool SaveAs(string name)
    {
        name = SafeName(name);
        if (name.Length == 0) return false;
        try
        {
            Directory.CreateDirectory(PresetFolder);
            File.WriteAllText(Path.Combine(PresetFolder, name + ".json"), JsonUtility.ToJson(this, true));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Room] 프리셋 저장 실패: {e.Message}");
            return false;
        }
    }

    public static bool TryLoad(string name, out RoomSettings result)
    {
        result = null;
        try
        {
            string path = Path.Combine(PresetFolder, SafeName(name) + ".json");
            if (!File.Exists(path)) return false;
            result = JsonUtility.FromJson<RoomSettings>(File.ReadAllText(path));
            return result != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Room] 프리셋 불러오기 실패: {e.Message}");
            return false;
        }
    }

    static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
            if (Array.IndexOf(bad, c) < 0) sb.Append(c);
        return sb.ToString();
    }
}
