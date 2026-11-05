using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>꾸미기 · 스킨 등급 — 이름 · 색 · 확률은 GachaTable</summary>
public enum CosmeticGrade { Common, Rare, Legend }

/// <summary>
/// 꾸미기 칸 하나 (머리 · 몸 · 안경 · 모자 · 손 치장 …) — 칸마다 부위 하나만. 목록에 칸을 더 추가해도 됨
/// </summary>
[Serializable]
public class SlotDef
{
    [Tooltip("칸 키 — 부위(Part)의 Slot이 이 키를 가리킴. 정한 뒤엔 바꾸지 않기")]
    public string Key = "hat";
    [Tooltip("화면에 보이는 이름")]
    public string Name = "모자";
    [Tooltip("켜면 비울 수 없음 — 머리 · 몸 같은 기본 모델 칸")]
    public bool Required;
    [Tooltip("아무것도 안 골랐을 때 끼는 부위 Id (0 = 기본 캐릭터 모델에 들어 있는 그대로)")]
    public int DefaultPart;
    [Tooltip("부위를 붙일 뼈 이름 (부위에서 따로 안 정하면 이것 · 비우면 아래 사람형 뼈)")]
    public string Bone;
    public HumanBodyBones HumanBone = HumanBodyBones.Head;
    [Tooltip("이 칸에 부위를 끼면 숨길 기본 모델의 렌더러 이름 (예: 머리 칸 → 기본 머리 메시)")]
    public string[] HideRenderers;
}

/// <summary>
/// 꾸미기 부위 하나 — 세 가지 방식 (섞어도 됨)
///   1) 프리팹에 SkinnedMeshRenderer가 있으면 → 캐릭터 뼈에 그대로 입힘 (같은 뼈 이름으로 리깅된 머리 · 몸 모델)
///   2) 일반 메시 프리팹 → 뼈에 붙임 (안경 · 모자 · 헤드폰 · 팔찌)
///   3) 재질만 → 기본 모델의 렌더러 재질 바꾸기 (손 문양 · 색)
/// </summary>
[Serializable]
public class PartData
{
    [Tooltip("고유 번호 (1부터 · 저장 · 네트워크에 씀 — 바꾸지 말 것)")]
    public int Id;
    public string Name = "새 부위";
    [Tooltip("칸 키 (CosmeticDatabase의 칸 목록 — head · body · glasses · hat · hand …)")]
    public string Slot = "hat";
    public CosmeticGrade Grade = CosmeticGrade.Common;
    [Tooltip("SC 정가 (0 = 상점에서 안 팖)")]
    public int PriceSC = 300;
    [Tooltip("뽑기에서 나옴")]
    public bool InGacha = true;
    [Tooltip("처음부터 가짐 (기본 머리 등)")]
    public bool Free;
    public Sprite Icon;

    [Header("모델")]
    public GameObject Prefab;
    [Tooltip("붙일 뼈 이름 (비우면 칸의 뼈) — 리깅된 모델(SkinnedMesh)은 안 씀")]
    public string Bone;
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale = Vector3.one;

    [Header("재질 (문양 · 색)")]
    [Tooltip("재질을 바꿀 기본 모델 렌더러 이름 (예: 손 문양 → 손 메시 이름)")]
    public string[] TargetRenderers;
    [Tooltip("그 렌더러의 재질 순서대로 (비운 칸은 그대로)")]
    public Material[] Materials;

    [Header("숨기기")]
    [Tooltip("이 부위를 끼면 숨길 기본 모델 렌더러 이름 (예: 모자 → 기본 머리카락)")]
    public string[] HideRenderers;
}

/// <summary>무기 스킨 하나 — 재질만 바꾸거나, 모델을 통째로 바꿈 (모델은 WeaponRig까지 같은 구조여야 함)</summary>
[Serializable]
public class WeaponSkinData
{
    [Tooltip("고유 번호 (1부터 · 저장 · 네트워크에 씀 — 바꾸지 말 것)")]
    public int Id;
    [Tooltip("어느 무기 (EquipmentData.Id)")]
    public int WeaponId;
    public string Name = "새 스킨";
    public CosmeticGrade Grade = CosmeticGrade.Common;
    public int PriceSC = 300;
    public bool InGacha = true;
    public Sprite Icon;

    [Header("모양")]
    [Tooltip("통째로 바꿀 모델 — 원래 무기 모델과 같은 구조 (WeaponRig · 총구 · 손잡이). 비우면 재질만")]
    public GameObject ModelPrefab;
    [Tooltip("원래 모델 렌더러 순서대로 바꿀 재질 (비운 칸은 그대로)")]
    public Material[] Materials;
}

/// <summary>
/// 꾸미기 칸 · 부위 · 무기 스킨 목록 — GameMeta에 연결.
/// 번호(Id)는 저장 · 네트워크에 그대로 쓰여서 한 번 정하면 바꾸지 않기
/// </summary>
[CreateAssetMenu(menuName = "Game/Meta/Cosmetic Database", fileName = "CosmeticDatabase")]
public class CosmeticDatabase : ScriptableObject
{
    [Tooltip("꾸미기 칸 — 칸마다 부위 하나만. 순서 = 꾸미기 창 탭 순서. 더 추가해도 됨")]
    public SlotDef[] Slots = DefaultSlots();
    public PartData[] Parts = new PartData[0];
    public WeaponSkinData[] WeaponSkins = new WeaponSkinData[0];

    public static SlotDef[] DefaultSlots() => new[]
    {
        new SlotDef { Key = "head", Name = "머리", Required = true, HumanBone = HumanBodyBones.Head },
        new SlotDef { Key = "body", Name = "몸", Required = true, HumanBone = HumanBodyBones.Hips },
        new SlotDef { Key = "glasses", Name = "안경", HumanBone = HumanBodyBones.Head },
        new SlotDef { Key = "hat", Name = "모자", HumanBone = HumanBodyBones.Head },
        new SlotDef { Key = "hand", Name = "손 치장", HumanBone = HumanBodyBones.LeftHand },
    };

    public PartData Part(int id)
    {
        if (id <= 0 || Parts == null) return null;
        foreach (var p in Parts) if (p != null && p.Id == id) return p;
        return null;
    }

    public SlotDef Slot(string key)
    {
        if (Slots == null || string.IsNullOrEmpty(key)) return null;
        foreach (var s in Slots) if (s != null && s.Key == key) return s;
        return null;
    }

    public WeaponSkinData Skin(int id)
    {
        if (id <= 0 || WeaponSkins == null) return null;
        foreach (var s in WeaponSkins) if (s != null && s.Id == id) return s;
        return null;
    }

    public IEnumerable<PartData> PartsIn(string slot)
    {
        if (Parts == null) yield break;
        foreach (var p in Parts) if (p != null && p.Slot == slot) yield return p;
    }

    public IEnumerable<WeaponSkinData> SkinsFor(int weaponId)
    {
        if (WeaponSkins == null) yield break;
        foreach (var s in WeaponSkins) if (s != null && s.WeaponId == weaponId) yield return s;
    }

    /// <summary>처음부터 가진 부위인가 (Free · 칸 기본)</summary>
    public bool IsFree(int id)
    {
        var p = Part(id);
        if (p == null) return false;
        if (p.Free) return true;
        var s = Slot(p.Slot);
        return s != null && s.DefaultPart == id;
    }

    void OnValidate()
    {
        if (Slots == null || Slots.Length == 0) Slots = DefaultSlots();
    }
}
