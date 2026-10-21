using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum ChatChannel
{
    Match = 0,      // 경기 — 모든 플레이어
    Team = 1,       // 팀 — 같은 팀만 (팀 모드일 때만 열림)
    Global = 2,     // 글로벌 — 게임 이벤트 기록 (플레이어가 직접 쓰지 않음)
}

public enum NoticeKind
{
    Big = 0,        // 화면 가운데 큰 알림
    Small = 1,      // 위쪽 작은 메시지
    Log = 2,        // 채팅창 글로벌 기록만
}

/// <summary>
/// 채팅 · 알림 · 공용 이펙트를 모든 클라이언트로 보내는 통로. 플레이어 프리팹에 붙임 (NetworkObject 아래).
/// 보내기는 언제나 '내 플레이어'(Local)로 — Shared Mode에서 내 플레이어는 내가 권한자라서.
/// </summary>
public class PlayerComms : NetworkBehaviour
{
    public const int MaxChatLength = 120;

    [Tooltip("능력들이 모든 화면에 띄울 공용 이펙트 — 능력은 프리팹으로 요청하고 여기 번호로 보냄 (사망회귀 부활 이펙트 등)")]
    [SerializeField] private GameObject[] sharedFx;

    /// <summary>이 화면의 내 플레이어</summary>
    public static PlayerComms Local { get; private set; }

    /// <summary>보이스 — 지금 어느 쪽으로 말하는지 (VoiceChat.FlagMatch | FlagTeam). 권한자가 씀</summary>
    [Networked] public byte VoiceFlags { get; set; }

    public override void Spawned()
    {
        if (HasInputAuthority) Local = this;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
    }

    // ══════════════════ 채팅 ══════════════════

    /// <summary>내가 채팅을 보냄 (ChatBox가 부름)</summary>
    public void SendChat(ChatChannel ch, string text)
    {
        if (!HasStateAuthority) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (ch == ChatChannel.Global) return;                        // 플레이어는 글로벌에 못 씀

        if (TryGetComponent<PlayerStatus>(out var st) && st.Has(StatusType.Curse))
        {
            ChatLog.AddSystem(TargetUtil.Text("chat.cursed", "저주에 걸려 말할 수 없다..."));
            return;
        }

        text = text.Trim();
        if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);
        RPC_Chat((int)ch, text);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Chat(int channel, string text)
    {
        var ch = (ChatChannel)channel;
        if (ch == ChatChannel.Team && !TeamUtil.AreAllies(Object.InputAuthority, Runner.LocalPlayer)) return;
        ChatLog.Add(ch, TargetUtil.NameOf(this), text);
    }

    // ══════════════════ 알림 ══════════════════

    /// <summary>
    /// 모든 화면에 알림 (권한자 로직에서 부름 — 보통 PlayerComms.Local.Notice).
    /// range &gt; 0이면 origin에서 그 거리 안에 있는 사람만, only가 None이 아니면 그 사람만.
    /// log면 채팅창 글로벌에도 남김
    /// </summary>
    public void Notice(NoticeKind kind, string text, Vector3 origin, float range, PlayerRef only, bool log = true)
    {
        if (!HasStateAuthority || string.IsNullOrEmpty(text)) return;
        RPC_Notice((int)kind, text, origin, range, only, log);
    }

    public void Notice(NoticeKind kind, string text, bool log = true) =>
        Notice(kind, text, Vector3.zero, 0f, PlayerRef.None, log);

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Notice(int kind, string text, Vector3 origin, float range, PlayerRef only, NetworkBool log)
    {
        if (only != PlayerRef.None && only != Runner.LocalPlayer) return;

        if (range > 0f)
        {
            var me = Runner.GetPlayerObject(Runner.LocalPlayer);
            if (me == null || (me.transform.position - origin).sqrMagnitude > range * range) return;
        }

        var k = (NoticeKind)kind;
        if (k != NoticeKind.Log) CommsHud.ShowNotice(k, text);
        if (log || k == NoticeKind.Log) ChatLog.Add(ChatChannel.Global, null, text);
    }

    // ══════════════════ 공용 이펙트 ══════════════════

    /// <summary>모든 화면에 이펙트 — prefab이 이 컴포넌트의 '공용 이펙트' 목록에 있어야 함</summary>
    public void PlayFx(GameObject prefab, Vector3 pos)
    {
        if (!HasStateAuthority || prefab == null) return;
        int i = sharedFx != null ? Array.IndexOf(sharedFx, prefab) : -1;
        if (i < 0)
        {
            Debug.LogWarning($"[PlayerComms] {prefab.name}이(가) 플레이어 프리팹 PlayerComms의 '공용 이펙트' 목록에 없음 — 내 화면에만 나옴", this);
            Destroy(Instantiate(prefab, pos, Quaternion.identity), 3f);
            return;
        }
        RPC_Fx(i, pos);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_Fx(int index, Vector3 pos)
    {
        if (sharedFx == null || index < 0 || index >= sharedFx.Length || sharedFx[index] == null) return;
        Destroy(Instantiate(sharedFx[index], pos, Quaternion.identity), 3f);
    }
}

/// <summary>채팅창에 보이는 줄들 (이 화면). ChatBox가 그림</summary>
public static class ChatLog
{
    public struct Line
    {
        public ChatChannel Channel;
        public string Sender;       // null = 시스템
        public string Text;
        public float Time;
    }

    public const int Max = 100;
    static readonly List<Line> _lines = new();
    public static IReadOnlyList<Line> Lines => _lines;
    public static event Action Changed;

    public static void Add(ChatChannel ch, string sender, string text)
    {
        _lines.Add(new Line { Channel = ch, Sender = sender, Text = text, Time = UnityEngine.Time.unscaledTime });
        while (_lines.Count > Max) _lines.RemoveAt(0);
        Changed?.Invoke();
    }

    /// <summary>나에게만 보이는 안내 (글로벌 색)</summary>
    public static void AddSystem(string text) => Add(ChatChannel.Global, null, text);

    public static void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }
}
