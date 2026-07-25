namespace Empire.Core;

public sealed partial class GameEngine
{
    private static readonly string[] AiResourcePreferences = ["wood", "food", "gold", "stone"];
    private readonly Dictionary<int, EntityState> _entitiesById = [];
    private readonly Dictionary<int, ResourceNodeState> _nodesById = [];
    private readonly int[] _siteOccupants = new int[4];
    private readonly List<string> _aiTrainOptions = new(16);
    private DeterministicRandom _random;
    private int _nextId;

    private GameEngine(GameState state, int nextId, int randomSeed)
    {
        State = state;
        _nextId = Math.Max(1, nextId);
        _random = new(randomSeed);
        RebuildIndexes();
    }

    public GameState State { get; }
    public int NextId => _nextId;
    public int RandomSeed => _random.Seed;

    public event EventHandler<GameEndedEventArgs>? GameEnded;

    public static GameEngine CreateNew(NewGameOptions? options = null)
    {
        options ??= new();
        if (!GameData.Civilizations.ContainsKey(options.Civilization))
        {
            throw new ArgumentException("文明資料無效。", nameof(options));
        }

        if (!GameData.Difficulties.ContainsKey(options.Difficulty))
        {
            throw new ArgumentException("難度資料無效。", nameof(options));
        }

        var count = Math.Clamp(options.PlayerCount, 2, 4);
        var state = new GameState
        {
            PlayerCount = count,
            Difficulty = options.Difficulty,
            Fog = Enumerable.Repeat((byte)0, GameConstants.MapWidth * GameConstants.MapHeight).ToList(),
            Supremacy = Enumerable.Repeat(0d, count).ToList(),
            Wonder = Enumerable.Repeat(0d, count).ToList(),
            Tutorial = new TutorialState { Active = options.Tutorial }
        };
        var engine = new GameEngine(state, 1, options.Seed);
        WorldGenerator.GenerateTerrain(state);
        engine.InitializePlayers(options.Civilization, count);
        engine.InitializeWorld();
        engine.UpdateFog(force: true);
        return engine;
    }

    internal static GameEngine Restore(GameState state, int nextId, int randomSeed)
    {
        state.PlayerCount = state.Players.Count;
        state.Fog = state.Fog.Count == GameConstants.MapWidth * GameConstants.MapHeight
            ? state.Fog
            : Enumerable.Repeat((byte)0, GameConstants.MapWidth * GameConstants.MapHeight).ToList();
        state.Supremacy = Enumerable.Range(0, state.PlayerCount).Select(index => state.Supremacy.ElementAtOrDefault(index)).ToList();
        state.Wonder = Enumerable.Range(0, state.PlayerCount).Select(index => state.Wonder.ElementAtOrDefault(index)).ToList();
        state.Projectiles = [];
        state.Running = !state.Ended;
        if (state.Ended)
        {
            state.Paused = true;
        }
        WorldGenerator.GenerateTerrain(state);
        var engine = new GameEngine(state, nextId, randomSeed);
        engine.RebuildDerivedValues();
        engine.UpdateFog(force: true);
        return engine;
    }

    public PlayerState Player(int faction) =>
        faction >= 0 && faction < State.Players.Count
            ? State.Players[faction]
            : throw new ArgumentOutOfRangeException(nameof(faction));

    public EntityState? Entity(int id) => _entitiesById.GetValueOrDefault(id);
    public ResourceNodeState? Node(int id) => _nodesById.GetValueOrDefault(id);

    public object? Target(int id) => _entitiesById.TryGetValue(id, out var entity)
        ? entity
        : _nodesById.GetValueOrDefault(id);

    public EntityState CreateUnit(string type, int faction, double x, double y)
    {
        var definition = GameData.Units.GetValueOrDefault(type) ?? throw new ArgumentException("未知兵種。", nameof(type));
        var player = Player(faction);
        var civ = GameData.Civilizations[player.Civ];
        var modifiers = civ.Modifiers;
        var maxHp = definition.Hp * GameRules.Modifier(modifiers.UnitHp, definition);
        var unit = new EntityState
        {
            Id = _nextId++,
            Kind = "unit",
            Type = type,
            Faction = faction,
            Civ = player.Civ,
            X = x,
            Y = y,
            PrevX = x,
            PrevY = y,
            Radius = definition.Role switch { "siege" => 16, "cavalry" => 13, _ => 10 },
            MaxHp = Math.Round(maxHp),
            Hp = Math.Round(maxHp),
            Armor = definition.Armor + GameRules.Modifier(modifiers.UnitArmor, definition, 0) + player.Tech.Armor,
            Speed = definition.Speed * GameRules.Modifier(modifiers.UnitSpeed, definition),
            Damage = definition.Damage * GameRules.Modifier(modifiers.UnitDamage, definition),
            Range = definition.Range * GameRules.Modifier(modifiers.UnitRange, definition),
            Cool = definition.Cooldown * GameRules.Modifier(modifiers.UnitCooldown, definition),
            AttackTimer = _random.NextDouble() * .4,
            Anim = _random.NextDouble() * 10,
            Rally = new(x, y)
        };
        State.Entities.Add(unit);
        _entitiesById.Add(unit.Id, unit);
        player.Pop += definition.Population;
        return unit;
    }

    public EntityState CreateBuilding(string type, int faction, double x, double y, double complete = 1)
    {
        var definition = GameData.Buildings.GetValueOrDefault(type) ?? throw new ArgumentException("未知建築。", nameof(type));
        var player = Player(faction);
        var civ = GameData.Civilizations[player.Civ];
        var maxHp = definition.Hp * civ.Modifiers.BuildingHp;
        var centerX = GameConstants.WorldWidth * .5 - x;
        var centerY = GameConstants.WorldHeight * .5 - y;
        var length = Math.Sqrt(centerX * centerX + centerY * centerY);
        if (length < .001)
        {
            length = 1;
        }

        var building = new EntityState
        {
            Id = _nextId++,
            Kind = "building",
            Type = type,
            Faction = faction,
            Civ = player.Civ,
            X = x,
            Y = y,
            PrevX = x,
            PrevY = y,
            Radius = definition.Size,
            MaxHp = Math.Round(maxHp),
            Hp = Math.Max(1, Math.Round(maxHp * complete)),
            Construction = complete,
            BuildTime = definition.BuildTime,
            Food = definition.Food * (type == "farm" ? civ.Modifiers.FarmYield : 1),
            Rally = new(x + centerX / length * 105, y + centerY / length * 105)
        };
        State.Entities.Add(building);
        _entitiesById.Add(building.Id, building);
        if (complete >= 1 && definition.Population > 0)
        {
            player.PopCap = Math.Min(GameConstants.MaxPopulation, player.PopCap + definition.Population);
        }

        return building;
    }

    public ResourceNodeState CreateResource(string type, double x, double y, double amount = 500, double radius = 18)
    {
        if (!GameConstants.ResourceKeys.Contains(type, StringComparer.Ordinal))
        {
            throw new ArgumentException("未知資源。", nameof(type));
        }

        var node = new ResourceNodeState
        {
            Id = _nextId++,
            Type = type,
            X = x,
            Y = y,
            Amount = amount,
            Radius = radius,
            Wiggle = _random.NextDouble() * 10
        };
        State.Nodes.Add(node);
        _nodesById.Add(node.Id, node);
        return node;
    }

    private void InitializePlayers(string chosenCivilization, int count)
    {
        var pool = GameData.Civilizations.Keys.Where(key => key != chosenCivilization).ToList();
        for (var index = pool.Count - 1; index > 0; index--)
        {
            var swap = _random.Next(index + 1);
            (pool[index], pool[swap]) = (pool[swap], pool[index]);
        }

        State.Players.Add(MakePlayer(0, chosenCivilization));
        for (var faction = 1; faction < count; faction++)
        {
            State.Players.Add(MakePlayer(faction, pool[faction - 1]));
        }

        var difficulty = GameData.Difficulties[State.Difficulty];
        State.Ais.Add(null);
        for (var faction = 1; faction < count; faction++)
        {
            State.Ais.Add(new AiState
            {
                Faction = faction,
                Think = difficulty.ThinkSeconds * (.28 + faction * .12),
                Wave = difficulty.WaveSeconds * (.82 + _random.NextDouble() * .28),
                Build = 7 + faction * 1.15 + _random.NextDouble() * 1.5,
                Train = 2.8 + faction * .45 + _random.NextDouble()
            });
        }
    }

    private PlayerState MakePlayer(int faction, string civilization)
    {
        var baseResources = faction == 0
            ? new ResourceBag { Food = 320, Wood = 280, Gold = 140, Stone = 120 }
            : new ResourceBag { Food = 580, Wood = 520, Gold = 300, Stone = 220 };
        var definition = GameData.Civilizations[civilization];
        foreach (var key in GameConstants.ResourceKeys)
        {
            baseResources[key] = Math.Round(baseResources[key] * GameRules.Modifier(definition.Modifiers.StartResources, key));
        }

        return new PlayerState
        {
            Faction = faction,
            Civ = civilization,
            Color = faction < GameConstants.FactionColors.Length ? GameConstants.FactionColors[faction] : definition.Color,
            Resources = baseResources
        };
    }

    private void InitializeWorld()
    {
        (double X, double Y, int InX, int InY)[] corners =
        [
            (420, GameConstants.WorldHeight - 381, 1, -1),
            (GameConstants.WorldWidth - 420, 380, -1, 1),
            (420, 380, 1, 1),
            (GameConstants.WorldWidth - 420, GameConstants.WorldHeight - 381, -1, -1)
        ];
        var difficulty = GameData.Difficulties[State.Difficulty];
        State.Spawn = corners.Take(State.PlayerCount).Select(corner => new WorldPoint(corner.X, corner.Y)).ToList();

        for (var faction = 0; faction < State.PlayerCount; faction++)
        {
            var corner = corners[faction];
            var length = Math.Sqrt(corner.InX * corner.InX + corner.InY * corner.InY);
            var forwardX = corner.InX / length;
            var forwardY = corner.InY / length;
            var rightX = -forwardY;
            var rightY = forwardX;
            WorldPoint At(double forward, double right) => new(
                Math.Clamp(corner.X + forwardX * forward + rightX * right, 80, GameConstants.WorldWidth - 80),
                Math.Clamp(corner.Y + forwardY * forward + rightY * right, 80, GameConstants.WorldHeight - 80));

            CreateBuilding("town", faction, corner.X, corner.Y);
            for (var index = 0; index < 5; index++)
            {
                var point = At(-82, (index - 2) * 27);
                CreateUnit("villager", faction, point.X, point.Y);
            }

            var location = At(112, 34);
            CreateUnit("scout", faction, location.X, location.Y);
            location = At(-20, 235);
            AddTreeCluster(location.X, location.Y, 18);
            location = At(280, 130);
            AddTreeCluster(location.X, location.Y, 15);
            location = At(110, -220);
            AddMine("gold", location.X, location.Y);
            location = At(245, -20);
            AddMine("stone", location.X, location.Y);
            location = At(-130, -185);
            AddBerries(location.X, location.Y);
            for (var index = 0; index < difficulty.StartingSoldiers; index++)
            {
                location = At(145, (index - (difficulty.StartingSoldiers - 1) * .5) * 25);
                CreateUnit("swordsman", faction, location.X, location.Y);
            }
        }

        (double X, double Y)[] extras = [(850, 520), (900, 1040), (700, 1290), (2010, 750), (1940, 1430), (1740, 1760)];
        for (var index = 0; index < extras.Length; index++)
        {
            var point = extras[index];
            if (index % 3 == 0) AddMine("gold", point.X, point.Y);
            else if (index % 3 == 1) AddTreeCluster(point.X, point.Y, 12);
            else AddBerries(point.X, point.Y);
        }

        int[] fordRows = [9, 21, 33];
        string[] ordinals = ["一", "二", "三"];
        for (var index = 0; index < fordRows.Length; index++)
        {
            var row = fordRows[index];
            State.Sites.Add(new SiteState
            {
                Id = _nextId++,
                X = (GameConstants.MapWidth * .5 + Math.Sin(row * .28) * 2.2) * GameConstants.TileSize + GameConstants.TileSize / 2d,
                Y = row * GameConstants.TileSize + GameConstants.TileSize / 2d,
                Label = $"第{ordinals[index]}王旗"
            });
        }

        State.Camera.X = State.Spawn[0].X;
        State.Camera.Y = State.Spawn[0].Y;
    }

    private void AddTreeCluster(double x, double y, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = 25 + _random.NextDouble() * 105;
            var nodeX = Math.Clamp(x + Math.Cos(angle) * distance, 24, GameConstants.WorldWidth - 24);
            var nodeY = Math.Clamp(y + Math.Sin(angle) * distance, 24, GameConstants.WorldHeight - 24);
            if (WorldGenerator.IsLand(State, nodeX, nodeY))
            {
                CreateResource("wood", nodeX, nodeY, 260, 16);
            }
        }
    }

    private void AddMine(string type, double x, double y)
    {
        for (var index = 0; index < 5; index++)
        {
            var angle = index * Math.PI * 2 / 5 + _random.NextDouble() * .35;
            var distance = index == 0 ? 0 : 25 + _random.NextDouble() * 18;
            CreateResource(type, x + Math.Cos(angle) * distance, y + Math.Sin(angle) * distance, 500, 18);
        }
    }

    private void AddBerries(double x, double y)
    {
        for (var index = 0; index < 7; index++)
        {
            var angle = index * Math.PI * 2 / 7;
            var distance = index == 0 ? 0 : 31;
            CreateResource("food", x + Math.Cos(angle) * distance, y + Math.Sin(angle) * distance, 170, 13);
        }
    }

    private void RebuildIndexes()
    {
        _entitiesById.Clear();
        _nodesById.Clear();
        for (var index = 0; index < State.Entities.Count; index++)
        {
            var entity = State.Entities[index];
            if (!entity.Dead)
            {
                _entitiesById[entity.Id] = entity;
            }
        }

        for (var index = 0; index < State.Nodes.Count; index++)
        {
            var node = State.Nodes[index];
            if (!node.Dead && node.Amount > 0)
            {
                _nodesById[node.Id] = node;
            }
        }
    }

    private void RebuildDerivedValues()
    {
        foreach (var player in State.Players)
        {
            player.Pop = 0;
            player.PopCap = 0;
        }

        foreach (var entity in State.Entities.Where(entity => !entity.Dead))
        {
            var player = Player(entity.Faction);
            entity.Civ = player.Civ;
            var civ = GameData.Civilizations[player.Civ];
            var oldMaximum = Math.Max(1, entity.MaxHp > 0 ? entity.MaxHp : entity.Hp);
            var healthRatio = Math.Clamp(entity.Hp / oldMaximum, 0, 1);
            if (entity.Kind == "unit" && GameData.Units.TryGetValue(entity.Type, out var unit))
            {
                entity.Radius = unit.Role switch { "siege" => 16, "cavalry" => 13, _ => 10 };
                entity.MaxHp = Math.Round(unit.Hp * GameRules.Modifier(civ.Modifiers.UnitHp, unit));
                entity.Hp = Math.Max(1, entity.MaxHp * healthRatio);
                entity.Armor = unit.Armor + GameRules.Modifier(civ.Modifiers.UnitArmor, unit, 0) + player.Tech.Armor;
                entity.Speed = unit.Speed * GameRules.Modifier(civ.Modifiers.UnitSpeed, unit);
                entity.Damage = unit.Damage * GameRules.Modifier(civ.Modifiers.UnitDamage, unit);
                entity.Range = unit.Range * GameRules.Modifier(civ.Modifiers.UnitRange, unit);
                entity.Cool = unit.Cooldown * GameRules.Modifier(civ.Modifiers.UnitCooldown, unit);
                player.Pop += unit.Population;
            }
            else if (entity.Kind == "building" && GameData.Buildings.TryGetValue(entity.Type, out var building))
            {
                entity.Radius = building.Size;
                entity.MaxHp = Math.Round(building.Hp * civ.Modifiers.BuildingHp);
                entity.Hp = Math.Max(1, entity.MaxHp * healthRatio);
                if (entity.Construction >= 1)
                {
                    player.PopCap += building.Population;
                }
            }
        }

        foreach (var player in State.Players)
        {
            player.Pop = Math.Max(0, player.Pop);
            player.PopCap = Math.Clamp(player.PopCap, 0, GameConstants.MaxPopulation);
        }
    }
}

public sealed class GameEndedEventArgs(int winnerFaction, string victoryWay) : EventArgs
{
    public int WinnerFaction { get; } = winnerFaction;
    public string VictoryWay { get; } = victoryWay;
    public bool HumanWon => WinnerFaction == 0;
}
