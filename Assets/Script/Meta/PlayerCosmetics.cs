using Fusion;
using UnityEngine;

/// <summary>
/// 게임 안 꾸미기 · 무기 스킨 — 플레이어 프리팹(NetworkObject 있는 곳)에 붙임.
/// 내 것은 들어올 때 저장에서 읽어 네트워크로 → 다른 사람 화면에서도 같은 모습.
///   부위 → 자식의 CharacterAppearanceApplier (캐릭터 모델 루트에 붙여 두기)
///   무기 스킨 → FPArmRig가 무기를 띄울 때 SkinFor(무기)를 물어봄
/// </summary>
public class PlayerCosmetics : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(OnCodeChanged))] public NetworkString<_128> Code { get; set; }

    Appearance _look;
    string _parsed;

    public Appearance Look
    {
        get
        {
            string c = Code.ToString();
            if (c != _parsed) { _parsed = c; _look = Appearance.Parse(c); }
            return _look;
        }
    }

    public override void Spawned()
    {
        if (HasStateAuthority)      // 내 플레이어 (Shared 모드 — 내가 만든 오브젝트)
            Code = Clip(PlayerSave.Look.FullCode);
        OnCodeChanged();
    }

    static string Clip(string s) => s.Length <= 120 ? s : s.Substring(0, 120);

    void OnCodeChanged()
    {
        var look = Look;
        var ap = GetComponentInChildren<CharacterAppearanceApplier>(true);
        if (ap != null) ap.Apply(look);
        var rig = GetComponentInChildren<FPArmRig>(true);
        if (rig != null) rig.Remount();      // 무기 스킨 다시
    }

    /// <summary>이 무기에 낀 스킨 (없으면 null)</summary>
    public WeaponSkinData SkinFor(int weaponId)
    {
        int id = Look.SkinFor(weaponId);
        return id > 0 ? WeaponSkins.Get(id) : null;
    }
}
