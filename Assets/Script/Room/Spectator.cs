using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 옵저버 카메라 — 게임 중인 방에 들어오면 캐릭터 대신 이걸로 봄.
/// 좌클릭/우클릭 = 다음/이전 사람, 마우스 = 돌려 보기, Space = 자유 카메라(WASD · Q/E · Shift), Esc = 커서
/// 채팅(Enter) · 보이스(경기 말하기 키)는 관전자끼리만. 플레이어 목소리 · 알림은 들림 (목소리는 카메라 위치 기준 거리)
/// </summary>
public class Spectator : MonoBehaviour
{
    static Spectator _inst;
    static NetworkObject _voice;

    public static bool Active => _inst != null;

    /// <summary>관전 중이고 커서를 풀어 둔 상태 (ESC 메뉴 · 설정 창이 열려 있음)</summary>
    public static bool CursorFree => _inst != null && (EscMenu.IsOpen || SettingsWindow.IsOpen);

    /// <summary>관전 시작 — voice = 관전자 보이스 오브젝트 (없으면 null)</summary>
    public static void Begin(NetworkObject voice = null)
    {
        if (_inst != null) return;
        _voice = voice;
        var go = new GameObject("Spectator Camera");
        _inst = go.AddComponent<Spectator>();
    }

    public static void End()
    {
        if (_voice != null)
        {
            var r = _voice.Runner;
            if (r != null && _voice.IsValid && _voice.HasStateAuthority) r.Despawn(_voice);
        }
        _voice = null;

        if (_inst == null) return;
        Destroy(_inst.gameObject);
        _inst = null;
    }

    const float Dist = 4.5f;
    const float PivotHeight = 1.6f;
    const float Sens = 2.5f;

    readonly List<PlayerHealth> _targets = new();
    PlayerHealth _target;
    float _yaw, _pitch = 15f, _refresh;
    bool _free, _cursorFree;
    int _wallMask;
    GUIStyle _style;

    static bool Typing => CommsHud.ChatOpen || VoiceSettingsPanel.IsOpen || DevConsole.IsOpen
                          || EscMenu.IsOpen || SettingsWindow.IsOpen;

    void Awake()
    {
        tag = "MainCamera";
        var cam = gameObject.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        gameObject.AddComponent<AudioListener>();
        _wallMask = ~LayerMask.GetMask("Player", "PlayerGhost");
        LockCursor(true);
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
    }

    void LockCursor(bool lockIt)
    {
        _cursorFree = !lockIt;
        Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockIt;
    }

    void Update()
    {
        if (Time.unscaledTime >= _refresh) RefreshTargets();
        if (Typing) return;     // Esc = ESC 메뉴 (커서는 메뉴가 풀고 잠금)

        _yaw += Input.GetAxis("Mouse X") * Sens;
        _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * Sens, -80f, 80f);

        if (Input.GetMouseButtonDown(0)) Cycle(1);
        if (Input.GetMouseButtonDown(1)) Cycle(-1);
        if (Input.GetKeyDown(KeyCode.Space)) _free = !_free;
    }

    void LateUpdate()
    {
        var rot = Quaternion.Euler(_pitch, _yaw, 0f);

        if (_free || _target == null)
        {
            if (Typing) return;
            float up = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            var mv = new Vector3(Input.GetAxisRaw("Horizontal"), up, Input.GetAxisRaw("Vertical"));
            float speed = Input.GetKey(KeyCode.LeftShift) ? 24f : 10f;
            transform.position += rot * mv * (speed * Time.deltaTime);
            transform.rotation = rot;
            return;
        }

        Vector3 pivot = _target.transform.position + Vector3.up * PivotHeight;
        Vector3 back = rot * Vector3.back;
        float d = Dist;
        if (Physics.SphereCast(pivot, 0.2f, back, out var hit, Dist, _wallMask, QueryTriggerInteraction.Ignore))
            d = Mathf.Max(0.5f, hit.distance);

        transform.SetPositionAndRotation(pivot + back * d, rot);
    }

    void RefreshTargets()
    {
        _refresh = Time.unscaledTime + 0.5f;
        _targets.Clear();
        foreach (var h in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            if (h != null && !h.IsDead) _targets.Add(h);
        _targets.Sort((a, b) => Key(a).CompareTo(Key(b)));

        if (_target == null || _target.IsDead)
            _target = _targets.Count > 0 ? _targets[0] : null;
    }

    static int Key(PlayerHealth h) =>
        h.Object != null && h.Object.IsValid ? h.Object.StateAuthority.PlayerId : int.MaxValue;

    void Cycle(int dir)
    {
        if (_targets.Count == 0) return;
        _free = false;
        int i = _targets.IndexOf(_target);
        i = i < 0 ? 0 : (i + dir + _targets.Count) % _targets.Count;
        _target = _targets[i];
    }

    void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        }

        string who = "자유 카메라";
        if (!_free && _target != null)
        {
            var rs = RoomState.Instance;
            who = rs != null && _target.Object != null && _target.Object.IsValid
                ? rs.NameOf(_target.Object.StateAuthority)
                : _target.name;
        }

        float w = 760f;
        GUI.Box(new Rect((Screen.width - w) / 2f, 12f, w, 70f),
            $"관전 중 · {who}\n좌/우클릭 다음/이전 · Space 자유 카메라 · Esc 메뉴 · Enter 관전 채팅", _style);
    }
}

/// <summary>관전자 채팅 — 관전자끼리만 보임 (RoomState RPC로 보냄)</summary>
public static class SpectatorChat
{
    public static bool CanSend => Spectator.Active && RoomState.Instance != null && RoomLauncher.Runner != null;

    public static void Send(string text)
    {
        var rs = RoomState.Instance;
        var runner = RoomLauncher.Runner;
        if (rs == null || runner == null || string.IsNullOrWhiteSpace(text)) return;
        rs.RPC_SpectatorChat(runner.LocalPlayer, text);
    }

    public static void Receive(PlayerRef from, string text)
    {
        if (!Spectator.Active) return;          // 플레이어 화면엔 안 보임
        var rs = RoomState.Instance;
        string n = rs != null ? rs.NameOf(from) : $"Player {from.PlayerId}";
        ChatLog.AddSystem($"[관전] {n}: {text}");
    }
}
