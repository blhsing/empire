using System.Text.Json;
using System.Text.Json.Serialization;

namespace Empire.Core;

public sealed class GameSaveEnvelope
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 4;
    public string GameVersion { get; set; } = GameConstants.GameVersion;
    public string Projection { get; set; } = GameConstants.Projection;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Seed { get; set; }
    public int NextId { get; set; }
    public string ChosenCiv { get; set; } = "britons";
    public string Difficulty { get; set; } = "征戰";
    public int PlayerCount { get; set; } = 2;
    public GameState Game { get; set; } = new();
}

public sealed class GameSaveService
{
    private const long MaximumSaveBytes = 12 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> LegacyCivilizations = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["jade"] = "chinese", ["steppe"] = "mongols", ["desert"] = "saracens", ["north"] = "teutons",
        ["isles"] = "vikings", ["jungle"] = "goths", ["iron"] = "franks", ["peacock"] = "persians",
        ["assyrians"] = "mongols", ["babylonians"] = "byzantines", ["carthaginians"] = "goths",
        ["choson"] = "japanese", ["egyptians"] = "saracens", ["greeks"] = "byzantines",
        ["hittites"] = "turks", ["macedonians"] = "franks", ["minoans"] = "britons",
        ["palmyrans"] = "saracens", ["phoenicians"] = "vikings", ["romans"] = "teutons",
        ["shang"] = "chinese", ["sumerians"] = "goths", ["yamato"] = "japanese"
    };

    private static readonly IReadOnlyDictionary<string, string> LegacyUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["repeater"] = "chuKoNu", ["horseArcher"] = "mangudai", ["camel"] = "mameluke",
        ["axeguard"] = "teutonicKnight", ["windRanger"] = "berserk", ["eagle"] = "huskarl",
        ["elephant"] = "warElephant", ["assyrianChariot"] = "mangudai", ["babylonianGuard"] = "cataphract",
        ["sacredBand"] = "huskarl", ["chosonGuard"] = "samurai", ["egyptianChariot"] = "mameluke",
        ["greekHoplite"] = "cataphract", ["hittiteChariot"] = "janissary", ["companion"] = "throwingAxeman",
        ["cretanArcher"] = "longbowman", ["palmyranCamel"] = "mameluke", ["immortal"] = "warElephant",
        ["phoenicianElephant"] = "berserk", ["legion"] = "teutonicKnight", ["shangHalberd"] = "chuKoNu",
        ["sumerianChariot"] = "huskarl", ["yamatoRider"] = "samurai"
    };

    public GameSaveService(string? autosavePath = null)
    {
        AutosavePath = autosavePath ?? DefaultAutosavePath;
    }

    public static string DefaultAutosavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "帝國餘燼",
        "saves",
        "autosave-v4.json");

    public string AutosavePath { get; }

    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();
    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonOptions)
    {
        WriteIndented = true
    };

    public bool HasAutosave => File.Exists(AutosavePath);

    public void AttachAutosave(GameEngine engine) => engine.AutosaveRequested += OnAutosaveRequested;
    public void DetachAutosave(GameEngine engine) => engine.AutosaveRequested -= OnAutosaveRequested;

    public string Serialize(GameEngine engine, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var envelope = CreateEnvelope(engine);
        return JsonSerializer.Serialize(envelope, indented ? IndentedJsonOptions : JsonOptions);
    }

    public GameEngine Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var envelope = JsonSerializer.Deserialize<GameSaveEnvelope>(json, JsonOptions) ?? throw new InvalidDataException("存檔內容為空。 ");
        ValidateStructure(envelope);
        if (envelope.Version is < 1 or > 4)
        {
            throw new InvalidDataException("不支援的存檔版本。 ");
        }
        Migrate(envelope);
        Validate(envelope);
        return GameEngine.Restore(envelope.Game, envelope.NextId, envelope.Seed);
    }

    public void SaveAutosave(GameEngine engine) => SaveToFile(engine, AutosavePath, indented: false);

    public GameEngine LoadAutosave()
    {
        if (!File.Exists(AutosavePath))
        {
            throw new FileNotFoundException("尚未找到自動存檔。", AutosavePath);
        }
        return Import(AutosavePath);
    }

    public void Export(GameEngine engine, string path) => SaveToFile(engine, path, indented: true);

    public GameEngine Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("找不到存檔。", path);
        }
        if (info.Length > MaximumSaveBytes)
        {
            throw new InvalidDataException("存檔超過 12 MB 上限。 ");
        }
        return Deserialize(File.ReadAllText(path));
    }

    public void SaveToFile(GameEngine engine, string path, bool indented)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("存檔路徑沒有有效目錄。 ");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, Serialize(engine, indented));
            File.Move(temporaryPath, fullPath, overwrite: true);
            engine.TutorialEvent("saved");
            if (fullPath != Path.GetFullPath(AutosavePath))
            {
                engine.TutorialEvent("exported");
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static GameSaveEnvelope CreateEnvelope(GameEngine engine)
    {
        var stateJson = JsonSerializer.Serialize(engine.State, JsonOptions);
        var state = JsonSerializer.Deserialize<GameState>(stateJson, JsonOptions) ?? throw new InvalidOperationException("無法建立存檔快照。 ");
        state.Entities.RemoveAll(entity => entity.Dead);
        state.Nodes.RemoveAll(node => node.Dead || node.Amount <= 0);
        var liveIds = state.Entities.Select(entity => entity.Id).ToHashSet();
        state.Selected.RemoveWhere(id => !liveIds.Contains(id));
        state.Projectiles.Clear();
        state.AutoSaveIn = 30;
        state.LegacyPlayer = null;
        state.LegacyEnemy = null;
        return new GameSaveEnvelope
        {
            Seed = engine.RandomSeed,
            NextId = engine.NextId,
            ChosenCiv = engine.Player(0).Civ,
            Difficulty = state.Difficulty,
            PlayerCount = state.PlayerCount,
            Game = state
        };
    }

    private static void Migrate(GameSaveEnvelope envelope)
    {
        var game = envelope.Game;
        if (game.Players.Count == 0)
        {
            if (game.LegacyPlayer is not null) game.Players.Add(game.LegacyPlayer);
            if (game.LegacyEnemy is not null) game.Players.Add(game.LegacyEnemy);
        }
        game.LegacyPlayer = null;
        game.LegacyEnemy = null;

        envelope.ChosenCiv = CurrentCivilization(!string.IsNullOrWhiteSpace(envelope.ChosenCiv) ? envelope.ChosenCiv : game.Players.FirstOrDefault()?.Civ);
        for (var index = 0; index < game.Players.Count; index++)
        {
            var player = game.Players[index];
            player.Faction = index;
            player.Civ = CurrentCivilization(player.Civ);
            player.Color = index < GameConstants.FactionColors.Length ? GameConstants.FactionColors[index] : GameData.Civilizations[player.Civ].Color;
        }

        foreach (var entity in game.Entities)
        {
            entity.Civ = entity.Faction >= 0 && entity.Faction < game.Players.Count ? game.Players[entity.Faction].Civ : envelope.ChosenCiv;
            if (entity.Kind == "unit" && (string.IsNullOrWhiteSpace(entity.Type) || !GameData.Units.ContainsKey(entity.Type)))
            {
                entity.Type = LegacyUnits.GetValueOrDefault(entity.Type ?? string.Empty, "spear");
            }
            if (entity.Kind == "building")
            {
                entity.Queue ??= [];
                if (entity.Rally == default)
                {
                    entity.Rally = new WorldPoint(entity.X, entity.Y);
                }
                foreach (var queue in entity.Queue)
                {
                    if (string.IsNullOrWhiteSpace(queue.Type) || !GameData.Units.ContainsKey(queue.Type))
                    {
                        queue.Type = LegacyUnits.GetValueOrDefault(queue.Type ?? string.Empty, "villager");
                    }
                    if (queue.Total <= 0)
                    {
                        queue.Total = Math.Max(.01, queue.Remaining);
                    }
                }
            }
        }

        game.PlayerCount = game.Players.Count;
        game.Difficulty = !string.IsNullOrWhiteSpace(envelope.Difficulty) && GameData.Difficulties.ContainsKey(envelope.Difficulty) ? envelope.Difficulty : "征戰";
        // Browser v1-v4 saves only persisted the ended flag, without winner or
        // victory metadata. Their historical loader resumed those matches, so
        // preserve that compatibility while retaining complete native endings.
        if (game.Ended && (game.WinnerFaction is null || string.IsNullOrWhiteSpace(game.VictoryWay)))
        {
            game.Ended = false;
            game.Paused = false;
            game.WinnerFaction = null;
            game.VictoryWay = null;
        }
        while (game.Ais.Count < game.PlayerCount) game.Ais.Add(null);
        game.Ais[0] = null;
        var difficulty = GameData.Difficulties[game.Difficulty];
        for (var faction = 1; faction < game.PlayerCount; faction++)
        {
            game.Ais[faction] ??= new AiState { Faction = faction, Think = 0, Wave = difficulty.WaveSeconds, Build = 8, Train = 4 };
            game.Ais[faction]!.Faction = faction;
        }
        envelope.PlayerCount = game.PlayerCount;
        envelope.Difficulty = game.Difficulty;
        envelope.Projection = GameConstants.Projection;
        envelope.NextId = Math.Max(envelope.NextId, Math.Max(
            game.Entities.Select(entity => entity.Id).DefaultIfEmpty().Max(),
            game.Nodes.Select(node => node.Id).Concat(game.Sites.Select(site => site.Id)).DefaultIfEmpty().Max()) + 1);
    }

    private static string CurrentCivilization(string? civilization)
    {
        if (civilization is not null && GameData.Civilizations.ContainsKey(civilization))
        {
            return civilization;
        }
        return civilization is not null && LegacyCivilizations.TryGetValue(civilization, out var mapped) ? mapped : "britons";
    }

    private static void ValidateStructure(GameSaveEnvelope envelope)
    {
        var game = envelope.Game ?? throw new InvalidDataException("存檔缺少戰局資料。 ");
        if (game.Camera is null || game.Players is null || game.Ais is null ||
            game.Entities is null || game.Nodes is null || game.Sites is null ||
            game.Projectiles is null || game.Selected is null || game.Fog is null ||
            game.Supremacy is null || game.Wonder is null || game.Spawn is null ||
            game.Stats is null || game.Tutorial is null || game.Tutorial.Flags is null ||
            game.Tutorial.Granted is null)
        {
            throw new InvalidDataException("存檔結構不完整。 ");
        }

        static bool InvalidPlayer(PlayerState? player) =>
            player is null || player.Resources is null || player.Tech is null;

        if (game.Players.Any(InvalidPlayer) || InvalidPlayer(game.LegacyPlayer) && game.LegacyPlayer is not null ||
            InvalidPlayer(game.LegacyEnemy) && game.LegacyEnemy is not null ||
            game.Entities.Any(entity => entity is null || entity.Order is null || entity.Path is null ||
                entity.Queue is null || entity.Queue.Any(item => item is null)) ||
            game.Nodes.Any(node => node is null) || game.Sites.Any(site => site is null) ||
            game.Projectiles.Any(projectile => projectile is null))
        {
            throw new InvalidDataException("存檔包含不完整的物件。 ");
        }
    }

    private static void Validate(GameSaveEnvelope envelope)
    {
        if (envelope.Version is < 1 or > 4)
        {
            throw new InvalidDataException("不支援的存檔版本。 ");
        }
        var game = envelope.Game;
        if (game.Players.Count is < 2 or > 4)
        {
            throw new InvalidDataException("玩家數量無效。 ");
        }
        if (game.Entities.Count > 5000 || game.Nodes.Count > 5000 || game.Sites.Count is < 1 or > 16 ||
            game.Fog.Count != GameConstants.MapWidth * GameConstants.MapHeight || game.Spawn.Count < game.Players.Count)
        {
            throw new InvalidDataException("戰局資料尺寸異常。 ");
        }
        if (string.IsNullOrWhiteSpace(envelope.ChosenCiv) || !GameData.Civilizations.ContainsKey(envelope.ChosenCiv) ||
            string.IsNullOrWhiteSpace(game.Difficulty) || !GameData.Difficulties.ContainsKey(game.Difficulty) ||
            game.Ended && (game.WinnerFaction is null || string.IsNullOrWhiteSpace(game.VictoryWay)))
        {
            throw new InvalidDataException("文明或難度資料無效。 ");
        }

        if (!Finite(game.Camera.X) || !Finite(game.Camera.Y) || !Finite(game.Camera.Zoom) || game.Camera.Zoom <= 0 ||
            !Finite(game.Time) || game.Time < 0 || game.Tick < 0 || !Finite(game.Speed) || game.Speed <= 0 ||
            !Finite(game.Stats.Gathered) || game.Stats.Gathered < 0)
        {
            throw new InvalidDataException("戰局狀態數值無效。 ");
        }

        var ids = new HashSet<int>();
        foreach (var entity in game.Entities)
        {
            if (entity.Id <= 0 || !ids.Add(entity.Id) || entity.Faction < 0 || entity.Faction >= game.Players.Count ||
                !Finite(entity.X) || !Finite(entity.Y) || entity.X < 0 || entity.Y < 0 || entity.X > GameConstants.WorldWidth || entity.Y > GameConstants.WorldHeight ||
                !Finite(entity.Hp) || entity.Hp < 0 || entity.Kind is not "unit" and not "building" ||
                entity.Order is null || string.IsNullOrWhiteSpace(entity.Order.Type) ||
                entity.Path.Any(point => !Finite(point.X) || !Finite(point.Y)) ||
                entity.Kind == "unit" && (string.IsNullOrWhiteSpace(entity.Type) || !GameData.Units.ContainsKey(entity.Type)) ||
                entity.Kind == "building" && (string.IsNullOrWhiteSpace(entity.Type) || !GameData.Buildings.ContainsKey(entity.Type) ||
                    !Finite(entity.Rally.X) || !Finite(entity.Rally.Y) || entity.Rally.X < 0 || entity.Rally.Y < 0 ||
                    entity.Rally.X > GameConstants.WorldWidth || entity.Rally.Y > GameConstants.WorldHeight ||
                    entity.Queue.Any(item => string.IsNullOrWhiteSpace(item.Type) || !GameData.Units.ContainsKey(item.Type) || !Finite(item.Remaining) || !Finite(item.Total))))
            {
                throw new InvalidDataException("實體資料無效。 ");
            }
        }

        foreach (var node in game.Nodes)
        {
            if (node.Id <= 0 || !ids.Add(node.Id) || !GameConstants.ResourceKeys.Contains(node.Type, StringComparer.Ordinal) ||
                !Finite(node.X) || !Finite(node.Y) || !Finite(node.Amount) || node.Amount < 0)
            {
                throw new InvalidDataException("資源資料無效。 ");
            }
        }

        foreach (var site in game.Sites)
        {
            if (site.Id <= 0 || !ids.Add(site.Id) || !Finite(site.X) || !Finite(site.Y) || !Finite(site.Progress) ||
                site.X < 0 || site.Y < 0 || site.X > GameConstants.WorldWidth || site.Y > GameConstants.WorldHeight || site.Progress < 0 ||
                site.Owner < -1 || site.Owner >= game.Players.Count || site.CaptureBy < -1 || site.CaptureBy >= game.Players.Count)
            {
                throw new InvalidDataException("王旗資料無效。 ");
            }
        }

        for (var spawnIndex = 0; spawnIndex < game.Players.Count; spawnIndex++)
        {
            var spawn = game.Spawn[spawnIndex];
            if (!Finite(spawn.X) || !Finite(spawn.Y) || spawn.X < 0 || spawn.Y < 0 ||
                spawn.X > GameConstants.WorldWidth || spawn.Y > GameConstants.WorldHeight)
            {
                throw new InvalidDataException("出生點資料無效。 ");
            }
        }

        for (var faction = 1; faction < game.Players.Count; faction++)
        {
            var ai = game.Ais[faction];
            if (ai is null || ai.Faction != faction || !Finite(ai.Think) || !Finite(ai.Wave) || !Finite(ai.Build) || !Finite(ai.Train))
            {
                throw new InvalidDataException("人工智慧資料無效。 ");
            }
        }

        foreach (var player in game.Players)
        {
            if (!GameData.Civilizations.ContainsKey(player.Civ) || player.Age is < 1 or > 4 ||
                GameConstants.ResourceKeys.Any(key => !Finite(player.Resources[key]) || player.Resources[key] < 0))
            {
                throw new InvalidDataException("玩家資料無效。 ");
            }
        }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private void OnAutosaveRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is GameEngine engine)
        {
            SaveAutosave(engine);
        }
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
