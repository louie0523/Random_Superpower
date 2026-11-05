using System;
using UnityEngine;

[Serializable]
public class GradeInfo
{
    public CosmeticGrade Grade;
    public string Name = "일반";
    public Color Color = Color.white;
    [Tooltip("뽑기 확률 가중치 (합에서 차지하는 비율)")]
    public float Weight = 70f;
    [Tooltip("이미 가진 걸 뽑으면 돌려주는 SC")]
    public int RefundSC = 20;
}

/// <summary>뽑기 — 등급 확률 · 가격 · 중복 환급 (임시값 — 여기서 고치기)</summary>
[CreateAssetMenu(menuName = "Game/Meta/Gacha Table", fileName = "GachaTable")]
public class GachaTable : ScriptableObject
{
    public GradeInfo[] Grades =
    {
        new() { Grade = CosmeticGrade.Common, Name = "일반", Color = new Color(0.78f, 0.82f, 0.86f), Weight = 70f, RefundSC = 20 },
        new() { Grade = CosmeticGrade.Rare, Name = "희귀", Color = new Color(0.25f, 0.66f, 1f), Weight = 25f, RefundSC = 60 },
        new() { Grade = CosmeticGrade.Legend, Name = "전설", Color = new Color(1f, 0.82f, 0.12f), Weight = 5f, RefundSC = 200 },
    };

    [Header("가격")]
    public int SingleSC = 100;
    public int TenSC = 900;
    public int SingleTickets = 1;
    public int TenTickets = 10;

    public GradeInfo Info(CosmeticGrade g)
    {
        if (Grades != null) foreach (var x in Grades) if (x != null && x.Grade == g) return x;
        return new GradeInfo { Grade = g, Name = g.ToString(), Weight = 0f };
    }

    /// <summary>등급 하나 뽑기 (가중치)</summary>
    public CosmeticGrade RollGrade(System.Random r)
    {
        float sum = 0f;
        foreach (var x in Grades) if (x != null) sum += Mathf.Max(0f, x.Weight);
        if (sum <= 0f) return CosmeticGrade.Common;
        float v = (float)r.NextDouble() * sum;
        foreach (var x in Grades)
        {
            if (x == null) continue;
            v -= Mathf.Max(0f, x.Weight);
            if (v <= 0f) return x.Grade;
        }
        return Grades[Grades.Length - 1].Grade;
    }
}
