using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Fusion;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 치트 명령 모음. 콘솔(DevConsole)이 줄을 넘기면 대상마다 그 대상의 권한자(SA)에서 실행.
///
///   /give item [id·이름]      들고 있는 칸이 비었으면 거기, 아니면 빈 칸, 다 차면 발밑에 픽업. 없으면 무작위
///   /give alter [id]          변화 붙이기 (clear = 전부 떼기)
///   /give soul [n] · stone [n] · sp [n]
///   /ch class|sub|rand|rand2 [id]   능력 바꾸기 (없으면 무작위)
///   /reroll                   랜덤 능력 다시 뽑기
///   /status 이름 [초] [수치]  예) /status burn 3 0.25 — 비우면 무작위.  /status clear
///   /dmg [n] · /heal [n] · /kill · /god · /cd
///   /round reset · /dummy [n] · /help
///   맨 뒤에 대상: @me(기본) @aim(조준한 대상) @all @others @dummies
/// </summary>
public static class DevCommands
{
    // ══════════════════ 명령 정의 ══════════════════

    public class Cmd
    {
        public string Name;
        public string[] Aliases = Array.Empty<string>();
        public string Usage;
        public string Help;
        /// <summary>true = 대상(@me 등)에게 실행. false = 전체 · 나 혼자</summary>
        public bool Targeted = true;
        public Func<Ctx, string[], string> Run;
        /// <summary>(앞 인자들, 지금 쓰는 인자 번호) → 후보 (값, 설명)</summary>
        public Func<string[], int, IEnumerable<(string value, string label)>> Complete;

        public bool Is(string s) =>
            string.Equals(s, Name, StringComparison.OrdinalIgnoreCase) ||
            Aliases.Any(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>실행 대상 하나</summary>
    public class Ctx
    {
        public GameObject Target;
        public NetworkRunner Runner;
        public T Get<T>() where T : Component => Target != null ? Target.GetComponent<T>() : null;
        public PlayerHealth Health => Get<PlayerHealth>();
        public PlayerStatus Status => Get<PlayerStatus>();
        public PlayerEquipment Equipment => Get<PlayerEquipment>();
        public PlayerEnchant Enchant => Get<PlayerEnchant>();
        public PlayerAlter Alter => Get<PlayerAlter>();
        public AbilityRuntime Ability => Get<AbilityRuntime>();
        public string Who => Target != null ? Target.name : "?";
    }

    static List<Cmd> _cmds;
    public static IReadOnlyList<Cmd> All => _cmds ??= Build();

    public static Cmd Find(string name) => All.FirstOrDefault(c => c.Is(name));

    static readonly string[] Targets = { "@me", "@aim", "@all", "@others", "@dummies" };

    static List<Cmd> Build() => new()
    {
        new Cmd
        {
            Name = "help", Aliases = new[] { "?", "도움말" }, Targeted = false,
            Usage = "/help [명령]", Help = "명령 목록",
            Run = (c, a) => HelpText(a.Length > 0 ? a[0] : null),
            Complete = (a, i) => i == 0 ? All.Select(x => (x.Name, x.Help)) : null,
        },
        new Cmd
        {
            Name = "give", Aliases = new[] { "줘" },
            Usage = "/give item|alter|soul|stone|sp [값]", Help = "아이템 · 변화 · 소울 · 스톤 · 강화 포인트",
            Run = Give,
            Complete = (a, i) => i == 0
                ? new[] { ("item", "아이템 (무기 · 방어구)"), ("alter", "변화"), ("soul", "소울"), ("stone", "인챈트 스톤"), ("sp", "강화 포인트") }
                : i == 1 ? GiveArg(a[0]) : null,
        },
        new Cmd
        {
            Name = "ch", Aliases = new[] { "change", "바꾸기" },
            Usage = "/ch class|sub|rand|rand2 [id]", Help = "능력 바꾸기 (비우면 무작위)",
            Run = Change,
            Complete = (a, i) => i == 0
                ? new[] { ("class", "메인 능력 (직업)"), ("sub", "서브 능력"), ("rand", "랜덤 능력 (F)"), ("rand2", "두 번째 랜덤 (V)") }
                : i == 1 ? AbilityList(a[0]) : null,
        },
        new Cmd
        {
            Name = "reroll", Aliases = new[] { "랜능" },
            Usage = "/reroll", Help = "랜덤 능력 다시 뽑기",
            Run = (c, a) =>
            {
                var ar = c.Ability;
                if (ar == null) return Err("AbilityRuntime 없음");
                ar.RerollRandomAbility();
                var r = ar.GetRandomAbility();
                return Ok($"{c.Who} 랜덤 능력 → {(r != null ? Name(r) : "없음")}");
            },
        },
        new Cmd
        {
            Name = "job", Aliases = new[] { "기믹", "직업" },
            Usage = "/job 명령 [값]", Help = "직업별 기믹 (예: 죽음의 기사 /job name 아서) — /job help",
            Run = Job,
            Complete = JobComplete,
        },
        new Cmd
        {
            Name = "status", Aliases = new[] { "st", "상태" },
            Usage = "/status 이름 [초] [수치]  ·  /status clear", Help = "상태이상 걸기 (비우면 무작위)",
            Run = Status,
            Complete = (a, i) => i == 0
                ? StatusNames.Select(s => (s.key, $"{s.kr}  {s.hint}")).Prepend(("clear", "모두 해제"))
                : i == 1 ? new[] { ("3", "초"), ("5", "초"), ("10", "초") }
                : i == 2 ? StatusValueHint(a[0]) : null,
        },
        new Cmd
        {
            Name = "cleanse", Aliases = new[] { "clear", "해제" },
            Usage = "/cleanse", Help = "상태이상 모두 해제",
            Run = (c, a) => Status(c, new[] { "clear" }),
        },
        new Cmd
        {
            Name = "dmg", Aliases = new[] { "damage", "자해" },
            Usage = "/dmg [피해=100]", Help = "피해 (방어 무시)",
            Run = (c, a) =>
            {
                var h = c.Health;
                if (h == null) return Err("PlayerHealth 없음");
                int n = Mathf.Max(1, Int(a, 0, 100));
                var info = DamageInfo.FromEnvironment(n, c.Target.transform.position + Vector3.forward);
                info.IgnoreDefense = true;
                h.RPC_TakeDamage(info);
                return Ok($"{c.Who} 피해 {n}");
            },
        },
        new Cmd
        {
            Name = "heal", Aliases = new[] { "회복" },
            Usage = "/heal [양] (비우면 최대)", Help = "회복",
            Run = (c, a) =>
            {
                var h = c.Health;
                if (h == null) return Err("PlayerHealth 없음");
                var stats = c.Get<PlayerStats>();
                int full = stats != null ? Mathf.RoundToInt(stats.Get(StatType.MaxHP)) : 9999;
                int n = Int(a, 0, full);
                h.RPC_Heal(n);
                return Ok($"{c.Who} 회복 {n}");
            },
        },
        new Cmd
        {
            Name = "kill", Aliases = new[] { "죽기" },
            Usage = "/kill", Help = "즉사 (회피 · 고스트도 풀고)",
            Run = (c, a) =>
            {
                var h = c.Health;
                if (h == null) return Err("PlayerHealth 없음");
                c.Status?.ClearAll();
                var info = DamageInfo.FromEnvironment(999999, c.Target.transform.position);
                info.IgnoreDefense = true;
                h.RPC_TakeDamage(info);
                return Ok($"{c.Who} 처치");
            },
        },
        new Cmd
        {
            Name = "god", Aliases = new[] { "무적" },
            Usage = "/god", Help = "무적 켜기/끄기 (회피 상태)",
            Run = (c, a) =>
            {
                var st = c.Status;
                if (st == null) return Err("PlayerStatus 없음");
                if (st.Has(StatusType.Evade)) { st.ClearAll(); return Ok($"{c.Who} 무적 끔 (상태이상 전부 해제됨)"); }
                st.Apply(StatusType.Evade, 1f, 99999f, PlayerRef.None);
                return Ok($"{c.Who} 무적 켬");
            },
        },
        new Cmd
        {
            Name = "cd", Aliases = new[] { "cooldown", "쿨" },
            Usage = "/cd", Help = "모든 스킬 쿨타임 초기화",
            Run = (c, a) =>
            {
                var ar = c.Ability;
                if (ar == null) return Err("AbilityRuntime 없음");
                for (int i = 0; i < SkillSlotExt.Count; i++)
                {
                    ar.CooldownEnd.Set(i, 0f);
                    ar.HoldUntil.Set(i, 0f);
                }
                return Ok($"{c.Who} 쿨타임 초기화");
            },
        },
        new Cmd
        {
            Name = "round", Aliases = new[] { "라운드" }, Targeted = false,
            Usage = "/round reset", Help = "모두 라운드 처음 상태로",
            Run = (c, a) =>
            {
                int n = 0;
                foreach (var h in UnityEngine.Object.FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                {
                    if (h == null || h.Object == null || !h.Object.IsValid) continue;
                    h.RPC_ResetForNewRound();
                    n++;
                }
                return Ok($"라운드 초기화 — {n}명");
            },
            Complete = (a, i) => i == 0 ? new[] { ("reset", "초기화") } : null,
        },
        new Cmd
        {
            Name = "outline", Aliases = new[] { "아웃라인" }, Targeted = false,
            Usage = "/outline visible|xray|off", Help = "아웃라인 방식 (이 화면) — xray = 벽 너머로도",
            Run = (c, a) =>
            {
                string m = a.Length > 0 ? a[0].ToLowerInvariant() : "";
                PlayerOutline.GlobalMode = m switch
                {
                    "xray" or "벽" => PlayerOutline.OutlineMode.XRay,
                    "off" or "끔" => PlayerOutline.OutlineMode.Off,
                    _ => PlayerOutline.OutlineMode.Visible,
                };
                return Ok($"아웃라인: {PlayerOutline.GlobalMode}");
            },
            Complete = (a, i) => i == 0 ? new[] { ("visible", "보이는 부분만 (기본)"), ("xray", "벽 너머로도"), ("off", "끔") } : null,
        },
        new Cmd
        {
            Name = "dummy", Aliases = new[] { "더미" }, Targeted = false,
            Usage = "/dummy [개수=1]", Help = "앞에 더미 만들기 (설정에 DummyPrefab 필요)",
            Run = SpawnDummy,
        },
    };

    // ══════════════════ 실행 ══════════════════

    /// <summary>권한자(SA) 쪽에서 — 이 오브젝트에게 명령 실행 (대상 표시는 이미 빠진 줄)</summary>
    public static string ExecuteOnSelf(GameObject self, string line)
    {
        var tok = Tokenize(line);
        if (tok.Count == 0) return "";
        var cmd = Find(tok[0]);
        if (cmd == null) return Err($"모르는 명령: {tok[0]}");
        var no = self.GetComponent<NetworkObject>();
        var ctx = new Ctx { Target = self, Runner = no != null ? no.Runner : null };
        try { return cmd.Run(ctx, tok.Skip(1).ToArray()); }
        catch (Exception e) { Debug.LogException(e); return Err($"{cmd.Name} 실패: {e.Message}"); }
    }

    public static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return list;
        line = line.Trim();
        if (line.StartsWith("/")) line = line.Substring(1);
        foreach (var t in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) list.Add(t);
        return list;
    }

    // ══════════════════ give ══════════════════

    static string Give(Ctx c, string[] a)
    {
        if (a.Length == 0) return Err("무엇을? item · alter · soul · stone · sp");
        string kind = a[0].ToLowerInvariant();
        string arg = a.Length > 1 ? a[1] : null;

        switch (kind)
        {
            case "item": case "아이템": case "무기": return GiveItem(c, arg);
            case "alter": case "변화": return GiveAlter(c, arg);
            case "soul":
            case "소울":
                {
                    var h = c.Health;
                    if (h == null) return Err("PlayerHealth 없음");
                    int n = Int(a, 1, 100);
                    h.RPC_GainSoul(n);
                    return Ok($"{c.Who} 소울 {n:+#;-#;0}");
                }
            case "stone":
            case "스톤":
                {
                    var en = c.Enchant;
                    if (en == null) return Err("PlayerEnchant 없음");
                    int n = Int(a, 1, 10);
                    en.RPC_AddStones(n);
                    return Ok($"{c.Who} 스톤 {n:+#;-#;0}");
                }
            case "sp":
            case "포인트":
                {
                    var ar = c.Ability;
                    if (ar == null) return Err("AbilityRuntime 없음");
                    int n = Int(a, 1, 1);
                    ar.AddSkillPoints(n);
                    return Ok($"{c.Who} 강화 포인트 {n:+#;-#;0}");
                }
        }
        return Err($"give 뒤에는 item · alter · soul · stone · sp ('{kind}' 모름)");
    }

    static string GiveItem(Ctx c, string arg)
    {
        var eq = c.Equipment;
        var db = Db.Items;
        if (eq == null || db == null || db.All == null) return Err("PlayerEquipment · EquipmentDatabase 없음");

        EquipmentData d;
        if (string.IsNullOrEmpty(arg))
        {
            var pool = db.All.Where(x => x != null && x.Slot == EquipSlot.Weapon && !x.IsUnarmed).ToArray();
            d = pool.Length > 0 ? pool[Random.Range(0, pool.Length)] : null;
        }
        else d = FindBy(db.All, arg, x => x.Id, x => ItemName(x));
        if (d == null) return Err($"아이템 '{arg}' 없음");

        if (d.Slot != EquipSlot.Weapon)
        {
            eq.Equip(d.Id);
            return Ok($"{c.Who} 장착: {ItemName(d)} ({d.Id})");
        }

        // 들고 있는 칸이 비었으면 거기 → 빈 칸 → 다 차면 발밑에 픽업
        int active = eq.ActiveWeaponSlot;
        int cur = eq.GetWeaponIdAt(active);
        var curData = cur != 0 ? db.Get(cur) : null;
        if (cur == 0 || (curData != null && curData.IsUnarmed))
        {
            eq.ReplaceWeaponAt(active, d.Id);
            return Ok($"{c.Who} 손에: {ItemName(d)} ({d.Id})");
        }
        if (eq.PickUpWeapon(d.Id, out int slot))
            return Ok($"{c.Who} {slot + 1}번 칸에: {ItemName(d)} ({d.Id})");

        eq.DropWeapon(d.Id);
        return Ok($"{c.Who} 칸이 가득 — 앞에 픽업으로: {ItemName(d)} ({d.Id})");
    }

    static string GiveAlter(Ctx c, string arg)
    {
        var pa = c.Alter;
        var db = Db.Alters;
        if (pa == null || db == null || db.All == null) return Err("PlayerAlter · AlterDatabase 없음");

        if (arg != null && (arg == "clear" || arg == "해제"))
        {
            pa.ClearAll();
            return Ok($"{c.Who} 변화 전부 뗌");
        }

        AlterData a = string.IsNullOrEmpty(arg)
            ? db.All.Where(x => x != null).OrderBy(_ => Random.value).FirstOrDefault()
            : FindBy(db.All, arg, x => x.Id, x => x.LocalizedName);
        if (a == null) return Err($"변화 '{arg}' 없음");

        pa.Add(a);
        return Ok($"{c.Who} 변화: {a.LocalizedName} ({a.Id})");
    }

    // ══════════════════ ch — 능력 바꾸기 ══════════════════

    static string Change(Ctx c, string[] a)
    {
        if (a.Length == 0) return Err("무엇을? class · sub · rand · rand2");
        int kind = KindOf(a[0]);
        if (kind < 0) return Err($"ch 뒤에는 class · sub · rand · rand2 ('{a[0]}' 모름)");

        var ar = c.Ability;
        var db = Db.Abilities;
        if (ar == null || db == null) return Err("AbilityRuntime · AbilityDatabase 없음");

        var pool = Pool(db, kind);
        string arg = a.Length > 1 ? a[1] : null;
        int curId = kind switch { 0 => ar.MainAbilityId, 1 => ar.SubAbilityId, 2 => ar.RandomAbilityId, _ => ar.RandomAbilityId2 };

        AbilityData pick;
        if (string.IsNullOrEmpty(arg))
        {
            var cand = pool.Where(x => x != null && x.Id != curId).ToArray();
            pick = cand.Length > 0 ? cand[Random.Range(0, cand.Length)] : null;
        }
        else pick = FindBy(pool, arg, x => x.Id, Name) ?? FindBy(db.All, arg, x => x.Id, Name);
        if (pick == null) return Err($"능력 '{arg}' 없음");

        SafeSwap(c, ar, kind, pick.Id);
        string what = kind switch { 0 => "직업", 1 => "서브", 2 => "랜덤", _ => "랜덤2" };
        return Ok($"{c.Who} {what} → {Name(pick)} ({pick.Id})");
    }

    static int KindOf(string s) => s.ToLowerInvariant() switch
    {
        "class" or "main" or "직업" or "메인" => 0,
        "sub" or "서브" => 1,
        "rand" or "random" or "랜덤" or "랜능" => 2,
        "rand2" or "random2" or "랜덤2" => 3,
        _ => -1,
    };

    static AbilityData[] Pool(AbilityDatabase db, int kind) =>
        (kind switch { 0 => db.MainAbilities, 1 => db.SubAbilities, _ => db.RandomAbilities }) ?? db.All ?? Array.Empty<AbilityData>();

    /// <summary>
    /// 능력을 안전하게 교체 — 시전 취소 · 쿨타임 · (직업이면) 레벨 · 게이지 · 조커 랜덤2 · 무기 칸을 정리한 뒤
    /// 바로 다시 빌드 (이전 Behaviour의 OnUnequip → 새 Behaviour의 OnEquip)
    /// </summary>
    static void SafeSwap(Ctx c, AbilityRuntime ar, int kind, int id)
    {
        Call(ar, "CancelCast");
        for (int i = 0; i < SkillSlotExt.Count; i++)
        {
            ar.CooldownEnd.Set(i, 0f);
            ar.HoldUntil.Set(i, 0f);
            ar.HeldCooldown.Set(i, 0f);
        }

        if (kind == 0 || kind == 1)
        {
            int owner = kind == 0 ? AbilityRuntime.OwnerMain : AbilityRuntime.OwnerSub;
            for (int l = 0; l < AbilityRuntime.EntriesPerAbility; l++)
                ar.EntryRanks.Set(AbilityRuntime.EntryIndex(owner, l), 0);
        }

        if (kind == 0)
        {
            for (int i = 0; i < ar.Charges.Length; i++) { ar.Charges.Set(i, 0f); ar.MaxCharges.Set(i, 0f); }
            ar.ActiveStateId = 0;
            ar.StateTimer = default;
            ar.MoveLock = AbilityRuntime.MoveLockMode.None;
            if (ar.DualRandom) { ar.DualRandom = false; ar.RandomAbilityId2 = 0; }   // 조커면 새 OnEquip이 다시 켬

            var eq = c.Equipment;
            if (eq != null && eq.UsableSlots > PlayerEquipment.DefaultSlotCount)
                eq.SetSlotCount(PlayerEquipment.DefaultSlotCount);                    // 무구의 달인 칸 (넘치는 무기는 바닥에)
        }

        switch (kind)
        {
            case 0: ar.MainAbilityId = id; break;
            case 1: ar.SubAbilityId = id; break;
            case 2: ar.RandomAbilityId = id; break;
            case 3:
                if (!ar.DualRandom) ar.DualRandom = true;
                ar.RandomAbilityId2 = id;
                break;
        }

        Call(ar, "RebuildAbilities");       // 권한자는 바로 (다른 화면은 OnChangedRender로)
    }

    static void Call(object o, string method)
    {
        var mi = o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                       null, Type.EmptyTypes, null);
        if (mi == null) { Debug.LogWarning($"[DevCommands] {o.GetType().Name}.{method} 없음 — 건너뜀"); return; }
        mi.Invoke(o, null);
    }

    // ══════════════════ job — 직업별 기믹 ══════════════════

    static string Job(Ctx c, string[] a)
    {
        var ar = c.Ability;
        if (ar == null) return Err("AbilityRuntime 없음");
        if (a.Length == 0) a = new[] { "help" };
        bool help = a[0].ToLowerInvariant() == "help";

        var sb = new System.Text.StringBuilder();
        foreach (var b in ar.Behaviours)
        {
            if (b == null) continue;
            string r = b.DevCommand(a);
            if (r == null) continue;
            if (!help) return Ok($"{c.Who} {r}");
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("  ").Append(r);
        }
        if (help) return sb.Length > 0 ? "<b>직업 기믹</b>\n" + sb : Err("지금 능력에는 기믹 명령이 없음");
        return Err($"'{a[0]}' — 지금 능력에 그런 기믹 명령이 없음 (/job help)");
    }

    static IEnumerable<(string, string)> JobComplete(string[] a, int index)
    {
        var ar = AbilityDescTokens.Local;
        if (ar == null) return null;
        var list = new List<(string, string)>();
        if (index == 0) list.Add(("help", "이 능력들의 기믹 명령"));
        foreach (var b in ar.Behaviours)
        {
            var r = b != null ? b.DevComplete(a, index) : null;
            if (r != null) list.AddRange(r);
        }
        return list;
    }

    // ══════════════════ status ══════════════════

    static readonly (string key, StatusType type, string kr, string hint, float min, float max)[] StatusNames =
    {
        ("slow",       StatusType.Slow,       "둔화",       "수치 = 이속 감소 %",          20f, 50f),
        ("burn",       StatusType.Burn,       "화상",       "수치 = 초당 최대 체력 %",     0.5f, 2f),
        ("frost",      StatusType.Frost,      "동상",       "수치 = 공속 감소 %",          20f, 50f),
        ("seal",       StatusType.Seal,       "봉인",       "스킬 사용 불가",              1f, 1f),
        ("stun",       StatusType.Stun,       "기절",       "",                            1f, 1f),
        ("wound",      StatusType.DeepWound,  "깊은상처",   "수치 = 받는 회복 감소 %",     30f, 60f),
        ("poison",     StatusType.Poison,     "중독",       "수치 = 중첩당 초당 최대 체력 %", 0.3f, 1f),
        ("evade",      StatusType.Evade,      "회피",       "",                            1f, 1f),
        ("armorbreak", StatusType.ArmorBreak, "방어력감소", "수치 = 방어력 감소 %",        20f, 40f),
        ("ghost",      StatusType.Ghost,      "고스트",     "",                            1f, 1f),
        ("blind",      StatusType.Blind,      "암전",       "수치 = 보이는 반경 m (최소 1)", 1f, 4f),
        ("root",       StatusType.Root,       "속박",       "",                            1f, 1f),
    };

    static string Status(Ctx c, string[] a)
    {
        var st = c.Status;
        if (st == null) return Err("PlayerStatus 없음");

        string name = a.Length > 0 ? a[0].ToLowerInvariant() : null;
        if (name == "clear" || name == "해제")
        {
            st.ClearAll();
            return Ok($"{c.Who} 상태이상 모두 해제");
        }

        int idx = -1;
        if (string.IsNullOrEmpty(name)) idx = Random.Range(0, StatusNames.Length);
        else
            for (int i = 0; i < StatusNames.Length; i++)
            {
                var s = StatusNames[i];
                if (s.key == name || s.kr == name || s.type.ToString().ToLowerInvariant() == name) { idx = i; break; }
            }
        if (idx < 0) return Err($"상태이상 '{a[0]}' 모름 — {string.Join(", ", StatusNames.Select(s => s.key))}");

        var def = StatusNames[idx];
        float dur = Float(a, 1, Mathf.Round(Random.Range(3f, 8f) * 10f) / 10f);
        float val = Float(a, 2, Mathf.Round(Random.Range(def.min, def.max) * 100f) / 100f);

        st.Apply(def.type, val, dur, PlayerRef.None, def.type == StatusType.Poison ? 9 : 1);
        bool flag = Mathf.Approximately(def.min, 1f) && Mathf.Approximately(def.max, 1f);
        return Ok($"{c.Who} {def.kr} {dur:0.#}초" + (flag ? "" : $" · {val:0.##}"));
    }

    static IEnumerable<(string, string)> StatusValueHint(string name)
    {
        name = name?.ToLowerInvariant();
        foreach (var s in StatusNames)
            if (s.key == name || s.kr == name)
            {
                if (Mathf.Approximately(s.min, 1f) && Mathf.Approximately(s.max, 1f)) return null;
                return new[] { (s.min.ToString(CultureInfo.InvariantCulture), s.hint), (s.max.ToString(CultureInfo.InvariantCulture), s.hint) };
            }
        return null;
    }

    // ══════════════════ 전체 명령 ══════════════════

    static string SpawnDummy(Ctx c, string[] a)
    {
        var set = DevConsoleSettings.Get();
        if (set.DummyPrefab == null) return Err("DevConsoleSettings에 DummyPrefab이 없음 (Resources/DevConsoleSettings)");
        var runner = c.Runner;
        if (runner == null || c.Target == null) return Err("네트워크 연결 전");

        int n = Mathf.Clamp(Int(a, 0, 1), 1, 10);
        var me = c.Target.transform;
        Vector3 fwd = me.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        for (int i = 0; i < n; i++)
        {
            float side = (i - (n - 1) * 0.5f) * 2f;
            Vector3 pos = me.position + fwd * 4f + right * side;
            var obj = runner.Spawn(set.DummyPrefab, pos, Quaternion.LookRotation(-fwd, Vector3.up));
            if (obj != null) MakeDummy(obj.gameObject);
        }
        return Ok($"더미 {n}개");
    }

    /// <summary>
    /// 플레이어 프리팹을 더미로 쓸 때 — 만든 사람(나)이 권한자라서 내 키보드 · 마우스 입력을 같이 받아 버림.
    /// 입력 · 카메라 쪽만 꺼서 가만히 서 있는 과녁으로. (체력 · 상태이상 · 능력 · 변화는 그대로)
    /// 다른 화면에서는 원래 입력을 안 받으니 내 쪽에서만 끄면 됨
    /// </summary>
    static void MakeDummy(GameObject go)
    {
        go.name = "Dummy_" + go.name.Replace("(Clone)", "");
        string[] inputs = { "PlayerMovement", "PlayerSkillInput", "PlayerInteract", "FPSCamera", "ThirdPersonView", "PlayerShop" };
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && Array.IndexOf(inputs, mb.GetType().Name) >= 0) mb.enabled = false;
        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
    }

    static string HelpText(string cmd)
    {
        if (!string.IsNullOrEmpty(cmd))
        {
            var c = Find(cmd);
            return c == null ? Err($"모르는 명령: {cmd}") : $"<b>{c.Usage}</b>  {c.Help}";
        }
        var sb = new System.Text.StringBuilder("<b>명령</b> (Tab 자동완성 · ↑↓ 기록 · 맨 뒤 @me @aim @all @others @dummies)\n");
        foreach (var c in All) sb.Append($"  <color=#9fd3ff>{c.Usage}</color>  {c.Help}\n");
        return sb.ToString().TrimEnd();
    }

    // ══════════════════ 자동완성 ══════════════════

    /// <summary>입력 줄 → 후보 (값, 설명). 마지막 토큰을 바꿀 값</summary>
    public static List<(string value, string label)> Suggest(string line)
    {
        var res = new List<(string, string)>();
        if (line == null) return res;
        string body = line.TrimStart();
        if (body.StartsWith("/")) body = body.Substring(1);

        var parts = body.Split(' ').ToList();          // 끝이 공백이면 마지막이 "" (새 인자)
        string cur = parts.Count > 0 ? parts[parts.Count - 1] : "";

        if (cur.StartsWith("@"))
        {
            foreach (var t in Targets) if (t.StartsWith(cur, StringComparison.OrdinalIgnoreCase)) res.Add((t, TargetLabel(t)));
            return res;
        }

        if (parts.Count <= 1)
        {
            foreach (var c in All)
                if (c.Name.StartsWith(cur, StringComparison.OrdinalIgnoreCase) || c.Aliases.Any(x => x.StartsWith(cur, StringComparison.OrdinalIgnoreCase)))
                    res.Add((c.Name, $"{c.Usage}  — {c.Help}"));
            return res;
        }

        var cmd = Find(parts[0]);
        if (cmd?.Complete == null) return res;
        var args = parts.Skip(1).Where(p => !p.StartsWith("@")).ToArray();
        int index = args.Length - 1;
        var all = cmd.Complete(args, index);
        if (all == null) return res;

        foreach (var (v, l) in all)
        {
            if (cur.Length == 0 || v.StartsWith(cur, StringComparison.OrdinalIgnoreCase) ||
                (l != null && l.IndexOf(cur, StringComparison.OrdinalIgnoreCase) >= 0))
                res.Add((v, l));
            if (res.Count >= 40) break;
        }
        return res;
    }

    static string TargetLabel(string t) => t switch
    {
        "@me" => "나 (기본)",
        "@aim" => "조준한 대상",
        "@all" => "모두 (나 포함, 더미 포함)",
        "@others" => "나 빼고 모두",
        "@dummies" => "더미만",
        _ => "",
    };

    static IEnumerable<(string, string)> GiveArg(string kind)
    {
        switch (kind?.ToLowerInvariant())
        {
            case "item":
            case "아이템":
            case "무기":
                var items = Db.Items?.All;
                return items == null ? null : items.Where(x => x != null).OrderBy(x => x.Id)
                    .Select(x => (x.Id.ToString(), $"{ItemName(x)}  ·  {x.Slot}"));
            case "alter":
            case "변화":
                var alters = Db.Alters?.All;
                return alters == null ? null : alters.Where(x => x != null).OrderBy(x => x.Id)
                    .Select(x => (x.Id.ToString(), x.LocalizedName)).Prepend(("clear", "전부 떼기"));
            case "soul": case "소울": return new[] { ("100", ""), ("500", ""), ("-50", "빼기") };
            case "stone": case "스톤": return new[] { ("10", ""), ("100", "") };
            case "sp": case "포인트": return new[] { ("1", ""), ("5", "") };
        }
        return null;
    }

    static IEnumerable<(string, string)> AbilityList(string kind)
    {
        var db = Db.Abilities;
        int k = KindOf(kind ?? "");
        if (db == null || k < 0) return null;
        return Pool(db, k).Where(x => x != null).OrderBy(x => x.Id).Select(x => (x.Id.ToString(), Name(x)));
    }

    // ══════════════════ 도우미 ══════════════════

    static T FindBy<T>(IEnumerable<T> list, string arg, Func<T, int> id, Func<T, string> name) where T : class
    {
        if (list == null || string.IsNullOrEmpty(arg)) return null;
        var arr = list.Where(x => x != null).ToArray();
        if (int.TryParse(arg, out int n)) return arr.FirstOrDefault(x => id(x) == n);
        return arr.FirstOrDefault(x => string.Equals(name(x), arg, StringComparison.OrdinalIgnoreCase))
            ?? arr.FirstOrDefault(x => (name(x) ?? "").IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    static string Name(AbilityData a) => a == null ? "" : LocUtil.GetOr($"ability.{a.Id}.name", LocUtil.Resolve(a.DisplayName));
    static string ItemName(EquipmentData d) => d == null ? "" : LocUtil.Resolve(d.DisplayName);

    static int Int(string[] a, int i, int def) =>
        a != null && i < a.Length && int.TryParse(a[i], out int v) ? v : def;

    static float Float(string[] a, int i, float def) =>
        a != null && i < a.Length && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

    public static string Ok(string s) => $"<color=#8fe388>✔</color> {s}";
    public static string Err(string s) => $"<color=#ff7a7a>✖ {s}</color>";

    /// <summary>데이터베이스 — 씬에서 쓰는 에셋을 찾아 둠 (설정 불필요)</summary>
    public static class Db
    {
        static EquipmentDatabase _items;
        static AlterDatabase _alters;
        static AbilityDatabase _abilities;
        public static EquipmentDatabase Items => _items != null ? _items : (_items = FindAsset<EquipmentDatabase>());
        public static AlterDatabase Alters => _alters != null ? _alters : (_alters = FindAsset<AlterDatabase>());
        public static AbilityDatabase Abilities => _abilities != null ? _abilities : (_abilities = FindAsset<AbilityDatabase>());

        static T FindAsset<T>() where T : ScriptableObject
        {
            var found = Resources.FindObjectsOfTypeAll<T>();
            return found != null && found.Length > 0 ? found[0] : null;
        }
    }
}