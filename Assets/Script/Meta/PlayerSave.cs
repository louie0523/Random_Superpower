using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>저장 내용 — 지금은 내 PC 파일 (JSON). 나중에 스팀 · 서버로 바꿀 때 ISaveStore만 바꿈</summary>
[Serializable]
public class SaveData
{
    public const int Current = 2;     // 2 = 꾸미기 칸이 글자 키 (head · hat …)
    public int Version = Current;
    public int SC;
    public int Tickets;

    public List<int> OwnedParts = new();
    public List<int> OwnedSkins = new();
    public List<SlotPick> EquippedParts = new();
    public List<WeaponPick> EquippedSkins = new();

    [Tooltip("도감 — 한 번이라도 가져 본 능력 Id")]
    public List<int> SeenAbilities = new();

    public int Matches, Wins, Kills;
    public List<AbilityRecord> Classes = new();     // 메인 클래스별
    public List<AbilityRecord> Randoms = new();     // 랜덤 능력별
    public int LastRound = -1;                      // 같은 판을 두 번 기록하지 않게

    [Serializable] public class SlotPick { public string Slot; public int Id; }
    [Serializable] public class WeaponPick { public int WeaponId; public int SkinId; }
}

[Serializable]
public class AbilityRecord
{
    public int AbilityId;
    public int Plays, Wins, Kills;
    public int MaxSoul, TotalSoul;
    public float WinRate => Plays > 0 ? Wins / (float)Plays : 0f;
    public float AvgSoul => Plays > 0 ? TotalSoul / (float)Plays : 0f;
}

/// <summary>저장소 — 바꿔 끼울 수 있게 (로컬 파일 · 스팀 · 서버)</summary>
public interface ISaveStore
{
    SaveData Load();
    void Save(SaveData d);
}

/// <summary>내 PC 파일 — Application.persistentDataPath/meta_save.json</summary>
public class LocalSaveStore : ISaveStore
{
    public string Path => System.IO.Path.Combine(Application.persistentDataPath, "meta_save.json");

    public SaveData Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path)) ?? new SaveData();
                if (d.Version < 2) d.EquippedParts.Clear();       // 예전 칸(Head · Body · Face · Back 숫자) → 장착만 비움 (가진 건 그대로)
                d.Version = SaveData.Current;
                return d;
            }
        }
        catch (Exception e) { Debug.LogWarning("[저장] 읽기 실패 — 새로 시작\n" + e.Message); }
        return new SaveData();
    }

    public void Save(SaveData d)
    {
        try
        {
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
            if (File.Exists(Path)) File.Delete(Path);
            File.Move(tmp, Path);
        }
        catch (Exception e) { Debug.LogWarning("[저장] 쓰기 실패\n" + e.Message); }
    }
}

/// <summary>
/// 내 저장 (SC · 티켓 · 가진 것 · 장착 · 도감 · 기록). 바꾸면 Commit() → 저장 + Changed 알림
/// </summary>
public static class PlayerSave
{
    public static ISaveStore Store = new LocalSaveStore();
    static SaveData _d;

    public static SaveData Data => _d ??= Store.Load();
    public static event Action Changed;

    public static void Commit()
    {
        Store.Save(Data);
        Changed?.Invoke();
    }

    // ── 돈 ──
    public static int SC => Data.SC;
    public static int Tickets => Data.Tickets;
    public static void AddSC(int n) { Data.SC = Mathf.Max(0, Data.SC + n); }
    public static void AddTickets(int n) { Data.Tickets = Mathf.Max(0, Data.Tickets + n); }

    public static bool TrySpendSC(int n)
    {
        if (n < 0 || Data.SC < n) return false;
        Data.SC -= n;
        return true;
    }

    public static bool TrySpendTickets(int n)
    {
        if (n < 0 || Data.Tickets < n) return false;
        Data.Tickets -= n;
        return true;
    }

    // ── 가진 것 · 장착 ──
    public static bool OwnsPart(int id)
    {
        if (id <= 0 || Data.OwnedParts.Contains(id)) return true;
        var db = GameMeta.Get() != null ? GameMeta.Get().Cosmetics : null;
        return db != null && db.IsFree(id);       // 처음부터 가진 것 (기본 머리 등)
    }
    public static bool OwnsSkin(int id) => id <= 0 || Data.OwnedSkins.Contains(id);
    public static void GrantPart(int id) { if (id > 0 && !Data.OwnedParts.Contains(id)) Data.OwnedParts.Add(id); }
    public static void GrantSkin(int id) { if (id > 0 && !Data.OwnedSkins.Contains(id)) Data.OwnedSkins.Add(id); }

    public static int EquippedPart(string s)
    {
        foreach (var p in Data.EquippedParts) if (p.Slot == s) return p.Id;
        return 0;
    }

    public static void EquipPart(string s, int id)
    {
        Data.EquippedParts.RemoveAll(p => p.Slot == s);
        if (id > 0) Data.EquippedParts.Add(new SaveData.SlotPick { Slot = s, Id = id });
    }

    public static int EquippedSkin(int weaponId)
    {
        foreach (var p in Data.EquippedSkins) if (p.WeaponId == weaponId) return p.SkinId;
        return 0;
    }

    public static void EquipSkin(int weaponId, int skinId)
    {
        Data.EquippedSkins.RemoveAll(p => p.WeaponId == weaponId);
        if (skinId > 0) Data.EquippedSkins.Add(new SaveData.WeaponPick { WeaponId = weaponId, SkinId = skinId });
    }

    // ── 도감 ──
    public static bool Seen(int abilityId) => Data.SeenAbilities.Contains(abilityId);

    /// <summary>처음 보는 능력이면 기록하고 true</summary>
    public static bool See(int abilityId)
    {
        if (abilityId <= 0 || Data.SeenAbilities.Contains(abilityId)) return false;
        Data.SeenAbilities.Add(abilityId);
        return true;
    }

    // ── 기록 ──
    public static AbilityRecord Record(List<AbilityRecord> list, int id)
    {
        foreach (var r in list) if (r.AbilityId == id) return r;
        var n = new AbilityRecord { AbilityId = id };
        list.Add(n);
        return n;
    }

    /// <summary>내 꾸미기 (네트워크 · 사진용)</summary>
    public static Appearance Look => Appearance.FromSave();
}
