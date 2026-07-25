using System.Text.Json.Serialization;

namespace Empire.Core;

public sealed class ResourceBag
{
    [JsonPropertyName("food")]
    public double Food { get; set; }

    [JsonPropertyName("wood")]
    public double Wood { get; set; }

    [JsonPropertyName("gold")]
    public double Gold { get; set; }

    [JsonPropertyName("stone")]
    public double Stone { get; set; }

    [JsonIgnore]
    public double this[string key]
    {
        get => key switch
        {
            "food" => Food,
            "wood" => Wood,
            "gold" => Gold,
            "stone" => Stone,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "未知資源。")
        };
        set
        {
            switch (key)
            {
                case "food": Food = value; break;
                case "wood": Wood = value; break;
                case "gold": Gold = value; break;
                case "stone": Stone = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "未知資源。");
            }
        }
    }

    public ResourceBag Clone() => new() { Food = Food, Wood = Wood, Gold = Gold, Stone = Stone };
}

public readonly record struct WorldPoint(double X, double Y);

public sealed class CameraState
{
    public double X { get; set; } = GameConstants.WorldWidth / 2d;
    public double Y { get; set; } = GameConstants.WorldHeight / 2d;
    public double Zoom { get; set; } = 1;
    public string Projection { get; set; } = GameConstants.Projection;
}

public sealed class TechnologyState
{
    public int Attack { get; set; }
    public int Armor { get; set; }
    public int Economy { get; set; }
}

public sealed class AgeUpState
{
    public int To { get; set; }
    public double Remaining { get; set; }
    public double Total { get; set; }
}

public sealed class PlayerState
{
    public int Faction { get; set; }
    public string Civ { get; set; } = "britons";
    public string Color { get; set; } = GameConstants.FactionColors[0];

    [JsonPropertyName("res")]
    public ResourceBag Resources { get; set; } = new();

    public int Age { get; set; } = 1;
    public int Pop { get; set; }
    public int PopCap { get; set; }
    public TechnologyState Tech { get; set; } = new();
    public AgeUpState? AgeUp { get; set; }
    public double PowerReady { get; set; }
    public double PowerUntil { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Losses { get; set; }
    public bool Eliminated { get; set; }
}

public sealed class OrderState
{
    public string Type { get; set; } = "idle";
    public int? TargetId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class QueueItemState
{
    public string Type { get; set; } = "villager";
    public double Remaining { get; set; }
    public double Total { get; set; }
}

public sealed class EntityState
{
    public int Id { get; set; }
    public string Kind { get; set; } = "unit";
    public string Type { get; set; } = "villager";
    public int Faction { get; set; }
    public string Civ { get; set; } = "britons";
    public double X { get; set; }
    public double Y { get; set; }
    public double PrevX { get; set; }
    public double PrevY { get; set; }
    public double Radius { get; set; }
    public double MaxHp { get; set; }
    public double Hp { get; set; }
    public double Armor { get; set; }
    public double Speed { get; set; }
    public double Damage { get; set; }
    public double Range { get; set; }
    public double Cool { get; set; }
    public double AttackTimer { get; set; }
    public double Anim { get; set; }
    public double Angle { get; set; }
    public OrderState Order { get; set; } = new();
    public List<WorldPoint> Path { get; set; } = [];
    public int PathIndex { get; set; }
    public bool Selected { get; set; }
    public bool Dead { get; set; }
    public string? Carrying { get; set; }
    public double WorkTimer { get; set; }
    public double Flash { get; set; }
    public double LastHit { get; set; } = -99;
    public double ScanTimer { get; set; }

    // Building fields. Keeping one flat DTO preserves browser v4 compatibility.
    public double Construction { get; set; } = 1;
    public double BuildTime { get; set; } = 1;
    public List<QueueItemState> Queue { get; set; } = [];
    public double ActivityFlash { get; set; }
    public double Food { get; set; }
    public WorldPoint Rally { get; set; }
    public double WonderTimer { get; set; }
}

public sealed class ResourceNodeState
{
    public int Id { get; set; }
    public string Kind { get; set; } = "resource";
    public string Type { get; set; } = "wood";
    public double X { get; set; }
    public double Y { get; set; }
    public double Amount { get; set; }
    public double Radius { get; set; }
    public bool Dead { get; set; }
    public double Wiggle { get; set; }
}

public sealed class SiteState
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Owner { get; set; } = -1;
    public double Progress { get; set; }
    public int CaptureBy { get; set; } = -1;
    public bool Contested { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ProjectileState
{
    public int SourceFaction { get; set; }
    public int TargetId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Speed { get; set; } = 440;
    public double Damage { get; set; }
    public double Splash { get; set; }
    public bool Dead { get; set; }
}

public sealed class AiState
{
    public int Faction { get; set; }
    public double Think { get; set; }
    public double Wave { get; set; }
    public double Build { get; set; }
    public double Train { get; set; }
}

public sealed class GameStats
{
    public double Gathered { get; set; }
    public int Trained { get; set; }
    public int Built { get; set; }
}

public sealed class TutorialState
{
    public bool Active { get; set; }
    public int Step { get; set; }
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.Ordinal);
    public List<int> Granted { get; set; } = [];
    public bool Completed { get; set; }
    public double CheckIn { get; set; }
    public double GatherStart { get; set; }
}

public sealed class GameState
{
    public bool Running { get; set; } = true;
    public bool Paused { get; set; }
    public bool Ended { get; set; }
    public double Time { get; set; }
    public long Tick { get; set; }
    public double Speed { get; set; } = 1;
    public double Combat { get; set; }
    public int PlayerCount { get; set; }
    public CameraState Camera { get; set; } = new();
    public List<PlayerState> Players { get; set; } = [];
    public List<AiState?> Ais { get; set; } = [];
    public List<EntityState> Entities { get; set; } = [];
    public List<ResourceNodeState> Nodes { get; set; } = [];
    public List<SiteState> Sites { get; set; } = [];
    public List<ProjectileState> Projectiles { get; set; } = [];
    public HashSet<int> Selected { get; set; } = [];
    public List<byte> Fog { get; set; } = [];
    public double FogTimer { get; set; }
    public double AutoSaveIn { get; set; } = 30;
    public List<double> Supremacy { get; set; } = [];
    public List<double> Wonder { get; set; } = [];
    public List<WorldPoint> Spawn { get; set; } = [];
    public GameStats Stats { get; set; } = new();
    public TutorialState Tutorial { get; set; } = new();
    public string Difficulty { get; set; } = "征戰";
    public double RevealUntil { get; set; }
    public int? WinnerFaction { get; set; }
    public string? VictoryWay { get; set; }

    [JsonPropertyName("player")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerState? LegacyPlayer { get; set; }

    [JsonPropertyName("enemy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerState? LegacyEnemy { get; set; }

    [JsonIgnore]
    public byte[][] Terrain { get; set; } = [];

    [JsonIgnore]
    public byte[][] Navigation { get; set; } = [];
}
