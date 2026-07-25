using Empire.Core;
using System.Diagnostics;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"驗證失敗：{message}");
    }
}

Assert(GameData.Civilizations.Count == 13, "應有 13 個文明");
Assert(GameData.Units.Count == 22, "應有 22 種單位");
Assert(GameData.Buildings.Count == 14, "應有 14 種建築");
Assert(GameData.Difficulties.Count == 4, "應有 4 種 AI 難度");
Assert(GameData.Civilizations.Values.All(civ => GameData.Units.ContainsKey(civ.UniqueUnit)), "每個文明都應有獨特兵種");

foreach (var playerCount in Enumerable.Range(2, 3))
{
    var generated = GameEngine.CreateNew(new NewGameOptions
    {
        Civilization = "britons",
        Difficulty = "休閒",
        PlayerCount = playerCount,
        Seed = 1000 + playerCount,
        Tutorial = true
    });
    Assert(generated.State.Players.Count == playerCount, $"{playerCount} 人戰局玩家數");
    Assert(generated.State.Spawn.Count == playerCount, $"{playerCount} 人戰局出生點");
    Assert(generated.State.Players.Select(player => player.Civ).Distinct(StringComparer.Ordinal).Count() == playerCount, "文明不得重複");
    Assert(generated.State.Fog.Count == GameConstants.MapWidth * GameConstants.MapHeight, "迷霧尺寸");
}

var engine = GameEngine.CreateNew(new NewGameOptions
{
    Civilization = "chinese",
    Difficulty = "休閒",
    PlayerCount = 2,
    Seed = 731942,
    Tutorial = true
});
var human = engine.Player(0);
var worker = engine.State.Entities.First(entity => entity.Faction == 0 && entity.Type == "villager");
var foodBefore = human.Resources.Food;
var testFood = engine.CreateResource("food", worker.X + worker.Radius + 14, worker.Y, 100, 10);
Assert(engine.SetGather(worker.Id, testFood.Id), "村民應能採集");
for (var index = 0; index < 240; index++) engine.Step();
Assert(human.Resources.Food > foodBefore, "採集應增加資源");
Assert(engine.State.Stats.Gathered > 0, "採集統計應增加");

EntityState? house = null;
for (var y = 1050d; y < 1850 && house is null; y += 55)
{
    for (var x = 180d; x < 1050 && house is null; x += 55)
    {
        if (engine.CanBuild(0, "house", x, y))
        {
            house = engine.StartBuilding("house", 0, x, y, [worker.Id], free: true);
        }
    }
}
Assert(house is not null, "應找到房舍工地");
for (var index = 0; index < 1800 && house!.Construction < 1; index++) engine.Step();
Assert(house!.Construction >= 1, "村民應完成房舍");
Assert(human.PopCap >= 25, "房舍應提高人口上限");

var town = engine.State.Entities.First(entity => entity.Faction == 0 && entity.Type == "town");
var villagersBefore = engine.State.Entities.Count(entity => !entity.Dead && entity.Faction == 0 && entity.Type == "villager");
Assert(engine.QueueUnit(town.Id, "villager", free: true), "城鎮中心應能訓練村民");
for (var index = 0; index < 520; index++) engine.Step();
Assert(engine.State.Entities.Count(entity => !entity.Dead && entity.Faction == 0 && entity.Type == "villager") > villagersBefore, "訓練應產生村民");

var attacker = engine.CreateUnit("swordsman", 0, 1300, 1150);
var defender = engine.CreateUnit("swordsman", 1, 1320, 1150);
Assert(engine.SetAttack(attacker.Id, defender.Id), "應能下達攻擊命令");
for (var index = 0; index < 800 && !defender.Dead; index++) engine.Step();
Assert(defender.Dead || defender.Hp < defender.MaxHp, "戰鬥應造成傷害");

human.Resources.Food = Math.Max(human.Resources.Food, 1200);
engine.CreateBuilding("mill", 0, 760, 1420);
engine.CreateBuilding("lumber", 0, 890, 1420);
Assert(engine.BeginAgeUp(0), "完成前置後應能晉升時代");
for (var index = 0; index < 1250; index++) engine.Step();
Assert(human.Age >= 2, "應完成封建時代晉升");
Assert(engine.UsePower(0), "封建時代應能發動文明軍令");

engine.State.Camera.X = 777;
engine.State.Camera.Y = 1337;
engine.State.Camera.Zoom = 1.27;
var saveService = new GameSaveService();
var json = saveService.Serialize(engine, indented: true);
var restored = saveService.Deserialize(json);
Assert(restored.State.PlayerCount == 2, "JSON 往返應保留玩家數");
Assert(restored.Player(0).Civ == "chinese", "JSON 往返應保留文明");
Assert(restored.State.Entities.Count == engine.State.Entities.Count(entity => !entity.Dead), "JSON 往返應保留存活實體");
Assert(restored.NextId >= engine.NextId, "JSON 往返應保留穩定 ID");
Assert(restored.State.Fog.Count == GameConstants.MapWidth * GameConstants.MapHeight, "JSON 往返應保留迷霧");
Assert(restored.State.Camera.X == 777 && restored.State.Camera.Y == 1337 && Math.Abs(restored.State.Camera.Zoom - 1.27) < 1e-9, "載入應保留玩家鏡頭");

var endedEnvelope = JsonSerializer.Deserialize<GameSaveEnvelope>(json, GameSaveService.JsonOptions)!;
endedEnvelope.Game.Ended = true;
endedEnvelope.Game.Paused = true;
endedEnvelope.Game.WinnerFaction = 0;
endedEnvelope.Game.VictoryWay = "征服";
var endedRestore = saveService.Deserialize(JsonSerializer.Serialize(endedEnvelope, GameSaveService.JsonOptions));
Assert(endedRestore.State.Ended && endedRestore.State.Paused && endedRestore.State.WinnerFaction == 0 && endedRestore.State.VictoryWay == "征服", "結束戰局的存檔應忠實保留勝負狀態");

endedEnvelope.Game.WinnerFaction = null;
endedEnvelope.Game.VictoryWay = null;
var legacyEndedRestore = saveService.Deserialize(JsonSerializer.Serialize(endedEnvelope, GameSaveService.JsonOptions));
Assert(!legacyEndedRestore.State.Ended, "缺少勝負資料的舊版瀏覽器結束存檔應依歷史行為恢復為可續戰狀態");

foreach (var malformed in new[]
         {
             "{\"v\":4,\"game\":null}",
             "{\"v\":4,\"game\":{\"players\":null}}",
             "{\"v\":4,\"game\":{\"players\":[null]}}"
         })
{
    var rejected = false;
    try
    {
        _ = saveService.Deserialize(malformed);
    }
    catch (Exception exception) when (exception is InvalidDataException or JsonException)
    {
        rejected = true;
    }
    Assert(rejected, "損毀 JSON 應安全拒絕而非造成未處理例外");
}

var exportPath = Path.Combine(Path.GetTempPath(), $"帝國餘燼-smoke-{Guid.NewGuid():N}.json");
try
{
    saveService.Export(engine, exportPath);
    var imported = saveService.Import(exportPath);
    Assert(imported.State.Tick == engine.State.Tick, "匯出匯入應保留模擬刻數");
    Assert(imported.State.Sites.Count == 3, "匯出匯入應保留王旗");
}
finally
{
    if (File.Exists(exportPath)) File.Delete(exportPath);
}

var autosavePath = Path.Combine(Path.GetTempPath(), $"帝國餘燼-autosave-{Guid.NewGuid():N}.json");
try
{
    new GameSaveService(autosavePath).SaveAutosave(engine);
    var nextSession = new GameSaveService(autosavePath);
    Assert(nextSession.HasAutosave, "新的遊戲工作階段應發現自動存檔");
    var continued = nextSession.LoadAutosave();
    Assert(continued.State.Tick == engine.State.Tick, "跨工作階段續戰應保留模擬刻數");
    Assert(continued.Player(0).Civ == engine.Player(0).Civ, "跨工作階段續戰應保留文明");
}
finally
{
    if (File.Exists(autosavePath)) File.Delete(autosavePath);
}

var guidedTutorial = GameEngine.CreateNew(new NewGameOptions
{
    Civilization = "britons",
    Difficulty = "休閒",
    PlayerCount = 2,
    Seed = 44021,
    Tutorial = true
});
for (var step = 0; step < 11; step++) Assert(guidedTutorial.SkipTutorialStep(), "教學應能前進到王旗課程");
var guidedSite = guidedTutorial.State.Sites.First(site => site.Owner != 0);
Assert(guidedTutorial.State.Tutorial.Step == 11 && guidedTutorial.State.Camera.X == guidedSite.X && guidedTutorial.State.Camera.Y == guidedSite.Y, "王旗課程應把鏡頭帶到目標");
guidedTutorial.Step();
var guidedFogIndex = (int)(guidedSite.Y / GameConstants.TileSize) * GameConstants.MapWidth + (int)(guidedSite.X / GameConstants.TileSize);
Assert(guidedTutorial.State.Fog[guidedFogIndex] == 2, "王旗課程應在迷霧中標示目標區域");

static GameEngine CreateLongRunGame()
{
    var game = GameEngine.CreateNew(new NewGameOptions
    {
        Civilization = "mongols",
        Difficulty = "霸主",
        PlayerCount = 4,
        Seed = 0x35A71C2,
        Tutorial = true
    });
    for (var faction = 0; faction < 4; faction++)
    {
        var angle = faction * Math.PI / 2;
        for (var index = 0; index < 10; index++)
        {
            var radius = 250 + index * 9;
            var unit = game.CreateUnit(index % 3 == 0 ? "archer" : index % 3 == 1 ? "spear" : "swordsman", faction,
                GameConstants.WorldWidth / 2d + Math.Cos(angle) * radius + index * 3,
                GameConstants.WorldHeight / 2d + Math.Sin(angle) * radius - index * 2);
            game.SetMove(unit.Id, GameConstants.WorldWidth / 2d, GameConstants.WorldHeight / 2d, attackMove: true);
        }
    }
    return game;
}

static void AssertFiniteAndConsistent(GameEngine game)
{
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    var ids = new HashSet<int>();
    foreach (var entity in game.State.Entities)
    {
        Assert(ids.Add(entity.Id), "長時間模擬實體 ID 不得重複");
        Assert(Finite(entity.X) && Finite(entity.Y) && Finite(entity.Hp) && Finite(entity.MaxHp), "長時間模擬實體數值必須有限");
        Assert(entity.X >= 0 && entity.X <= GameConstants.WorldWidth && entity.Y >= 0 && entity.Y <= GameConstants.WorldHeight, "實體不得離開世界邊界");
        foreach (var point in entity.Path) Assert(Finite(point.X) && Finite(point.Y), "路徑點必須有限");
        foreach (var item in entity.Queue) Assert(Finite(item.Remaining) && Finite(item.Total), "生產佇列必須有限");
    }
    foreach (var node in game.State.Nodes)
    {
        Assert(ids.Add(node.Id), "資源 ID 不得與實體重複");
        Assert(Finite(node.X) && Finite(node.Y) && Finite(node.Amount), "資源數值必須有限");
    }
    foreach (var site in game.State.Sites)
    {
        Assert(ids.Add(site.Id), "王旗 ID 不得與其他物件重複");
        Assert(Finite(site.Progress), "王旗進度必須有限");
    }
    foreach (var player in game.State.Players)
    {
        foreach (var key in GameConstants.ResourceKeys)
        {
            Assert(Finite(player.Resources[key]) && player.Resources[key] >= 0, "玩家資源必須有限且非負");
        }
    }
    Assert(game.NextId > ids.DefaultIfEmpty().Max(), "下一個穩定 ID 必須大於現存 ID");
    Assert(game.State.Fog.Count == GameConstants.MapWidth * GameConstants.MapHeight, "長時間模擬迷霧尺寸");
}

static ulong DeterministicSignature(GameEngine game)
{
    static ulong Mix(ulong hash, long value) => unchecked((hash ^ (ulong)value) * 1099511628211UL);
    static ulong Text(ulong hash, string value)
    {
        foreach (var character in value) hash = Mix(hash, character);
        return hash;
    }

    var hash = Mix(1469598103934665603UL, game.State.Tick);
    hash = Mix(hash, BitConverter.DoubleToInt64Bits(game.State.Time));
    foreach (var player in game.State.Players)
    {
        hash = Text(hash, player.Civ);
        hash = Mix(hash, player.Age);
        foreach (var key in GameConstants.ResourceKeys) hash = Mix(hash, BitConverter.DoubleToInt64Bits(player.Resources[key]));
    }
    foreach (var entity in game.State.Entities)
    {
        hash = Mix(hash, entity.Id);
        hash = Text(hash, entity.Type);
        hash = Mix(hash, BitConverter.DoubleToInt64Bits(entity.X));
        hash = Mix(hash, BitConverter.DoubleToInt64Bits(entity.Y));
        hash = Mix(hash, BitConverter.DoubleToInt64Bits(entity.Hp));
        hash = Text(hash, entity.Order.Type);
    }
    foreach (var node in game.State.Nodes)
    {
        hash = Mix(hash, node.Id);
        hash = Mix(hash, BitConverter.DoubleToInt64Bits(node.Amount));
    }
    return hash;
}

var longRunA = CreateLongRunGame();
var longRunB = CreateLongRunGame();
var benchmark = Stopwatch.StartNew();
const int longRunTicks = 3_600;
for (var index = 0; index < longRunTicks; index++)
{
    longRunA.Step();
    longRunB.Step();
}
benchmark.Stop();
AssertFiniteAndConsistent(longRunA);
AssertFiniteAndConsistent(longRunB);
Assert(DeterministicSignature(longRunA) == DeterministicSignature(longRunB), "相同種子與指令必須得到相同長時間模擬結果");

Console.WriteLine("帝國核心冒煙測試：PASS");
Console.WriteLine($"文明 {GameData.Civilizations.Count}｜兵種 {GameData.Units.Count}｜建築 {GameData.Buildings.Count}｜固定更新 {GameConstants.FixedStep:0.0000} 秒");
Console.WriteLine($"四方長時間模擬：{longRunTicks * 2:N0} 次邏輯更新／{benchmark.Elapsed.TotalMilliseconds:N1} 毫秒");
