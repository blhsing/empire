using System.Collections.ObjectModel;

namespace Empire.Core;

public sealed record UnitDefinition(
    string Key,
    string Name,
    string Glyph,
    double Hp,
    double Damage,
    double Armor,
    double Speed,
    double Range,
    double Cooldown,
    IReadOnlyDictionary<string, double> Cost,
    double TrainTime,
    int Population,
    int Age,
    string Role,
    string Description)
{
    public IReadOnlyDictionary<string, double> Bonus { get; init; } = GameData.EmptyValues;
    public double Splash { get; init; }
    public string? UniqueCivilization { get; init; }
    public string? TrainAt { get; init; }
    public bool IsRanged { get; init; }
    public double Sight { get; init; } = 230;
    public double Regeneration { get; init; }
}

public sealed record BuildingDefinition(
    string Key,
    string Name,
    string Glyph,
    double Hp,
    double Size,
    IReadOnlyDictionary<string, double> Cost,
    int Age,
    string Description)
{
    public int Population { get; init; }
    public double BuildTime { get; init; } = 1;
    public IReadOnlyList<string> Trains { get; init; } = [];
    public double Food { get; init; }
    public double Attack { get; init; }
    public double Range { get; init; }
    public double Cooldown { get; init; }
}

public sealed class ModifierSet
{
    public double Duration { get; init; }
    public double BuildingHp { get; init; } = 1;
    public double BuildingReduction { get; init; }
    public double AgeCost { get; init; } = 1;
    public double FarmYield { get; init; } = 1;
    public bool Reveal { get; init; }
    public IReadOnlyDictionary<string, double> StartResources { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> Gather { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitCost { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitHp { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitDamage { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitArmor { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitSpeed { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitRange { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> UnitCooldown { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> TrainSpeed { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> BuildingCost { get; init; } = GameData.EmptyValues;
    public IReadOnlyDictionary<string, double> Heal { get; init; } = GameData.EmptyValues;
}

public sealed record CivilizationDefinition(
    string Key,
    string Name,
    string Seal,
    string Style,
    string Color,
    string Accent,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    ModifierSet Modifiers,
    string PowerName,
    string PowerDescription,
    ModifierSet PowerModifiers,
    string UniqueUnit);

public sealed record DifficultyDefinition(
    string Key,
    double AiRate,
    double WaveSeconds,
    int StartingSoldiers,
    double PassiveIncome,
    double ThinkSeconds,
    double CounterSkill,
    string Description);

public static class GameData
{
    private static readonly ReadOnlyDictionary<string, double> NoValues = new(new Dictionary<string, double>(StringComparer.Ordinal));
    public static IReadOnlyDictionary<string, double> EmptyValues => NoValues;

    public static IReadOnlyDictionary<string, UnitDefinition> Units { get; } = BuildUnits();
    public static IReadOnlyDictionary<string, BuildingDefinition> Buildings { get; } = BuildBuildings();
    public static IReadOnlyDictionary<string, CivilizationDefinition> Civilizations { get; } = BuildCivilizations();
    public static IReadOnlyDictionary<string, DifficultyDefinition> Difficulties { get; } = BuildDifficulties();

    public static IReadOnlyList<string> BuildOrder { get; } =
    [
        "house", "mill", "lumber", "farm", "barracks", "blacksmith", "range",
        "stable", "tower", "wall", "castle", "workshop", "wonder"
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> TrainingAt { get; } =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["town"] = ["villager"],
            ["barracks"] = ["swordsman", "spear"],
            ["range"] = ["archer", "crossbow"],
            ["stable"] = ["scout", "cavalry"],
            ["castle"] = [],
            ["workshop"] = ["ram", "catapult"]
        });

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildingPrerequisites { get; } =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["farm"] = ["mill"],
            ["range"] = ["barracks"],
            ["stable"] = ["barracks"],
            ["workshop"] = ["blacksmith"],
            ["castle"] = ["blacksmith"]
        });

    private static IReadOnlyDictionary<string, double> V(params (string Key, double Value)[] pairs) =>
        new ReadOnlyDictionary<string, double>(pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, UnitDefinition> BuildUnits()
    {
        var values = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal)
        {
            ["villager"] = new("villager", "村民", "民", 55, 3, 0, 62, 22, 1.3, V(("food", 50)), 15, 1, 1, "worker", "採集資源、興建與修理建築。"),
            ["scout"] = new("scout", "斥候騎兵", "斥", 100, 6, 1, 98, 24, 1.25, V(("food", 80)), 18, 1, 2, "cavalry", "高速偵察，視野廣闊。") { Sight = 300 },
            ["swordsman"] = new("swordsman", "民兵", "劍", 82, 8, 0, 59, 27, 1.3, V(("food", 60), ("gold", 20)), 16, 1, 1, "infantry", "黑暗時代即可訓練的基礎步兵。"),
            ["spear"] = new("spear", "長槍兵", "槍", 115, 10, 1, 58, 28, 1.3, V(("food", 60), ("wood", 20)), 16, 1, 2, "infantry", "廉價前排，強力克制騎兵。") { Bonus = V(("cavalry", 20)) },
            ["archer"] = new("archer", "弓箭手", "弓", 75, 11, 0, 61, 225, 1.35, V(("food", 40), ("wood", 45)), 18, 1, 2, "ranged", "遠程壓制長槍兵，畏懼騎兵。") { Bonus = V(("infantry", 5)) },
            ["cavalry"] = new("cavalry", "騎士", "騎", 195, 21, 3, 91, 30, 1.45, V(("food", 90), ("gold", 60)), 26, 2, 3, "cavalry", "城堡時代的重騎兵，擅長衝擊後排。") { Bonus = V(("ranged", 10), ("siege", 12)) },
            ["crossbow"] = new("crossbow", "弩兵", "弩", 85, 17, 1, 56, 220, 1.55, V(("food", 45), ("gold", 55)), 21, 1, 3, "ranged", "穿甲遠程，克制重裝軍隊。") { Bonus = V(("infantry", 10), ("cavalry", 8)) },
            ["ram"] = new("ram", "衝撞車", "車", 420, 35, 8, 35, 34, 2, V(("wood", 170), ("gold", 80)), 36, 3, 3, "siege", "耐射擊，專門摧毀建築。") { Bonus = V(("building", 70)) },
            ["catapult"] = new("catapult", "投石車", "砲", 160, 32, 1, 34, 270, 3, V(("wood", 120), ("gold", 90)), 34, 3, 3, "siege", "拋射巨石，打擊密集軍隊。") { Bonus = V(("ranged", 18), ("building", 18)), Splash = 58 },
            ["longbowman"] = Unique("longbowman", "長弓兵", "弓", 82, 17, 0, 55, 278, 1.5, V(("wood", 48), ("gold", 52)), 24, 1, "ranged", "射程冠絕戰場，但近身後十分脆弱。", "britons", V(("infantry", 8))),
            ["cataphract"] = Unique("cataphract", "拜占庭聖騎兵", "雙", 225, 23, 5, 82, 31, 1.45, V(("food", 92), ("gold", 72)), 29, 2, "cavalry", "披掛重甲的精騎，專門踐破步兵陣線。", "byzantines", V(("infantry", 18))) with { Splash = 18 },
            ["woadRaider"] = Unique("woadRaider", "菘藍突襲者", "藍", 145, 20, 2, 82, 28, 1.2, V(("food", 72), ("gold", 38)), 21, 1, "infantry", "速度驚人的突擊步兵，適合繞後襲擊。", "celts", V(("siege", 8))),
            ["chuKoNu"] = Unique("chuKoNu", "諸葛弩", "諸", 78, 9, 1, 55, 220, .72, V(("wood", 52), ("gold", 48)), 24, 1, "ranged", "以極高射速連續發射弩箭。", "chinese", V(("infantry", 5))) with { Splash = 14 },
            ["throwingAxeman"] = Unique("throwingAxeman", "擲斧兵", "斧", 122, 18, 3, 55, 142, 1.35, V(("food", 64), ("gold", 48)), 23, 1, "infantry", "以重斧進行短程投射的耐久步兵。", "franks", V(("infantry", 8))) with { IsRanged = true },
            ["huskarl"] = Unique("huskarl", "哥德衛隊", "盔", 160, 18, 6, 68, 28, 1.25, V(("food", 70), ("gold", 42)), 20, 1, "infantry", "抗箭甲冑與高速步伐令弓兵聞風喪膽。", "goths", V(("ranged", 18))),
            ["samurai"] = Unique("samurai", "日本武士", "武", 158, 21, 4, 59, 29, 1.1, V(("food", 72), ("gold", 48)), 22, 1, "infantry", "出手迅捷的精銳刀兵，善斬敵方菁英。", "japanese", V(("infantry", 7), ("cavalry", 5))),
            ["mangudai"] = Unique("mangudai", "蒙古突騎", "鷹", 135, 15, 2, 96, 228, 1.2, V(("food", 70), ("gold", 62)), 26, 2, "cavalry", "高速騎射手，能迅速摧毀攻城器。", "mongols", V(("siege", 18))) with { IsRanged = true },
            ["warElephant"] = Unique("warElephant", "戰象", "象", 430, 31, 5, 48, 36, 1.9, V(("food", 130), ("gold", 95)), 38, 3, "cavalry", "昂貴、緩慢而驚人的重型衝擊單位。", "persians", V(("building", 25), ("infantry", 10))) with { Splash = 34 },
            ["mameluke"] = Unique("mameluke", "馬穆魯克", "月", 170, 17, 3, 86, 118, 1.25, V(("food", 76), ("gold", 64)), 27, 2, "cavalry", "投擲彎刀的駱駝精騎，強力克制騎兵。", "saracens", V(("cavalry", 20))) with { IsRanged = true },
            ["teutonicKnight"] = Unique("teutonicKnight", "條頓武士", "十", 230, 27, 8, 40, 28, 1.55, V(("food", 82), ("gold", 58)), 29, 1, "infantry", "極慢但近戰攻防無雙的重甲武士。", "teutons", V(("building", 10))),
            ["janissary"] = Unique("janissary", "土耳其火槍兵", "銃", 92, 24, 1, 56, 235, 1.85, V(("food", 58), ("gold", 68)), 27, 1, "ranged", "射速偏慢，但單發火力與射程優秀。", "turks", V(("infantry", 6))),
            ["berserk"] = Unique("berserk", "狂戰士", "狂", 180, 21, 4, 62, 29, 1.2, V(("food", 76), ("gold", 44)), 24, 1, "infantry", "能緩慢恢復生命的北海精銳戰士。", "vikings", V(("infantry", 5))) with { Regeneration = .75 }
        };
        return new ReadOnlyDictionary<string, UnitDefinition>(values);
    }

    private static UnitDefinition Unique(string key, string name, string glyph, double hp, double damage, double armor, double speed, double range, double cool, IReadOnlyDictionary<string, double> cost, double time, int pop, string role, string desc, string civ, IReadOnlyDictionary<string, double> bonus) =>
        new(key, name, glyph, hp, damage, armor, speed, range, cool, cost, time, pop, 3, role, desc)
        {
            Bonus = bonus,
            UniqueCivilization = civ,
            TrainAt = "castle"
        };

    private static IReadOnlyDictionary<string, BuildingDefinition> BuildBuildings()
    {
        var values = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal)
        {
            ["town"] = new("town", "城鎮中心", "城", 2800, 82, V(), 1, "王國心臟；訓練村民，遭摧毀即告戰敗。") { Population = 15, Trains = ["villager"] },
            ["house"] = new("house", "房舍", "舍", 420, 36, V(("wood", 100)), 1, "提高 10 人口容量。") { Population = 10, BuildTime = 18 },
            ["mill"] = new("mill", "磨坊", "磨", 520, 39, V(("wood", 100)), 1, "解鎖農田，附近食物採集效率＋10%。") { BuildTime = 20 },
            ["lumber"] = new("lumber", "伐木場", "木", 500, 38, V(("wood", 100)), 1, "附近木材採集效率＋10%，也是晉升封建時代的前置。") { BuildTime = 20 },
            ["farm"] = new("farm", "農田", "田", 260, 42, V(("wood", 60)), 1, "需先完成磨坊；提供可持續食物。") { BuildTime = 12, Food = 450 },
            ["barracks"] = new("barracks", "軍營", "營", 780, 51, V(("wood", 145)), 1, "訓練步兵，並解鎖靶場與馬廄。") { BuildTime = 28, Trains = ["swordsman", "spear"] },
            ["blacksmith"] = new("blacksmith", "鐵匠鋪", "鐵", 650, 43, V(("wood", 150)), 2, "研究經濟與軍事科技，並解鎖城堡與攻城工坊。") { BuildTime = 27 },
            ["range"] = new("range", "靶場", "靶", 650, 49, V(("wood", 165)), 2, "需先完成軍營；訓練弓箭手與弩兵。") { BuildTime = 30, Trains = ["archer", "crossbow"] },
            ["stable"] = new("stable", "馬廄", "廄", 700, 52, V(("wood", 190)), 2, "需先完成軍營；訓練斥候騎兵與騎士。") { BuildTime = 32, Trains = ["scout", "cavalry"] },
            ["tower"] = new("tower", "箭塔", "塔", 720, 34, V(("wood", 80), ("stone", 150)), 2, "自動射擊鄰近敵軍；最多四座。") { BuildTime = 32, Attack = 12, Range = 245, Cooldown = 1.5 },
            ["wall"] = new("wall", "石牆", "牆", 540, 29, V(("stone", 35)), 2, "便宜的封建時代防禦工事。") { BuildTime = 10 },
            ["castle"] = new("castle", "城堡", "堡", 3200, 76, V(("stone", 500)), 3, "訓練文明獨特兵種，也是晉升帝王時代的前置。") { BuildTime = 65, Attack = 18, Range = 285, Cooldown = 1.35 },
            ["workshop"] = new("workshop", "攻城工坊", "坊", 760, 56, V(("wood", 220), ("gold", 80)), 3, "需先完成鐵匠鋪；製造攻城器械。") { BuildTime = 38, Trains = ["ram", "catapult"] },
            ["wonder"] = new("wonder", "世界奇觀", "觀", 1900, 74, V(("wood", 800), ("gold", 800), ("stone", 800)), 4, "完工後守住 180 秒即可獲勝。") { BuildTime = 80 }
        };
        return new ReadOnlyDictionary<string, BuildingDefinition>(values);
    }

    private static IReadOnlyDictionary<string, CivilizationDefinition> BuildCivilizations()
    {
        var values = new Dictionary<string, CivilizationDefinition>(StringComparer.Ordinal)
        {
            ["britons"] = Civ("britons", "不列顛人", "弓", "長弓列陣 · 牧野王國", "#4f89c6", "#e6c96e", ["遠程射程＋10%，食物採集＋8%", "長弓兵能在敵軍接近前傾瀉箭雨"], ["騎兵生命−10%"], new() { UnitRange = V(("ranged", 1.1)), Gather = V(("food", 1.08)), UnitHp = V(("cavalry", .9)) }, "長弓齊射", "12 秒內遠程射程＋18%、攻速＋22%。", new() { Duration = 12, UnitRange = V(("ranged", 1.18)), UnitCooldown = V(("ranged", .78)) }, "longbowman"),
            ["byzantines"] = Civ("byzantines", "拜占庭人", "雙", "雙頭鷹旗 · 千年城垣", "#8b70bd", "#e0b75c", ["建築生命＋15%，時代晉升成本−10%", "拜占庭聖騎兵擅長踐破步兵陣線"], ["步兵攻擊−8%"], new() { BuildingHp = 1.15, AgeCost = .9, UnitDamage = V(("infantry", .92)) }, "君士坦丁壁壘", "14 秒內建築減傷 30%，騎兵護甲＋2。", new() { Duration = 14, BuildingReduction = .3, UnitArmor = V(("cavalry", 2)) }, "cataphract"),
            ["celts"] = Civ("celts", "塞爾特人", "結", "高地戰吼 · 森林攻城", "#4f9b70", "#d7a64b", ["木材採集＋12%，步兵移速＋10%", "攻城器攻速＋12%，菘藍突襲者行動迅捷"], ["騎兵護甲−1"], new() { Gather = V(("wood", 1.12)), UnitSpeed = V(("infantry", 1.1)), UnitCooldown = V(("siege", .88)), UnitArmor = V(("cavalry", -1)) }, "高地戰吼", "12 秒內步兵移速＋25%、攻擊＋16%。", new() { Duration = 12, UnitSpeed = V(("infantry", 1.25)), UnitDamage = V(("infantry", 1.16)) }, "woadRaider"),
            ["chinese"] = Civ("chinese", "中國人", "龍", "工巧農政 · 諸葛連弩", "#c94f4f", "#e4c15e", ["村民成本−10%，農田存量＋12%", "諸葛弩以密集連射壓制步兵"], ["起始食物−18%、黃金−10%"], new() { UnitCost = V(("worker", .9)), FarmYield = 1.12, StartResources = V(("food", .82), ("gold", .9)) }, "萬弩連發", "10 秒內遠程攻速＋35%、攻擊＋10%。", new() { Duration = 10, UnitCooldown = V(("ranged", .65)), UnitDamage = V(("ranged", 1.1)) }, "chuKoNu"),
            ["franks"] = Civ("franks", "法蘭克人", "鳶", "鳶尾戰旗 · 重騎封臣", "#386faf", "#e5cf77", ["騎兵生命＋12%，城堡成本−25%", "擲斧兵可從步兵陣後投射重斧"], ["遠程單位射程−10%"], new() { UnitHp = V(("cavalry", 1.12)), BuildingCost = V(("castle", .75)), UnitRange = V(("ranged", .9)) }, "封建重騎衝鋒", "10 秒內騎兵移速＋20%、攻擊＋20%，並恢復 10% 生命。", new() { Duration = 10, UnitSpeed = V(("cavalry", 1.2)), UnitDamage = V(("cavalry", 1.2)), Heal = V(("cavalry", .1)) }, "throwingAxeman"),
            ["goths"] = Civ("goths", "哥德人", "鴉", "部族洪流 · 破弓近衛", "#6f765f", "#caa66b", ["步兵成本−18%、訓練速度＋15%", "哥德衛隊對遠程單位極具威脅"], ["建築生命−10%"], new() { UnitCost = V(("infantry", .82)), TrainSpeed = V(("infantry", 1.15)), BuildingHp = .9 }, "部族洪流", "12 秒內步兵移速＋20%、攻速＋22%。", new() { Duration = 12, UnitSpeed = V(("infantry", 1.2)), UnitCooldown = V(("infantry", .78)) }, "huskarl"),
            ["japanese"] = Civ("japanese", "日本人", "日", "武家刀陣 · 精耕漁獵", "#d2635c", "#efcf83", ["步兵攻速＋14%，食物採集＋8%", "日本武士善於迅速斬破重裝步兵"], ["騎兵生命−10%"], new() { UnitCooldown = V(("infantry", .86)), Gather = V(("food", 1.08)), UnitHp = V(("cavalry", .9)) }, "武士決意", "10 秒內步兵攻擊＋20%、護甲＋2。", new() { Duration = 10, UnitDamage = V(("infantry", 1.2)), UnitArmor = V(("infantry", 2)) }, "samurai"),
            ["mongols"] = Civ("mongols", "蒙古人", "狼", "蒼狼騎射 · 草原奔襲", "#6aa7ba", "#d5a34d", ["食物採集＋10%，騎兵攻速＋12%", "蒙古突騎機動迅捷並克制攻城器"], ["建築生命−10%"], new() { Gather = V(("food", 1.1)), UnitCooldown = V(("cavalry", .88)), BuildingHp = .9 }, "草原風暴", "10 秒內騎兵移速＋22%、攻速＋25%。", new() { Duration = 10, UnitSpeed = V(("cavalry", 1.22)), UnitCooldown = V(("cavalry", .75)) }, "mangudai"),
            ["persians"] = Civ("persians", "波斯人", "象", "萬王之國 · 象軍震地", "#b85656", "#e7bd66", ["起始食物與木材＋8%，騎兵生命＋8%", "戰象能摧毀密集軍隊與建築"], ["農田存量−10%"], new() { StartResources = V(("food", 1.08), ("wood", 1.08)), UnitHp = V(("cavalry", 1.08)), FarmYield = .9 }, "萬王戰象", "12 秒內騎兵攻擊＋20%，並恢復 18% 生命。", new() { Duration = 12, UnitDamage = V(("cavalry", 1.2)), Heal = V(("cavalry", .18)) }, "warElephant"),
            ["saracens"] = Civ("saracens", "薩拉森人", "月", "新月商旅 · 馬穆魯克", "#c38d48", "#4fa9a1", ["黃金採集＋12%，騎兵攻擊＋8%", "馬穆魯克能以飛刃獵殺重騎兵"], ["農田存量−10%"], new() { Gather = V(("gold", 1.12)), UnitDamage = V(("cavalry", 1.08)), FarmYield = .9 }, "新月獵騎", "10 秒內騎兵射程＋18%、攻速＋20%。", new() { Duration = 10, UnitRange = V(("cavalry", 1.18)), UnitCooldown = V(("cavalry", .8)) }, "mameluke"),
            ["teutons"] = Civ("teutons", "條頓人", "十", "黑十字軍 · 鐵壁堡壘", "#8b8e98", "#d7bd74", ["步兵護甲＋1，建築生命＋12%", "條頓武士近戰攻防無雙"], ["騎兵移速−10%"], new() { UnitArmor = V(("infantry", 1)), BuildingHp = 1.12, UnitSpeed = V(("cavalry", .9)) }, "條頓鐵壁", "14 秒內步兵護甲＋3，建築減傷 20%。", new() { Duration = 14, UnitArmor = V(("infantry", 3)), BuildingReduction = .2 }, "teutonicKnight"),
            ["turks"] = Civ("turks", "土耳其人", "星", "火藥禁軍 · 黃金帝國", "#3f9b82", "#ddaa54", ["黃金採集＋15%，遠程攻擊＋8%", "土耳其火槍兵單發威力極高"], ["步兵生命−10%"], new() { Gather = V(("gold", 1.15)), UnitDamage = V(("ranged", 1.08)), UnitHp = V(("infantry", .9)) }, "蘇丹火網", "10 秒內遠程攻擊＋22%、射程＋12%。", new() { Duration = 10, UnitDamage = V(("ranged", 1.22)), UnitRange = V(("ranged", 1.12)) }, "janissary"),
            ["vikings"] = Civ("vikings", "維京人", "艦", "北海長船 · 狂戰斧陣", "#6b82a7", "#d49255", ["步兵生命＋12%，木材採集＋8%", "狂戰士耐久且能撕裂前線"], ["騎兵成本＋10%"], new() { UnitHp = V(("infantry", 1.12)), Gather = V(("wood", 1.08)), UnitCost = V(("cavalry", 1.1)) }, "奧丁狂怒", "12 秒內步兵攻速＋22%，並恢復 16% 生命。", new() { Duration = 12, UnitCooldown = V(("infantry", .78)), Heal = V(("infantry", .16)) }, "berserk")
        };
        return new ReadOnlyDictionary<string, CivilizationDefinition>(values);
    }

    private static CivilizationDefinition Civ(string key, string name, string seal, string style, string color, string accent, IReadOnlyList<string> pros, IReadOnlyList<string> cons, ModifierSet mods, string power, string powerDescription, ModifierSet powerMods, string unique) =>
        new(key, name, seal, style, color, accent, pros, cons, mods, power, powerDescription, powerMods, unique);

    private static IReadOnlyDictionary<string, DifficultyDefinition> BuildDifficulties()
    {
        var values = new Dictionary<string, DifficultyDefinition>(StringComparer.Ordinal)
        {
            ["休閒"] = new("休閒", .72, 120, 1, .12, 2.3, .25, "較慢決策、較少援軍，適合熟悉建造與剋制。"),
            ["征戰"] = new("征戰", 1, 84, 3, .22, 1.6, .48, "均衡的決策速度與攻勢，適合熟悉即時戰略的玩家。"),
            ["霸主"] = new("霸主", 1.23, 64, 4, .32, 1.15, .72, "更快擴張，會積極生產剋制兵種並夾擊弱點。"),
            ["天命"] = new("天命", 1.48, 49, 5, .44, .78, .9, "高速經濟、精準反制與連續攻勢，只適合帝國老將。")
        };
        return new ReadOnlyDictionary<string, DifficultyDefinition>(values);
    }
}

public static class GameRules
{
    public static double Modifier(IReadOnlyDictionary<string, double> values, UnitDefinition unit, double fallback = 1) =>
        values.TryGetValue(unit.Key, out var direct) ? direct :
        values.TryGetValue(unit.Role, out var role) ? role :
        unit.IsRanged && values.TryGetValue("ranged", out var ranged) ? ranged :
        values.TryGetValue("all", out var all) ? all : fallback;

    public static double Modifier(IReadOnlyDictionary<string, double> values, string key, double fallback = 1) =>
        values.TryGetValue(key, out var direct) ? direct : values.TryGetValue("all", out var all) ? all : fallback;

    public static ResourceBag Cost(IReadOnlyDictionary<string, double> source, double multiplier = 1) => new()
    {
        Food = Math.Ceiling(source.GetValueOrDefault("food") * multiplier),
        Wood = Math.Ceiling(source.GetValueOrDefault("wood") * multiplier),
        Gold = Math.Ceiling(source.GetValueOrDefault("gold") * multiplier),
        Stone = Math.Ceiling(source.GetValueOrDefault("stone") * multiplier)
    };
}
