using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 꾸미기 한 벌 — 낀 부위 번호들(칸마다 하나) + 무기마다 스킨 번호.
/// 글자로 바꿔서 네트워크 · 사진 캐시에 씀: "12.40.7" (부위만, 대기실) / "12.40.7|무기:스킨,무기:스킨" (게임 안).
/// 부위가 어느 칸인지는 CosmeticDatabase에서 (번호만 저장 → 칸을 더 늘려도 그대로)
/// </summary>
public struct Appearance
{
    public List<int> Parts;
    public List<(int weapon, int skin)> Skins;

    static CosmeticDatabase Db => GameMeta.Get() != null ? GameMeta.Get().Cosmetics : null;

    /// <summary>이 칸에 낀 부위 (없으면 0 — 칸 기본은 CharacterAppearanceApplier가 채움)</summary>
    public int Get(string slot)
    {
        var db = Db;
        if (Parts == null || db == null) return 0;
        foreach (var id in Parts)
        {
            var p = db.Part(id);
            if (p != null && p.Slot == slot) return id;
        }
        return 0;
    }

    /// <summary>이 칸을 바꿈 (0 = 비우기)</summary>
    public void Set(string slot, int id)
    {
        Parts ??= new List<int>();
        var db = Db;
        if (db != null) Parts.RemoveAll(x => db.Part(x) is { } p && p.Slot == slot);
        if (id > 0 && !Parts.Contains(id)) Parts.Add(id);
    }

    public int SkinFor(int weaponId)
    {
        if (Skins != null) foreach (var (w, s) in Skins) if (w == weaponId) return s;
        return 0;
    }

    /// <summary>부위만 (대기실 명단 — 30자 안, 번호 순서대로)</summary>
    public string PartsCode
    {
        get
        {
            if (Parts == null || Parts.Count == 0) return "";
            var l = new List<int>(Parts);
            l.Sort();
            return string.Join(".", l);
        }
    }

    /// <summary>부위 + 무기 스킨 (게임 안)</summary>
    public string FullCode
    {
        get
        {
            var sb = new StringBuilder(PartsCode);
            if (Skins != null && Skins.Count > 0)
            {
                sb.Append('|');
                for (int i = 0; i < Skins.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Skins[i].weapon).Append(':').Append(Skins[i].skin);
                }
            }
            return sb.ToString();
        }
    }

    public static Appearance Parse(string code)
    {
        var a = new Appearance { Parts = new List<int>(), Skins = new List<(int, int)>() };
        if (string.IsNullOrEmpty(code)) return a;
        var halves = code.Split('|');
        foreach (var t in halves[0].Split('.'))
            if (int.TryParse(t, out var v) && v > 0 && !a.Parts.Contains(v)) a.Parts.Add(v);
        if (halves.Length > 1)
            foreach (var pair in halves[1].Split(','))
            {
                var kv = pair.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out var w) && int.TryParse(kv[1], out var s)) a.Skins.Add((w, s));
            }
        return a;
    }

    /// <summary>내 저장에서 (장착한 것 — 가진 것만)</summary>
    public static Appearance FromSave()
    {
        var a = new Appearance { Parts = new List<int>(), Skins = new List<(int, int)>() };
        foreach (var p in PlayerSave.Data.EquippedParts)
            if (p.Id > 0 && PlayerSave.OwnsPart(p.Id) && !a.Parts.Contains(p.Id)) a.Parts.Add(p.Id);
        foreach (var w in PlayerSave.Data.EquippedSkins)
            if (w.SkinId > 0 && PlayerSave.OwnsSkin(w.SkinId)) a.Skins.Add((w.WeaponId, w.SkinId));
        return a;
    }
}
