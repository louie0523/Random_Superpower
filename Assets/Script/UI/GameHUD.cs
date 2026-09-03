using Fusion;
using UnityEngine;

public class GameHUD : MonoBehaviour
{
    private NetworkRunner _runner;

    void OnGUI()
    {
        if (_runner == null)
        {
            _runner = FindFirstObjectByType<NetworkRunner>();
            return;
        }

        var local = _runner.GetPlayerObject(_runner.LocalPlayer);
        if (local == null) return;
        if (!local.TryGetComponent<PlayerHealth>(out var hp)) return;

        GUI.Label(new Rect(20, 20, 300, 30),
            $"HP: {hp.HP}   SOUL: {hp.Soul}");

        if (hp.IsDead)
            GUI.Label(new Rect(Screen.width / 2 - 40, Screen.height / 2, 200, 30), "사망");

        // 조준점
        GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 10, 20, 20), "+");

        var ar = local.GetComponent<AbilityRuntime>();
        var rand = ar?.GetRandomAbility();
        if (rand != null)
            GUI.Label(new Rect(20, 50, 400, 30), $"이번 생: {rand.DisplayName}");
    }
}