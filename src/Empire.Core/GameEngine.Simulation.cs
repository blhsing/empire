namespace Empire.Core;

public sealed partial class GameEngine
{
    private double _accumulator;

    public event EventHandler? AutosaveRequested;

    public int Advance(double elapsedSeconds)
    {
        if (elapsedSeconds <= 0 || State.Paused || State.Ended)
        {
            return 0;
        }

        _accumulator += Math.Min(elapsedSeconds, .25) * Math.Clamp(State.Speed, .25, 4);
        var updates = 0;
        while (_accumulator >= GameConstants.FixedStep && updates < 15)
        {
            Step();
            _accumulator -= GameConstants.FixedStep;
            updates++;
        }

        return updates;
    }

    public void Step()
    {
        if (State.Paused || State.Ended)
        {
            return;
        }

        const double delta = GameConstants.FixedStep;
        State.Time += delta;
        State.Tick++;
        State.Combat = Math.Max(0, State.Combat - delta * .25);

        for (var playerIndex = 0; playerIndex < State.Players.Count; playerIndex++)
        {
            var player = State.Players[playerIndex];
            if (!player.Eliminated)
            {
                UpdateAge(player, delta);
            }
        }

        // Training may append entities. Updating only the count captured at tick start
        // preserves the browser snapshot behavior without allocating an array every tick.
        var entityCountAtTickStart = State.Entities.Count;
        for (var entityIndex = 0; entityIndex < entityCountAtTickStart; entityIndex++)
        {
            var entity = State.Entities[entityIndex];
            if (entity.Dead)
            {
                continue;
            }

            if (entity.Kind == "unit") UpdateUnit(entity, delta);
            else UpdateBuilding(entity, delta);
        }

        UpdateProjectiles(delta);
        UpdateSites(delta);
        UpdateAi(delta);
        UpdateFog();
        UpdateTutorial(delta);

        State.AutoSaveIn -= delta;
        if (State.AutoSaveIn <= 0)
        {
            State.AutoSaveIn = 30;
            AutosaveRequested?.Invoke(this, EventArgs.Empty);
        }

        if (State.Tick % 15 == 0)
        {
            Cleanup();
        }
    }

    private void UpdateUnit(EntityState unit, double delta)
    {
        unit.PrevX = unit.X;
        unit.PrevY = unit.Y;
        unit.Anim += delta * (2 + unit.Speed / 40);
        unit.AttackTimer -= delta;
        unit.Flash = Math.Max(0, unit.Flash - delta);
        unit.ScanTimer -= delta;
        var definition = GameData.Units[unit.Type];
        if (definition.Regeneration > 0 && unit.Hp < unit.MaxHp && State.Time - unit.LastHit > 4)
        {
            unit.Hp = Math.Min(unit.MaxHp, unit.Hp + definition.Regeneration * delta);
        }

        switch (unit.Order.Type)
        {
            case "move":
            case "attackMove":
            {
                var attackMove = unit.Order.Type == "attackMove";
                var completed = FollowPath(unit, delta);
                if (attackMove && unit.ScanTimer <= 0)
                {
                    unit.ScanTimer = .35;
                    var powerRange = PowerUnitRange(unit.Faction, definition);
                    var enemy = NearestEnemy(unit, Math.Max(210, unit.Range * powerRange + 55));
                    if (enemy is not null)
                    {
                        SetAttack(unit.Id, enemy.Id);
                        return;
                    }
                }

                if (completed)
                {
                    StopUnit(unit);
                }
                break;
            }
            case "attack":
                UpdateAttackOrder(unit, definition, delta);
                break;
            case "gather":
                UpdateGatherOrder(unit, delta);
                break;
            case "build":
                UpdateBuildOrder(unit, delta);
                break;
        }
    }

    private void UpdateAttackOrder(EntityState unit, UnitDefinition definition, double delta)
    {
        if (unit.Order.TargetId is not int targetId || !_entitiesById.TryGetValue(targetId, out var target) || target.Dead)
        {
            StopUnit(unit);
            return;
        }

        var rangeMultiplier = PowerUnitRange(unit.Faction, definition);
        var range = unit.Range * rangeMultiplier;
        var reach = range + (range > 50 ? target.Radius : unit.Radius + target.Radius);
        if (Distance(unit, target) > reach)
        {
            EnsurePath(unit, target.X, target.Y);
            FollowPath(unit, delta);
            return;
        }

        unit.Angle = Math.Atan2(target.Y - unit.Y, target.X - unit.X);
        unit.Path.Clear();
        if (unit.AttackTimer <= 0)
        {
            PerformAttack(unit, target, definition);
        }
    }

    private void UpdateGatherOrder(EntityState unit, double delta)
    {
        if (unit.Order.TargetId is not int targetId)
        {
            StopUnit(unit);
            return;
        }

        var target = Target(targetId);
        if (target is ResourceNodeState resource && (resource.Dead || resource.Amount <= 0) ||
            target is EntityState farm && (farm.Dead || farm.Type != "farm" || farm.Food <= 0) || target is null)
        {
            StopUnit(unit);
            return;
        }

        var (targetX, targetY) = TargetPosition(target);
        var reach = TargetRadius(target) + unit.Radius + 6;
        if (Distance(unit.X, unit.Y, targetX, targetY) > reach)
        {
            EnsurePath(unit, targetX, targetY);
            FollowPath(unit, delta);
            return;
        }

        unit.Path.Clear();
        unit.Angle = Math.Atan2(targetY - unit.Y, targetX - unit.X);
        unit.WorkTimer += delta;
        var resourceType = target is ResourceNodeState node ? node.Type : "food";
        var rates = resourceType switch { "food" => .7, "wood" => .65, "gold" => .55, "stone" => .5, _ => 0 };
        var player = Player(unit.Faction);
        var civMultiplier = GameRules.Modifier(GameData.Civilizations[player.Civ].Modifiers.Gather, resourceType);
        var powerMultiplier = PowerGather(unit.Faction, resourceType);
        var technologyMultiplier = 1 + player.Tech.Economy * .12;
        var nearbyMultiplier = EconomicBuildingBonus(unit.Faction, resourceType, targetX, targetY);
        var amount = rates * civMultiplier * powerMultiplier * technologyMultiplier * nearbyMultiplier * delta;

        if (target is ResourceNodeState source)
        {
            amount = Math.Min(amount, source.Amount);
            source.Amount -= amount;
            if (source.Amount <= .001)
            {
                source.Amount = 0;
                source.Dead = true;
            }
        }
        else if (target is EntityState sourceFarm)
        {
            amount = Math.Min(amount, sourceFarm.Food);
            sourceFarm.Food -= amount;
            if (sourceFarm.Food <= .001)
            {
                sourceFarm.Food = 0;
                DestroyEntity(sourceFarm, null);
            }
        }

        player.Resources[resourceType] += amount;
        if (unit.Faction == 0)
        {
            State.Stats.Gathered += amount;
        }
    }

    private void UpdateBuildOrder(EntityState unit, double delta)
    {
        if (unit.Order.TargetId is not int targetId || !_entitiesById.TryGetValue(targetId, out var building) || building.Dead ||
            building.Kind != "building" || building.Faction != unit.Faction)
        {
            StopUnit(unit);
            return;
        }

        if (building.Construction >= 1)
        {
            StopUnit(unit);
            return;
        }

        var reach = building.Radius + unit.Radius + 8;
        if (Distance(unit, building) > reach)
        {
            EnsurePath(unit, building.X, building.Y);
            FollowPath(unit, delta);
            return;
        }

        unit.Path.Clear();
        unit.Angle = Math.Atan2(building.Y - unit.Y, building.X - unit.X);
        unit.WorkTimer += delta;
        var before = building.Construction;
        building.Construction = Math.Min(1, building.Construction + delta / Math.Max(1, building.BuildTime));
        building.Hp = Math.Max(1, building.MaxHp * building.Construction);
        if (before < 1 && building.Construction >= 1)
        {
            FinishBuilding(building);
        }
    }

    private void PerformAttack(EntityState attacker, EntityState target, UnitDefinition definition)
    {
        var damage = AttackDamage(attacker, target, definition);
        var isRanged = attacker.Range > 50 || definition.IsRanged;
        if (isRanged)
        {
            State.Projectiles.Add(new ProjectileState
            {
                SourceFaction = attacker.Faction,
                TargetId = target.Id,
                X = attacker.X,
                Y = attacker.Y,
                Damage = damage,
                Splash = definition.Splash,
                Speed = definition.Role == "siege" ? 300 : 480
            });
        }
        else
        {
            ApplyDamage(target, damage, attacker);
            if (definition.Splash > 0)
            {
                ApplySplash(target.X, target.Y, definition.Splash, damage * .45, attacker.Faction, target.Id);
            }
        }

        var cooldownMultiplier = PowerUnitCooldown(attacker.Faction, definition);
        attacker.AttackTimer = attacker.Cool * cooldownMultiplier;
        State.Combat = Math.Min(1, State.Combat + .08);
    }

    private double AttackDamage(EntityState attacker, EntityState target, UnitDefinition definition)
    {
        var player = Player(attacker.Faction);
        var powerMultiplier = PowerUnitDamage(attacker.Faction, definition);
        var value = (attacker.Damage + player.Tech.Attack) * powerMultiplier;
        var role = target.Kind == "building" ? "building" : GameData.Units[target.Type].Role;
        value += definition.Bonus.GetValueOrDefault(role);
        value += definition.Bonus.GetValueOrDefault(target.Type);
        return Math.Max(1, Math.Round(value - EffectiveArmor(target)));
    }

    private double EffectiveArmor(EntityState target)
    {
        if (target.Kind != "unit")
        {
            return target.Armor;
        }

        var definition = GameData.Units[target.Type];
        return target.Armor + PowerUnitArmor(target.Faction, definition);
    }

    private void ApplyDamage(EntityState target, double damage, EntityState? source)
    {
        if (target.Dead)
        {
            return;
        }

        if (target.Kind == "building")
        {
            var reduction = PowerBuildingReduction(target.Faction);
            damage *= Math.Clamp(1 - reduction, .1, 1);
        }

        target.Hp -= Math.Max(1, damage);
        target.Flash = .16;
        target.LastHit = State.Time;
        if (target.Hp <= 0)
        {
            DestroyEntity(target, source);
        }
    }

    private void UpdateProjectiles(double delta)
    {
        foreach (var projectile in State.Projectiles)
        {
            if (projectile.Dead || !_entitiesById.TryGetValue(projectile.TargetId, out var target) || target.Dead)
            {
                projectile.Dead = true;
                continue;
            }

            var dx = target.X - projectile.X;
            var dy = target.Y - projectile.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var movement = projectile.Speed * delta;
            if (distance <= movement + target.Radius)
            {
                var source = FirstLivingEntity(projectile.SourceFaction);
                ApplyDamage(target, projectile.Damage, source);
                if (projectile.Splash > 0)
                {
                    ApplySplash(target.X, target.Y, projectile.Splash, projectile.Damage * .45, projectile.SourceFaction, target.Id);
                }
                projectile.Dead = true;
            }
            else if (distance > 0)
            {
                projectile.X += dx / distance * movement;
                projectile.Y += dy / distance * movement;
            }
        }

        State.Projectiles.RemoveAll(static projectile => projectile.Dead);
    }

    private void ApplySplash(double x, double y, double radius, double damage, int sourceFaction, int excludedId)
    {
        var source = FirstLivingEntity(sourceFaction);
        var entityCount = State.Entities.Count;
        for (var index = 0; index < entityCount; index++)
        {
            var target = State.Entities[index];
            if (target.Id != excludedId && !target.Dead && target.Faction != sourceFaction && Distance(x, y, target.X, target.Y) <= radius + target.Radius)
            {
                ApplyDamage(target, damage, source);
            }
        }
    }

    private void UpdateBuilding(EntityState building, double delta)
    {
        building.Flash = Math.Max(0, building.Flash - delta);
        building.ActivityFlash = Math.Max(0, building.ActivityFlash - delta);
        if (building.Construction < 1)
        {
            return;
        }

        if (building.Queue.Count > 0)
        {
            var queue = building.Queue[0];
            queue.Remaining -= delta;
            if (queue.Remaining <= 0)
            {
                var angle = _random.NextDouble() * Math.PI * 2;
                var x = building.Rally.X + Math.Cos(angle) * 12;
                var y = building.Rally.Y + Math.Sin(angle) * 12;
                var unit = CreateUnit(queue.Type, building.Faction, x, y);
                building.Queue.RemoveAt(0);
                building.ActivityFlash = .7;
                if (building.Faction == 0)
                {
                    State.Stats.Trained++;
                }
                SetMove(unit.Id, building.Rally.X, building.Rally.Y);
            }
        }

        var definition = GameData.Buildings[building.Type];
        if (definition.Attack > 0)
        {
            building.AttackTimer -= delta;
            if (building.AttackTimer <= 0)
            {
                var target = NearestEnemy(building, definition.Range);
                if (target is not null)
                {
                    var damage = Math.Max(1, definition.Attack - EffectiveArmor(target));
                    State.Projectiles.Add(new ProjectileState
                    {
                        SourceFaction = building.Faction,
                        TargetId = target.Id,
                        X = building.X,
                        Y = building.Y,
                        Damage = damage
                    });
                    building.AttackTimer = definition.Cooldown;
                }
            }
        }

        if (building.Type == "wonder")
        {
            building.WonderTimer += delta;
            State.Wonder[building.Faction] = building.WonderTimer;
            if (building.WonderTimer >= 180)
            {
                EndGame(building.Faction, "奇觀");
            }
        }
    }

    private void FinishBuilding(EntityState building)
    {
        var definition = GameData.Buildings[building.Type];
        var player = Player(building.Faction);
        if (definition.Population > 0)
        {
            player.PopCap = Math.Min(GameConstants.MaxPopulation, player.PopCap + definition.Population);
        }
        if (building.Faction == 0)
        {
            State.Stats.Built++;
        }
    }

    private void UpdateAge(PlayerState player, double delta)
    {
        if (player.AgeUp is null)
        {
            return;
        }

        player.AgeUp.Remaining -= delta;
        if (player.AgeUp.Remaining > 0)
        {
            return;
        }

        player.Age = player.AgeUp.To;
        player.AgeUp = null;
        foreach (var town in State.Entities.Where(entity => !entity.Dead && entity.Faction == player.Faction && entity.Type == "town"))
        {
            town.MaxHp = Math.Round(town.MaxHp * 1.22);
            town.Hp = Math.Min(town.MaxHp, town.Hp + town.MaxHp * .22);
            town.ActivityFlash = 1;
        }
    }

    private void UpdateSites(double delta)
    {
        for (var siteIndex = 0; siteIndex < State.Sites.Count; siteIndex++)
        {
            var site = State.Sites[siteIndex];
            Array.Clear(_siteOccupants, 0, State.PlayerCount);
            for (var entityIndex = 0; entityIndex < State.Entities.Count; entityIndex++)
            {
                var unit = State.Entities[entityIndex];
                if (!unit.Dead && unit.Kind == "unit" && GameData.Units[unit.Type].Role != "worker" && DistanceSquared(unit.X, unit.Y, site.X, site.Y) < 112 * 112)
                {
                    _siteOccupants[unit.Faction]++;
                }
            }

            if (site.Owner >= 0 && !Player(site.Owner).Eliminated)
            {
                Player(site.Owner).Resources.Gold += .4 * delta;
            }

            var presentCount = 0;
            var soleFaction = -1;
            for (var faction = 0; faction < State.PlayerCount; faction++)
            {
                if (_siteOccupants[faction] <= 0)
                {
                    continue;
                }
                presentCount++;
                soleFaction = faction;
            }
            site.Contested = presentCount > 1;
            if (site.Contested)
            {
                continue;
            }

            if (presentCount != 1)
            {
                if (site.Progress < 6)
                {
                    site.CaptureBy = -1;
                    site.Progress = Math.Max(0, site.Progress - delta * .8);
                }
                continue;
            }

            var capturingFaction = soleFaction;
            if (site.CaptureBy != capturingFaction)
            {
                site.CaptureBy = capturingFaction;
                site.Progress = site.Owner == capturingFaction ? 6 : 0;
            }
            site.Progress += delta;
            if (site.Progress >= 6 && site.Owner != capturingFaction)
            {
                site.Owner = capturingFaction;
                site.Progress = 6;
                if (capturingFaction == 0)
                {
                    TutorialEvent("site");
                }
            }
        }

        for (var faction = 0; faction < State.PlayerCount; faction++)
        {
            var ownsEverySite = true;
            for (var siteIndex = 0; siteIndex < State.Sites.Count; siteIndex++)
            {
                if (State.Sites[siteIndex].Owner != faction)
                {
                    ownsEverySite = false;
                    break;
                }
            }
            if (ownsEverySite)
            {
                State.Supremacy[faction] += delta;
            }
            else
            {
                State.Supremacy[faction] = Math.Max(0, State.Supremacy[faction] - delta * 2);
            }

            if (State.Supremacy[faction] >= 90)
            {
                EndGame(faction, "霸權");
            }
        }
    }

    private void DestroyEntity(EntityState entity, EntityState? killer)
    {
        if (entity.Dead)
        {
            return;
        }
        entity.Dead = true;
        if (entity.Kind == "unit")
        {
            var player = Player(entity.Faction);
            player.Pop = Math.Max(0, player.Pop - GameData.Units[entity.Type].Population);
            player.Losses++;
            if (killer is not null)
            {
                Player(killer.Faction).Kills++;
            }
            return;
        }

        var definition = GameData.Buildings[entity.Type];
        var owner = Player(entity.Faction);
        if (definition.Population > 0)
        {
            owner.PopCap = Math.Max(0, owner.PopCap - definition.Population);
        }
        if (entity.Type == "wonder")
        {
            State.Wonder[entity.Faction] = 0;
        }
        if (entity.Type != "town")
        {
            return;
        }

        owner.Eliminated = true;
        foreach (var owned in State.Entities.Where(other => other.Id != entity.Id && other.Faction == entity.Faction))
        {
            owned.Dead = true;
        }
        owner.Pop = 0;
        foreach (var site in State.Sites.Where(site => site.Owner == entity.Faction))
        {
            site.Owner = -1;
            site.Progress = 0;
            site.CaptureBy = -1;
        }
        if (entity.Faction == 0)
        {
            EndGame(-1, "征服");
        }
        else if (State.Players.Skip(1).All(player => player.Eliminated))
        {
            EndGame(0, "征服");
        }
    }

    private void EndGame(int winnerFaction, string way)
    {
        if (State.Ended)
        {
            return;
        }
        State.Ended = true;
        State.Paused = true;
        State.WinnerFaction = winnerFaction;
        State.VictoryWay = way;
        GameEnded?.Invoke(this, new GameEndedEventArgs(winnerFaction, way));
    }

    private bool FollowPath(EntityState unit, double delta)
    {
        if (unit.PathIndex >= unit.Path.Count)
        {
            return true;
        }

        var point = unit.Path[unit.PathIndex];
        var dx = point.X - unit.X;
        var dy = point.Y - unit.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var definition = GameData.Units[unit.Type];
        var multiplier = PowerUnitSpeed(unit.Faction, definition);
        var movement = unit.Speed * multiplier * delta;
        if (distance <= movement || distance < .001)
        {
            unit.X = point.X;
            unit.Y = point.Y;
            unit.PathIndex++;
            return unit.PathIndex >= unit.Path.Count;
        }

        unit.Angle = Math.Atan2(dy, dx);
        unit.X += dx / distance * movement;
        unit.Y += dy / distance * movement;
        return false;
    }

    private void EnsurePath(EntityState unit, double x, double y)
    {
        if (unit.Path.Count == 0 || unit.PathIndex >= unit.Path.Count ||
            Distance(unit.Path[^1].X, unit.Path[^1].Y, x, y) > GameConstants.TileSize * 1.5)
        {
            unit.Path = WorldGenerator.FindPath(State, unit.X, unit.Y, x, y);
            unit.PathIndex = 0;
        }
    }

    private void StopUnit(EntityState unit)
    {
        unit.Order = new OrderState();
        unit.Path.Clear();
        unit.PathIndex = 0;
        unit.Carrying = null;
    }

    private EntityState? NearestEnemy(EntityState source, double range)
    {
        EntityState? best = null;
        var bestDistance = range;
        foreach (var entity in State.Entities)
        {
            if (entity.Dead || entity.Faction == source.Faction || Player(entity.Faction).Eliminated)
            {
                continue;
            }
            var distance = Distance(source, entity) - entity.Radius;
            if (distance < bestDistance)
            {
                best = entity;
                bestDistance = distance;
            }
        }
        return best;
    }

    private ResourceNodeState? NearestResource(EntityState unit, string? preferred = null)
    {
        ResourceNodeState? best = null;
        var bestDistanceSquared = double.PositiveInfinity;
        foreach (var node in State.Nodes)
        {
            if (node.Dead || node.Amount <= 0 || preferred is not null && node.Type != preferred)
            {
                continue;
            }
            var distanceSquared = DistanceSquared(unit.X, unit.Y, node.X, node.Y);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = node;
            }
        }
        return best;
    }

    private double EconomicBuildingBonus(int faction, string resource, double x, double y)
    {
        var desired = resource switch { "food" => "mill", "wood" => "lumber", _ => string.Empty };
        if (desired.Length == 0)
        {
            return 1;
        }
        for (var index = 0; index < State.Entities.Count; index++)
        {
            var entity = State.Entities[index];
            if (!entity.Dead && entity.Faction == faction && entity.Type == desired && entity.Construction >= 1 &&
                DistanceSquared(x, y, entity.X, entity.Y) <= 170 * 170)
            {
                return 1.1;
            }
        }
        return 1;
    }

    private ModifierSet? ActivePower(int faction)
    {
        var player = Player(faction);
        if (player.PowerUntil <= State.Time)
        {
            return null;
        }
        return GameData.Civilizations[player.Civ].PowerModifiers;
    }

    private double PowerUnitRange(int faction, UnitDefinition unit) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.UnitRange, unit) : 1;

    private double PowerUnitCooldown(int faction, UnitDefinition unit) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.UnitCooldown, unit) : 1;

    private double PowerUnitDamage(int faction, UnitDefinition unit) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.UnitDamage, unit) : 1;

    private double PowerUnitSpeed(int faction, UnitDefinition unit) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.UnitSpeed, unit) : 1;

    private double PowerGather(int faction, string resource) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.Gather, resource) : 1;

    private double PowerUnitArmor(int faction, UnitDefinition unit) =>
        ActivePower(faction) is { } power ? GameRules.Modifier(power.UnitArmor, unit, 0) : 0;

    private double PowerBuildingReduction(int faction) => ActivePower(faction)?.BuildingReduction ?? 0;

    private EntityState? FirstLivingEntity(int faction)
    {
        for (var index = 0; index < State.Entities.Count; index++)
        {
            var entity = State.Entities[index];
            if (!entity.Dead && entity.Faction == faction)
            {
                return entity;
            }
        }
        return null;
    }

    private void Cleanup()
    {
        State.Selected.RemoveWhere(id => !_entitiesById.TryGetValue(id, out var entity) || entity.Dead);
        State.Entities.RemoveAll(entity => entity.Dead);
        State.Nodes.RemoveAll(node => node.Dead || node.Amount <= 0);
        RebuildIndexes();
    }
}
