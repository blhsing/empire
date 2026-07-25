namespace Empire.Core;

public sealed partial class GameEngine
{
    public bool SetMove(int unitId, double x, double y, bool attackMove = false)
    {
        if (!TryUnit(unitId, out var unit))
        {
            return false;
        }

        unit.Path = WorldGenerator.FindPath(State, unit.X, unit.Y, x, y);
        unit.PathIndex = 0;
        unit.Order = new OrderState { Type = attackMove ? "attackMove" : "move", X = x, Y = y };
        unit.Carrying = null;
        if (unit.Faction == 0)
        {
            TutorialEvent("order");
            if (attackMove) TutorialEvent("attackMove");
        }
        return true;
    }

    public bool SetAttack(int unitId, int targetId)
    {
        if (!TryUnit(unitId, out var unit) || !_entitiesById.TryGetValue(targetId, out var target) || target.Dead || target.Faction == unit.Faction)
        {
            return false;
        }

        unit.Order = new OrderState { Type = "attack", TargetId = targetId, X = target.X, Y = target.Y };
        unit.Path.Clear();
        unit.PathIndex = 0;
        return true;
    }

    public bool SetGather(int unitId, int targetId)
    {
        if (!TryVillager(unitId, out var unit))
        {
            return false;
        }

        var target = Target(targetId);
        var valid = target is ResourceNodeState { Dead: false, Amount: > 0 } ||
                    target is EntityState { Kind: "building", Type: "farm", Dead: false } farm && farm.Faction == unit.Faction && farm.Food > 0;
        if (!valid)
        {
            return false;
        }

        var (x, y) = TargetPosition(target!);
        unit.Order = new OrderState { Type = "gather", TargetId = targetId, X = x, Y = y };
        unit.Path.Clear();
        unit.PathIndex = 0;
        return true;
    }

    public bool SetBuild(int unitId, int buildingId)
    {
        if (!TryVillager(unitId, out var unit) || !_entitiesById.TryGetValue(buildingId, out var building) ||
            building.Kind != "building" || building.Dead || building.Faction != unit.Faction || building.Construction >= 1)
        {
            return false;
        }

        unit.Order = new OrderState { Type = "build", TargetId = buildingId, X = building.X, Y = building.Y };
        unit.Path.Clear();
        unit.PathIndex = 0;
        return true;
    }

    public bool Stop(int unitId)
    {
        if (!TryUnit(unitId, out var unit))
        {
            return false;
        }

        StopUnit(unit);
        return true;
    }

    public EntityState? StartBuilding(string type, int faction, double x, double y, IEnumerable<int>? builders = null, bool free = false)
    {
        if (!GameData.Buildings.TryGetValue(type, out var definition) || !CanBuild(faction, type, x, y))
        {
            return null;
        }

        var player = Player(faction);
        var cost = AdjustedBuildingCost(definition, player.Civ);
        if (!free && !Spend(player, cost))
        {
            return null;
        }

        var building = CreateBuilding(type, faction, x, y, 0);
        if (builders is not null)
        {
            foreach (var builder in builders)
            {
                SetBuild(builder, building.Id);
            }
        }

        return building;
    }

    public bool CanBuild(int faction, string type, double x, double y)
    {
        if (!GameData.Buildings.TryGetValue(type, out var definition) || faction < 0 || faction >= State.PlayerCount)
        {
            return false;
        }

        var player = Player(faction);
        if (player.Age < definition.Age || !HasPrerequisites(faction, type))
        {
            return false;
        }

        if (type == "tower" && State.Entities.Count(entity => !entity.Dead && entity.Faction == faction && entity.Type == "tower") >= 4)
        {
            return false;
        }

        if (x < definition.Size || y < definition.Size || x > GameConstants.WorldWidth - definition.Size || y > GameConstants.WorldHeight - definition.Size ||
            !WorldGenerator.IsLand(State, x, y))
        {
            return false;
        }

        foreach (var entity in State.Entities)
        {
            if (!entity.Dead && entity.Kind == "building" && Distance(x, y, entity.X, entity.Y) < definition.Size + entity.Radius + 12)
            {
                return false;
            }
        }

        foreach (var node in State.Nodes)
        {
            if (!node.Dead && node.Amount > 0 && Distance(x, y, node.X, node.Y) < definition.Size + node.Radius + 4)
            {
                return false;
            }
        }

        return true;
    }

    public bool QueueUnit(int buildingId, string type, bool free = false)
    {
        if (!_entitiesById.TryGetValue(buildingId, out var building) || building.Kind != "building" || building.Dead || building.Construction < 1 ||
            !GameData.Units.TryGetValue(type, out var definition))
        {
            return false;
        }

        var player = Player(building.Faction);
        if (player.Age < definition.Age || definition.UniqueCivilization is not null && definition.UniqueCivilization != player.Civ)
        {
            return false;
        }

        var allowed = GameData.TrainingAt.TryGetValue(building.Type, out var regularUnits) && regularUnits.Contains(type, StringComparer.Ordinal) ||
                      definition.UniqueCivilization == player.Civ && definition.TrainAt == building.Type;
        if (!allowed || player.Pop + QueuedPopulation(building.Faction) + definition.Population > Math.Min(GameConstants.MaxPopulation, player.PopCap))
        {
            return false;
        }

        var cost = AdjustedUnitCost(definition, player.Civ);
        if (!free && !Spend(player, cost))
        {
            return false;
        }

        var trainSpeed = GameRules.Modifier(GameData.Civilizations[player.Civ].Modifiers.TrainSpeed, definition);
        var total = definition.TrainTime / trainSpeed;
        building.Queue.Add(new QueueItemState { Type = type, Remaining = total, Total = total });
        return true;
    }

    public bool CancelQueueItem(int buildingId, int queueIndex)
    {
        if (!_entitiesById.TryGetValue(buildingId, out var building) || building.Kind != "building" || building.Dead ||
            queueIndex < 0 || queueIndex >= building.Queue.Count)
        {
            return false;
        }

        var item = building.Queue[queueIndex];
        if (!GameData.Units.TryGetValue(item.Type, out var definition))
        {
            return false;
        }

        building.Queue.RemoveAt(queueIndex);
        Refund(Player(building.Faction), AdjustedUnitCost(definition, Player(building.Faction).Civ), .7);
        return true;
    }

    public bool SetRallyPoint(int buildingId, double x, double y)
    {
        if (!_entitiesById.TryGetValue(buildingId, out var building) || building.Kind != "building" || building.Dead)
        {
            return false;
        }

        building.Rally = new(Math.Clamp(x, 0, GameConstants.WorldWidth), Math.Clamp(y, 0, GameConstants.WorldHeight));
        if (building.Faction == 0) TutorialEvent("rally");
        return true;
    }

    public bool Research(int faction, string technology)
    {
        var player = Player(faction);
        if (player.Age < 2)
        {
            return false;
        }

        var current = technology switch
        {
            "attack" => player.Tech.Attack,
            "armor" => player.Tech.Armor,
            "economy" => player.Tech.Economy,
            _ => -1
        };
        var maximum = technology == "economy" ? 2 : 3;
        if (current < 0 || current >= maximum)
        {
            return false;
        }

        var level = current + 1;
        var cost = technology switch
        {
            "economy" => new ResourceBag { Food = 120 * level, Wood = 100 * level },
            "attack" => new ResourceBag { Food = 150 * level, Gold = 100 * level },
            _ => new ResourceBag { Food = 110 * level, Gold = 110 * level }
        };
        if (!Spend(player, cost))
        {
            return false;
        }

        switch (technology)
        {
            case "attack": player.Tech.Attack++; break;
            case "armor":
                player.Tech.Armor++;
                foreach (var unit in State.Entities.Where(entity => !entity.Dead && entity.Kind == "unit" && entity.Faction == faction))
                {
                    unit.Armor++;
                }
                break;
            case "economy": player.Tech.Economy++; break;
        }

        if (faction == 0) TutorialEvent("tech");
        return true;
    }

    public bool BeginAgeUp(int faction, bool free = false)
    {
        var player = Player(faction);
        if (player.Age >= 4 || player.AgeUp is not null || !MeetsAgeRequirement(player))
        {
            return false;
        }

        if (!free && !Spend(player, AgeCost(player)))
        {
            return false;
        }

        var total = player.Age switch { 1 => 40, 2 => 55, 3 => 70, _ => 0 };
        player.AgeUp = new AgeUpState { To = player.Age + 1, Remaining = total, Total = total };
        if (faction == 0) TutorialEvent("ageQueued");
        return true;
    }

    public bool UsePower(int faction)
    {
        var player = Player(faction);
        if (player.Eliminated || player.Age < 2 || player.PowerReady > State.Time)
        {
            return false;
        }

        var modifiers = GameData.Civilizations[player.Civ].PowerModifiers;
        player.PowerReady = State.Time + 120;
        player.PowerUntil = State.Time + (modifiers.Duration > 0 ? modifiers.Duration : 12);
        if (modifiers.Reveal && faction == 0)
        {
            State.RevealUntil = player.PowerUntil;
        }

        if (modifiers.Heal.Count > 0)
        {
            foreach (var unit in State.Entities.Where(entity => !entity.Dead && entity.Kind == "unit" && entity.Faction == faction))
            {
                var definition = GameData.Units[unit.Type];
                var ratio = GameRules.Modifier(modifiers.Heal, definition, 0);
                if (ratio > 0)
                {
                    unit.Hp = Math.Min(unit.MaxHp, unit.Hp + unit.MaxHp * ratio);
                }
            }
        }

        if (faction == 0) TutorialEvent("power");
        return true;
    }

    public void Select(IEnumerable<int> entityIds, bool append = false)
    {
        if (!append)
        {
            State.Selected.Clear();
        }
        foreach (var id in entityIds)
        {
            if (_entitiesById.TryGetValue(id, out var entity) && !entity.Dead && entity.Faction == 0)
            {
                State.Selected.Add(id);
                entity.Selected = true;
            }
        }
    }

    public ResourceBag AdjustedUnitCost(UnitDefinition definition, string civilization)
    {
        var multiplier = GameRules.Modifier(GameData.Civilizations[civilization].Modifiers.UnitCost, definition);
        return GameRules.Cost(definition.Cost, multiplier);
    }

    public ResourceBag AdjustedBuildingCost(BuildingDefinition definition, string civilization)
    {
        var multiplier = GameRules.Modifier(GameData.Civilizations[civilization].Modifiers.BuildingCost, definition.Key);
        return GameRules.Cost(definition.Cost, multiplier);
    }

    public static bool CanAfford(PlayerState player, ResourceBag cost) =>
        player.Resources.Food >= cost.Food && player.Resources.Wood >= cost.Wood &&
        player.Resources.Gold >= cost.Gold && player.Resources.Stone >= cost.Stone;

    public static bool Spend(PlayerState player, ResourceBag cost)
    {
        if (!CanAfford(player, cost))
        {
            return false;
        }

        player.Resources.Food -= cost.Food;
        player.Resources.Wood -= cost.Wood;
        player.Resources.Gold -= cost.Gold;
        player.Resources.Stone -= cost.Stone;
        return true;
    }

    public static void Refund(PlayerState player, ResourceBag cost, double ratio = 1)
    {
        player.Resources.Food += cost.Food * ratio;
        player.Resources.Wood += cost.Wood * ratio;
        player.Resources.Gold += cost.Gold * ratio;
        player.Resources.Stone += cost.Stone * ratio;
    }

    private bool HasPrerequisites(int faction, string type) =>
        !GameData.BuildingPrerequisites.TryGetValue(type, out var required) || required.All(item => OwnsCompletedBuilding(faction, item));

    private bool MeetsAgeRequirement(PlayerState player) => player.Age switch
    {
        1 => OwnsCompletedBuilding(player.Faction, "mill") && OwnsCompletedBuilding(player.Faction, "lumber"),
        2 => OwnsCompletedBuilding(player.Faction, "blacksmith") &&
             (OwnsCompletedBuilding(player.Faction, "range") || OwnsCompletedBuilding(player.Faction, "stable")),
        3 => OwnsCompletedBuilding(player.Faction, "castle"),
        _ => false
    };

    private ResourceBag AgeCost(PlayerState player)
    {
        var source = player.Age switch
        {
            1 => new ResourceBag { Food = 500 },
            2 => new ResourceBag { Food = 800, Gold = 200 },
            3 => new ResourceBag { Food = 1000, Gold = 800 },
            _ => new ResourceBag()
        };
        var multiplier = GameData.Civilizations[player.Civ].Modifiers.AgeCost;
        source.Food = Math.Ceiling(source.Food * multiplier);
        source.Gold = Math.Ceiling(source.Gold * multiplier);
        return source;
    }

    private bool OwnsCompletedBuilding(int faction, string type) =>
        State.Entities.Any(entity => !entity.Dead && entity.Kind == "building" && entity.Faction == faction && entity.Type == type && entity.Construction >= 1);

    private int QueuedPopulation(int faction) => State.Entities
        .Where(entity => !entity.Dead && entity.Kind == "building" && entity.Faction == faction)
        .SelectMany(entity => entity.Queue)
        .Sum(item => GameData.Units.GetValueOrDefault(item.Type)?.Population ?? 0);

    private bool TryUnit(int id, out EntityState unit)
    {
        if (_entitiesById.TryGetValue(id, out var entity) && entity.Kind == "unit" && !entity.Dead)
        {
            unit = entity;
            return true;
        }

        unit = null!;
        return false;
    }

    private bool TryVillager(int id, out EntityState unit)
    {
        if (TryUnit(id, out unit) && unit.Type == "villager")
        {
            return true;
        }

        unit = null!;
        return false;
    }

    private static (double X, double Y) TargetPosition(object target) => target switch
    {
        EntityState entity => (entity.X, entity.Y),
        ResourceNodeState node => (node.X, node.Y),
        _ => throw new ArgumentException("目標資料無效。", nameof(target))
    };

    private static double TargetRadius(object target) => target switch
    {
        EntityState entity => entity.Radius,
        ResourceNodeState node => node.Radius,
        _ => 0
    };

    private static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
    private static double DistanceSquared(double x1, double y1, double x2, double y2) => (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);
    private static double Distance(EntityState first, EntityState second) => Distance(first.X, first.Y, second.X, second.Y);
}
