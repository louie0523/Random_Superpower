using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// 로비 · 대기실 번역. 코드의 글자는 SetLoc.T("lobby.…", "한국어")로 바로,
/// 프리팹(RoomScreenView · 줄 프리팹)에 박힌 고정 글자는 TagStatic이 한국어 글자로 키를 찾아 SetLocText를 붙임
/// → 예전에 만든 프리팹도 다시 안 만들어도 됨 (글자를 직접 바꿨으면 그 글자는 그대로)
/// </summary>
public static class LobbyLoc
{
    /// <summary>프리팹에 박혀 있는 고정 글자 (키, 한국어)</summary>
    public static readonly (string Key, string Ko)[] Static =
    {
        ("lobby.title", "방 목록"),
        ("lobby.reconnect", "다시 연결"),
        ("lobby.nick", "닉네임"),
        ("lobby.room_name", "방 이름"),
        ("lobby.col.count", "인원"),
        ("lobby.lock", "잠금"),
        ("lobby.map", "맵"),
        ("lobby.col.state", "상태"),
        ("lobby.empty_hint", "열린 방이 없습니다 — 새로 만들어 보십시오"),
        ("lobby.room_name_hint", "비우면 '닉네임의 방'"),
        ("lobby.hidden", "숨김 (코드로만)"),
        ("lobby.create", "방 만들기"),
        ("lobby.code", "방 코드"),
        ("lobby.join_code", "코드로 참가"),
        ("lobby.copy", "복사"),
        ("lobby.leave", "방 나가기"),
        ("lobby.section_game", "게임"),
        ("lobby.max_players", "최대 인원"),
        ("lobby.round_time", "라운드 시간"),
        ("lobby.start_soul", "시작 소울"),
        ("lobby.chest", "상자 리필"),
        ("lobby.fall", "낙사"),
        ("lobby.fall_death", "즉사"),
        ("lobby.fall_return", "부활 지점으로"),
        ("lobby.team", "팀전 (설정만 저장 — 팀 나누기는 나중)"),
        ("lobby.hide_room", "방 숨기기 (코드로만 참가)"),
        ("lobby.altar_section", "제단 — 서브 제단 N개 → 중앙 제단"),
        ("lobby.sub_cost", "서브 제단 비용"),
        ("lobby.central_cost", "중앙 제단 비용"),
        ("lobby.need_all", "서브 제단을 전부 열어야 중앙 제단"),
        ("lobby.needed", "필요한 서브 제단"),
        ("lobby.channel", "중앙 채널 시간"),
        ("lobby.altar_note", "맵의 서브 제단이 필요 수보다 적으면 전부 열면 됩니다"),
        ("lobby.section_ban", "밴"),
        ("lobby.unban_all", "전부 풀기"),
        ("lobby.presets", "프리셋"),
        ("lobby.copy_settings", "설정 복사"),
        ("lobby.paste", "붙여넣기"),
        ("lobby.preset_name", "프리셋 이름"),
        ("lobby.save_file", "파일로 저장"),
        ("lobby.open_folder", "프리셋 폴더 열기"),
        ("lobby.load", "불러오기"),
        ("lobby.kick", "강퇴"),
    };

    /// <summary>다른 스크립트(RoomState 등)가 한국어로 만든 알림 · 시작 못 하는 이유 (키, 한국어)</summary>
    static readonly (string Key, string Ko)[] Messages =
    {
        ("lobby.msg.kicked", "방장이 내보냈습니다"),
        ("lobby.err.full", "방이 가득 찼습니다"),
        ("lobby.err.not_found", "해당 코드의 방이 없습니다"),
        ("lobby.err.closed", "들어갈 수 없는 방입니다"),
        ("lobby.why.host_only", "방장만 시작할 수 있습니다"),
        ("lobby.why.preparing", "준비 중"),
        ("lobby.why.no_scene", "맵 씬이 빌드 설정에 없습니다"),
    };

    static Dictionary<string, string> _staticByKo, _msgByKo;
    static readonly Regex NotReady = new(@"^준비 안 한 사람 (\d+)명$");

    static readonly (string Key, string Ko)[] Legacy =
    {
        ("lobby.empty_hint", "열린 방이 없어 — 새로 만들어 봐"),
        ("lobby.altar_note", "맵의 서브 제단이 필요 수보다 적으면 전부 열면 됨"),
        ("lobby.msg.kicked", "방장이 내보냈어"),
        ("lobby.err.full", "방이 가득 찼어"),
        ("lobby.err.not_found", "그 코드의 방이 없어"),
        ("lobby.err.closed", "들어갈 수 없는 방이야"),
        ("lobby.why.host_only", "방장만 시작할 수 있어"),
        ("lobby.why.no_scene", "맵 씬이 빌드 설정에 없어"),
    };

    static void Build()
    {
        if (_staticByKo != null) return;
        _staticByKo = new Dictionary<string, string>();
        foreach (var (k, ko) in Static) _staticByKo[ko] = k;
        _msgByKo = new Dictionary<string, string>();
        foreach (var (k, ko) in Messages) _msgByKo[ko] = k;
        // 예전 반말 글자 (이미 만든 프리팹 · 예전 빌드가 보낸 알림)도 같은 키로
        foreach (var (k, ko) in Legacy)
        {
            _staticByKo.TryAdd(ko, k);
            _msgByKo.TryAdd(ko, k);
        }
    }

    /// <summary>root 아래 고정 글자에 번역 연결 (skip = 코드가 계속 바꾸는 글자). 글자가 넘치면 줄이기도</summary>
    public static void TagStatic(Transform root, params TMP_Text[] skip)
    {
        if (root == null) return;
        Build();
        var skipSet = new HashSet<TMP_Text>();
        if (skip != null) foreach (var t in skip) if (t != null) skipSet.Add(t);

        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (skipSet.Contains(t) || t.GetComponent<SetLocText>() != null) continue;
            if (_staticByKo.TryGetValue((t.text ?? "").Trim(), out var key)) SetLocText.Add(t, key);
        }
    }

    /// <summary>한국어 알림을 지금 언어로 (모르는 글이면 그대로)</summary>
    public static string Msg(string ko)
    {
        if (string.IsNullOrEmpty(ko)) return ko;
        Build();
        if (_msgByKo.TryGetValue(ko, out var key)) return SetLoc.T(key, ko);
        var m = NotReady.Match(ko);
        if (m.Success) return SetLoc.F("lobby.why.not_ready", "준비 안 한 사람 {0}명", m.Groups[1].Value);
        return ko;
    }
}
