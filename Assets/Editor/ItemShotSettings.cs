#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>사진 종류 — 종류마다 기본 각도 · 조명이 따로</summary>
public enum ShotKind { Gun, Melee, Ranged, Armor, Helmet, Boots }

/// <summary>각도 — 자동(가장 긴 쪽을 눕혀서) 4가지 + 직접 돌린 것</summary>
public enum ShotAngle { Diagonal, Horizontal, Vertical, Front, Custom }

/// <summary>조명 3개 (정면 · 보조 · 뒤 테두리) + 주변광. 방향은 좌우(Yaw) · 위아래(Pitch) 각도</summary>
[Serializable]
public class ShotLight
{
    [Header("정면 (주 조명)")]
    public float KeyYaw = -35f;
    public float KeyPitch = 40f;
    [Range(0f, 3f)] public float KeyIntensity = 1.2f;
    public Color KeyColor = new(1f, 0.97f, 0.92f, 1f);

    [Header("보조 (그림자 쪽 채우기)")]
    public float FillYaw = 140f;
    public float FillPitch = 10f;
    [Range(0f, 3f)] public float FillIntensity = 0.45f;
    public Color FillColor = new(0.75f, 0.85f, 1f, 1f);

    [Header("뒤 테두리 (윤곽 빛)")]
    public float RimYaw = 180f;
    public float RimPitch = 25f;
    [Range(0f, 3f)] public float RimIntensity = 0.9f;
    public Color RimColor = Color.white;

    [Header("주변광")]
    public Color Ambient = new(0.42f, 0.42f, 0.48f, 1f);

    public ShotLight Clone() => (ShotLight)MemberwiseClone();
}

/// <summary>한 장의 모습 — 각도 · 화면 돌림 · 확대 · 위치 · 조명</summary>
[Serializable]
public class ShotLook
{
    public ShotAngle Angle = ShotAngle.Diagonal;
    [Tooltip("각도가 '직접'일 때 모델 회전")]
    public Vector3 Euler;
    [Tooltip("자동 각도 위에 더하는 화면 돌림 (도)")]
    public float Spin;
    [Tooltip("1 = 꽉 차게 (여백 조금)")]
    [Range(0.4f, 2.5f)] public float Zoom = 1f;
    [Tooltip("화면 위치 (-1 ~ 1)")]
    public Vector2 Offset;
    public ShotLight Light = new();

    public ShotLook Clone()
    {
        var c = (ShotLook)MemberwiseClone();
        c.Light = Light != null ? Light.Clone() : new ShotLight();
        return c;
    }
}

/// <summary>아이템 하나 — 따로 맞춘 모습(OwnLook) · 찍을 때의 모델 해시 (모델이 바뀌었는지 보려고)</summary>
[Serializable]
public class ShotEntry
{
    public int Id;
    [Tooltip("켜짐 = 이 아이템만 따로 맞춘 모습 / 꺼짐 = 종류 기본값")]
    public bool OwnLook;
    public ShotLook Look = new();
    public string ModelHash;
}

/// <summary>
/// 아이템 사진관 설정 (에디터 전용) — Assets/UI/Game/ItemShots/ItemShotSettings.asset
/// 종류별 기본 모습 + 아이템마다 따로 맞춘 모습 + 저장 방식(크기 · 외곽선)
/// </summary>
public class ItemShotSettings : ScriptableObject
{
    [Header("저장")]
    [Tooltip("그림 크기 (px)")]
    public int Size = 512;
    [Tooltip("크게 찍고 줄여서 계단 현상 줄이기 (2 = 2배로 찍음)")]
    [Range(1, 4)] public int Supersample = 2;
    [Tooltip("카툰 외곽선 (v7 UI)")]
    public bool Outline = true;
    [Range(1f, 20f)] public float OutlineWidth = 6f;
    public Color OutlineColor = new(0.067f, 0.067f, 0.067f, 1f);

    [Header("종류별 기본값")]
    public ShotLook Gun = new();
    public ShotLook Melee = new();
    public ShotLook Ranged = new();
    public ShotLook Armor = new() { Angle = ShotAngle.Front };
    public ShotLook Helmet = new() { Angle = ShotAngle.Front };
    public ShotLook Boots = new() { Angle = ShotAngle.Front };

    [Header("아이템마다")]
    public List<ShotEntry> Items = new();

    public static ShotKind KindOf(EquipmentData d)
    {
        if (d == null) return ShotKind.Melee;
        switch (d.Slot)
        {
            case EquipSlot.Armor: return ShotKind.Armor;
            case EquipSlot.Helmet: return ShotKind.Helmet;
            case EquipSlot.Boots: return ShotKind.Boots;
        }
        if (d.Class == WeaponClass.Melee) return ShotKind.Melee;
        return d.RangedKind == RangedType.Gun ? ShotKind.Gun : ShotKind.Ranged;
    }

    public static string KindName(ShotKind k) => k switch
    {
        ShotKind.Gun => "총",
        ShotKind.Melee => "근접 무기",
        ShotKind.Ranged => "활 · 지팡이 · 던지기",
        ShotKind.Armor => "갑옷",
        ShotKind.Helmet => "투구",
        ShotKind.Boots => "신발",
        _ => k.ToString(),
    };

    public ShotLook KindLook(ShotKind k)
    {
        ShotLook l = k switch
        {
            ShotKind.Gun => Gun,
            ShotKind.Melee => Melee,
            ShotKind.Ranged => Ranged,
            ShotKind.Armor => Armor,
            ShotKind.Helmet => Helmet,
            _ => Boots,
        };
        if (l == null) l = new ShotLook();
        if (l.Light == null) l.Light = new ShotLight();
        return l;
    }

    public ShotEntry Find(int id, bool create)
    {
        foreach (var e in Items) if (e != null && e.Id == id) return e;
        if (!create) return null;
        var n = new ShotEntry { Id = id };
        Items.Add(n);
        return n;
    }

    /// <summary>이 아이템을 찍을 모습 — 따로 맞춘 게 있으면 그것, 없으면 종류 기본값</summary>
    public ShotLook LookFor(EquipmentData d)
    {
        var e = d != null ? Find(d.Id, false) : null;
        if (e != null && e.OwnLook && e.Look != null)
        {
            if (e.Look.Light == null) e.Look.Light = new ShotLight();
            return e.Look;
        }
        return KindLook(KindOf(d));
    }
}
#endif
