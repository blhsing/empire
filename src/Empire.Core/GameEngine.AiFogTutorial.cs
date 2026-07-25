namespace Empire.Core;

public sealed partial class GameEngine
{
    private void UpdateAi(double delta)
    {
        var difficulty = GameData.Difficulties[State.Difficulty];
        var tutorialLocked = State.Tutorial.Active && !State.Tutorial.Completed && State.Tutorial.Step < 11;
        for (var faction = 1; faction < State.PlayerCount; faction++)
        {
            var player = Player(faction);
            var ai = faction < State.Ais.Count ? State.Ais[faction] : null;
            if (player.Eliminated || ai is null)
            {
                continue;
            }

            foreach (var resource in GameConstants.ResourceKeys)
            {
                player.Resources[resource] += difficulty.PassiveIncome * difficulty.AiRate * delta;
            }

            ai.Think -= delta;
            ai.Wave -= delta;
            ai.Build -= delta;
            ai.Train -= delta;
            if (ai.Think > 0)
            {
                continue;
            }

            ai.Think = difficulty.ThinkSeconds * (.9 + _random.NextDouble() * .22);
            for (var entityIndex = 0; entityIndex < State.Entities.Count; entityIndex++)
            {
                var worker = State.Entities[entityIndex];
                if (worker.Dead || worker.Faction != faction || worker.Type != "villager" || worker.Order.Type != "idle")
                {
                    continue;
                }
                var preferred = AiResourcePreferences[worker.Id % AiResourcePreferences.Length];
                var node = NearestResource(worker, preferred) ?? NearestResource(worker);
                if (node is not null)
                {
                    SetGather(worker.Id, node.Id);
                }
            }

            TryAiAge(player, 2, 95 / difficulty.AiRate);
            TryAiAge(player, 3, 275 / difficulty.AiRate);
            TryAiAge(player, 4, 610 / difficulty.AiRate);

            if (ai.Build <= 0)
            {
                ai.Build = 13 / difficulty.AiRate;
                TryAiBuild(faction);
            }

            if (ai.Train <= 0)
            {
                ai.Train = 3.1 / difficulty.AiRate;
                TryAiTrain(faction, difficulty.CounterSkill);
            }

            if (!tutorialLocked && ai.Wave <= 0)
            {
                ai.Wave = difficulty.WaveSeconds * (.86 + _random.NextDouble() * .28);
                LaunchAiWave(faction);
                if (player.Age >= 2 && player.PowerReady <= State.Time && _random.NextDouble() < .72)
                {
                    UsePower(faction);
                }
            }
        }
    }

    private void TryAiAge(PlayerState player, int toAge, double earliest)
    {
        if (player.Age != toAge - 1 || player.AgeUp is not null || State.Time < earliest || !MeetsAgeRequirement(player))
        {
            return;
        }

        var cost = AgeCost(player);
        if (!Spend(player, cost))
        {
            return;
        }

        var total = player.Age switch { 1 => 40, 2 => 55, 3 => 70, _ => 0 } * .82;
        player.AgeUp = new AgeUpState { To = toAge, Remaining = total, Total = total };
    }

    private void TryAiBuild(int faction)
    {
        var player = Player(faction);
        int Count(string type) => State.Entities.Count(entity => !entity.Dead && entity.Faction == faction && entity.Kind == "building" && entity.Type == type);
        string? type = null;
        if (player.PopCap - player.Pop < 8) type = "house";
        else if (Count("mill") == 0) type = "mill";
        else if (Count("lumber") == 0) type = "lumber";
        else if (Count("barracks") == 0) type = "barracks";
        else if (player.Age >= 2 && Count("blacksmith") == 0) type = "blacksmith";
        else if (player.Age >= 2 && Count("range") == 0) type = "range";
        else if (player.Age >= 2 && Count("stable") == 0) type = "stable";
        else if (player.Age >= 3 && Count("castle") == 0) type = "castle";
        else if (player.Age >= 3 && Count("workshop") == 0) type = "workshop";
        else if (player.Age >= 2 && Count("tower") < 2) type = "tower";
        else if (Count("farm") < 5) type = "farm";
        if (type is null)
        {
            return;
        }

        var spawn = State.Spawn[faction];
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var radius = 135 + _random.NextDouble() * 250;
            var x = Math.Clamp(spawn.X + Math.Cos(angle) * radius, 80, GameConstants.WorldWidth - 80);
            var y = Math.Clamp(spawn.Y + Math.Sin(angle) * radius, 80, GameConstants.WorldHeight - 80);
            if (!CanBuild(faction, type, x, y))
            {
                continue;
            }

            var builder = State.Entities
                .Where(entity => !entity.Dead && entity.Faction == faction && entity.Type == "villager")
                .MinBy(entity => Distance(entity.X, entity.Y, x, y));
            if (builder is null)
            {
                return;
            }
            if (StartBuilding(type, faction, x, y, [builder.Id]) is not null)
            {
                return;
            }
        }
    }

    private void TryAiTrain(int faction, double counterSkill)
    {
        var player = Player(faction);
        for (var entityIndex = 0; entityIndex < State.Entities.Count; entityIndex++)
        {
            var building = State.Entities[entityIndex];
            if (building.Dead || building.Faction != faction || building.Kind != "building" || building.Construction < 1 || building.Queue.Count >= 2)
            {
                continue;
            }
            _aiTrainOptions.Clear();
            if (GameData.TrainingAt.TryGetValue(building.Type, out var trainedUnits))
            {
                for (var optionIndex = 0; optionIndex < trainedUnits.Count; optionIndex++)
                {
                    var type = trainedUnits[optionIndex];
                    if (GameData.Units[type].Age <= player.Age)
                    {
                        _aiTrainOptions.Add(type);
                    }
                }
            }
            if (building.Type == "castle" && player.Age >= 3)
            {
                _aiTrainOptions.Add(GameData.Civilizations[player.Civ].UniqueUnit);
            }
            if (_aiTrainOptions.Count == 0)
            {
                continue;
            }

            var choice = ChooseCounterUnit(faction, _aiTrainOptions, counterSkill);
            if (choice is not null)
            {
                QueueUnit(building.Id, choice);
            }
        }
    }

    private string? ChooseCounterUnit(int faction, IReadOnlyList<string> options, double skill)
    {
        if (options.Count == 0)
        {
            return null;
        }
        if (_random.NextDouble() > skill)
        {
            return options[_random.Next(options.Count)];
        }

        var infantry = 0;
        var ranged = 0;
        var cavalry = 0;
        for (var entityIndex = 0; entityIndex < State.Entities.Count; entityIndex++)
        {
            var hostile = State.Entities[entityIndex];
            if (hostile.Dead || hostile.Faction == faction || hostile.Kind != "unit")
            {
                continue;
            }
            switch (GameData.Units[hostile.Type].Role)
            {
                case "infantry": infantry++; break;
                case "ranged": ranged++; break;
                case "cavalry": cavalry++; break;
            }
        }

        var desired = cavalry >= Math.Max(ranged, infantry)
            ? "spear"
            : ranged >= infantry ? "cavalry" : "archer";
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index] == desired)
            {
                return options[index];
            }
        }
        return options[_random.Next(options.Count)];
    }

    private void LaunchAiWave(int faction)
    {
        var army = State.Entities
            .Where(entity => !entity.Dead && entity.Faction == faction && entity.Kind == "unit" && GameData.Units[entity.Type].Role != "worker")
            .ToList();
        if (army.Count < 2)
        {
            return;
        }

        var centerX = army.Average(unit => unit.X);
        var centerY = army.Average(unit => unit.Y);
        var target = State.Entities
            .Where(entity => !entity.Dead && entity.Faction != faction && entity.Type == "town")
            .MinBy(entity => Distance(centerX, centerY, entity.X, entity.Y));
        if (target is null)
        {
            return;
        }

        var columns = (int)Math.Ceiling(Math.Sqrt(army.Count));
        for (var index = 0; index < army.Count; index++)
        {
            var offsetX = (index % columns - (columns - 1) / 2d) * 28;
            var offsetY = (index / columns - (Math.Ceiling(army.Count / (double)columns) - 1) / 2d) * 28;
            SetMove(army[index].Id, target.X + offsetX, target.Y + offsetY, attackMove: true);
        }
    }

    private void UpdateFog(bool force = false)
    {
        State.FogTimer -= force ? 99 : GameConstants.FixedStep;
        if (State.FogTimer > 0)
        {
            return;
        }
        State.FogTimer = .32;
        for (var index = 0; index < State.Fog.Count; index++)
        {
            if (State.Fog[index] == 2)
            {
                State.Fog[index] = 1;
            }
        }
        if (State.RevealUntil > State.Time)
        {
            for (var index = 0; index < State.Fog.Count; index++)
            {
                State.Fog[index] = 2;
            }
            return;
        }

        for (var entityIndex = 0; entityIndex < State.Entities.Count; entityIndex++)
        {
            var entity = State.Entities[entityIndex];
            if (entity.Dead || entity.Faction != 0)
            {
                continue;
            }
            var sight = entity.Kind == "building" ? 190 : GameData.Units[entity.Type].Sight;
            var centerX = (int)(entity.X / GameConstants.TileSize);
            var centerY = (int)(entity.Y / GameConstants.TileSize);
            var radius = (int)Math.Ceiling(sight / GameConstants.TileSize);
            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x >= 0 && y >= 0 && x < GameConstants.MapWidth && y < GameConstants.MapHeight &&
                        (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY) <= radius * radius)
                    {
                        State.Fog[y * GameConstants.MapWidth + x] = 2;
                    }
                }
            }
        }

        // The capture lesson must never strand a new player behind unexplored
        // fog. Keep a compact area around its target visible until the lesson
        // advances, without revealing the rest of the map.
        if (State.Tutorial is { Active: true, Completed: false, Step: 11 })
        {
            var targetSite = State.Sites.FirstOrDefault(site => site.Owner != 0);
            if (targetSite is not null)
            {
                var centerX = (int)(targetSite.X / GameConstants.TileSize);
                var centerY = (int)(targetSite.Y / GameConstants.TileSize);
                const int radius = 4;
                for (var y = centerY - radius; y <= centerY + radius; y++)
                {
                    for (var x = centerX - radius; x <= centerX + radius; x++)
                    {
                        if (x >= 0 && y >= 0 && x < GameConstants.MapWidth && y < GameConstants.MapHeight &&
                            (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY) <= radius * radius)
                        {
                            State.Fog[y * GameConstants.MapWidth + x] = 2;
                        }
                    }
                }
            }
        }
    }

    public void TutorialEvent(string key, bool value = true)
    {
        if (State.Tutorial.Active && !State.Tutorial.Completed)
        {
            State.Tutorial.Flags[key] = value;
        }
    }

    public bool SkipTutorialStep()
    {
        if (!State.Tutorial.Active || State.Tutorial.Completed || State.Tutorial.Step >= TutorialCatalog.Steps.Count - 1)
        {
            return false;
        }
        State.Tutorial.Step++;
        EnterTutorialStep(State.Tutorial.Step);
        return true;
    }

    public void CompleteTutorial(bool markComplete = true)
    {
        State.Tutorial.Active = false;
        State.Tutorial.Completed = markComplete;
    }

    private void UpdateTutorial(double delta)
    {
        var tutorial = State.Tutorial;
        if (!tutorial.Active || tutorial.Completed)
        {
            return;
        }
        tutorial.CheckIn -= delta;
        if (tutorial.CheckIn > 0)
        {
            return;
        }
        tutorial.CheckIn = .25;
        var step = tutorial.Step;
        if (step < TutorialCatalog.Steps.Count - 1 && TutorialCatalog.IsComplete(this, step))
        {
            tutorial.Step++;
            EnterTutorialStep(tutorial.Step);
        }
    }

    private void EnterTutorialStep(int step)
    {
        var tutorial = State.Tutorial;
        if (tutorial.Granted.Contains(step))
        {
            return;
        }
        tutorial.Granted.Add(step);
        foreach (var grant in TutorialCatalog.Steps[step].Grant)
        {
            Player(0).Resources[grant.Key] = Math.Max(Player(0).Resources[grant.Key], grant.Value);
        }
        if (step == 3)
        {
            tutorial.GatherStart = State.Stats.Gathered;
        }
        if (step == 10)
        {
            Player(0).PowerReady = 0;
        }
        if (step == 11)
        {
            var targetSite = State.Sites.FirstOrDefault(site => site.Owner != 0);
            if (targetSite is not null)
            {
                State.Camera.X = targetSite.X;
                State.Camera.Y = targetSite.Y;
                State.FogTimer = 0;
                tutorial.Flags["siteGuided"] = true;
            }
        }
    }
}

public sealed record TutorialStepDefinition(string Title, string Body, string Hint, IReadOnlyDictionary<string, double> Grant);

public static class TutorialCatalog
{
    private static readonly string[] SettlementBuildings = ["mill", "house", "farm"];

    public static IReadOnlyList<TutorialStepDefinition> Steps { get; } =
    [
        Step("環顧俯視戰場", "按住滑鼠右鍵拖曳即可平移 2D 地圖；WASD、畫面邊緣與小地圖也能移動視角，滾輪則縮放。滑鼠中鍵完全不使用。", "目標：平移或縮放一次戰場"),
        Step("選取你的人民", "左鍵點選村民；拖曳左鍵可框選，Shift 可追加，連按兩下會選取畫面內同兵種。", "目標：選取至少一名村民"),
        Step("快速右鍵下令", "在空地快速按一下右鍵可移動；對資源、敵軍或工地會自動採集、攻擊或施工。", "目標：下達一次移動命令"),
        Step("維持帝國經濟", "把村民分派至食物、木材、黃金與石材。", "目標：累積採集至少 12 單位資源", ("food", 380), ("wood", 360), ("gold", 220), ("stone", 180)),
        Step("安家、磨坊與屯田", "先興建磨坊，便能在其周圍開闢農田；房舍提高人口上限。", "目標：完成磨坊、房舍與農田", ("wood", 520)),
        Step("人口、生產與集合點", "選取城鎮中心訓練村民，再設定集合點。", "目標：擁有至少 6 名村民，並設定集合點", ("food", 260)),
        Step("保存與可攜存檔", "遊戲每 30 秒自動保存；也能匯出 JSON 並在另一部電腦匯入。", "目標：儲存並匯出一次存檔"),
        Step("晉升時代與鐵匠鋪", "完成時代前置建築，並在鐵匠鋪研究科技。", "目標：到達封建時代並研究一項科技", ("food", 820), ("gold", 430), ("wood", 620)),
        Step("組建剋制軍隊", "長槍克騎兵、騎兵克遠程、弓兵壓制長槍、攻城器克建築。", "目標：完成軍營並擁有至少兩名軍事單位", ("food", 520), ("wood", 420), ("gold", 260)),
        Step("編隊與攻擊移動", "建立編隊，並使用攻擊移動讓軍隊在行進中迎敵。", "目標：建立編隊並下達攻擊移動"),
        Step("發動文明軍令", "每個文明都有彼此平衡的專屬軍令。", "目標：發動一次文明軍令"),
        Step("爭奪王旗霸權", "讓非村民軍隊站在王旗附近六秒即可佔領。", "目標：奪下一座王旗", ("food", 300), ("gold", 220)),
        Step("決定天下", "征服城鎮中心、控制三座王旗，或守住世界奇觀都能勝利。", "教學完成；繼續這場戰局。")
    ];

    public static bool IsComplete(GameEngine engine, int step)
    {
        var game = engine.State;
        bool Flag(string key) => game.Tutorial.Flags.GetValueOrDefault(key);
        return step switch
        {
            0 => Flag("camera"),
            1 => game.Selected.Select(engine.Entity).Any(entity => entity?.Type == "villager"),
            2 => Flag("order"),
            3 => game.Stats.Gathered >= game.Tutorial.GatherStart + 12,
            4 => SettlementBuildings.All(type => game.Entities.Any(entity => !entity.Dead && entity.Faction == 0 && entity.Type == type && entity.Construction >= 1)),
            5 => game.Entities.Count(entity => !entity.Dead && entity.Faction == 0 && entity.Type == "villager") >= 6 && Flag("rally"),
            6 => Flag("saved") && Flag("exported"),
            7 => engine.Player(0).Age >= 2 && (engine.Player(0).Tech.Attack > 0 || engine.Player(0).Tech.Armor > 0 || engine.Player(0).Tech.Economy > 0),
            8 => game.Entities.Any(entity => !entity.Dead && entity.Faction == 0 && entity.Type == "barracks" && entity.Construction >= 1) &&
                 game.Entities.Count(entity => !entity.Dead && entity.Faction == 0 && entity.Kind == "unit" && GameData.Units[entity.Type].Role != "worker") >= 2,
            9 => Flag("group") && Flag("attackMove"),
            10 => Flag("power"),
            11 => game.Sites.Any(site => site.Owner == 0),
            _ => false
        };
    }

    private static TutorialStepDefinition Step(string title, string body, string hint, params (string Key, double Value)[] grants) =>
        new(title, body, hint, grants.ToDictionary(grant => grant.Key, grant => grant.Value, StringComparer.Ordinal));
}
