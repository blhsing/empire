using System;
using System.Collections.Generic;
using Empire.Core;
using Empire.Game.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Empire.Game.Rendering;

/// <summary>
/// High-throughput top-down renderer for the native simulation. It owns no game
/// state and deliberately renders no text; HUD/localized labels remain the
/// caller's responsibility.
/// </summary>
public sealed class WorldRenderer : IDisposable
{
    private const float HalfPi = MathF.PI * .5f;
    private const float Tau = MathF.PI * 2f;
    private const int MaxQueuedEffects = 512;

    private static readonly SceneryComparer ScenerySort = new();
    private static readonly UnitComparer UnitSort = new();
    private static readonly ProjectileComparer ProjectileSort = new();

    private readonly GraphicsDevice _graphicsDevice;
    private readonly GameAssets _assets;
    private readonly SpriteBatch _batch;
    private readonly bool _ownsBatch;
    private readonly Texture2D _softEllipse;
    private readonly Texture2D _ellipseRing;

    // These buffers retain their capacity between frames. No LINQ, iterator or
    // transient render-object allocation is used in DrawWorld.
    private readonly List<SceneryItem> _scenery = new(512);
    private readonly List<UnitItem> _units = new(256);
    private readonly List<ProjectileItem> _projectiles = new(128);
    private readonly List<EffectItem> _effects = new(256);
    private readonly Dictionary<int, EntityState> _entitiesById = new(512);
    private readonly Dictionary<int, ResourceNodeState> _nodesById = new(512);
    private readonly Color[] _teamColors = new Color[16];

    private Matrix _cameraTransform;
    private RectangleF _worldView;
    private float _zoom = 1f;
    private float _inverseZoom = 1f;
    private double _time;
    private int _viewerFaction;
    private bool _disposed;

    public WorldRenderer(GraphicsDevice graphicsDevice, GameAssets assets, SpriteBatch? spriteBatch = null)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _batch = spriteBatch ?? new SpriteBatch(graphicsDevice);
        _ownsBatch = spriteBatch is null;
        _softEllipse = CreateEllipseTexture(graphicsDevice, ring: false);
        _ellipseRing = CreateEllipseTexture(graphicsDevice, ring: true);
    }

    /// <summary>
    /// Draws the complete world using state.Camera. The caller must not have an
    /// active SpriteBatch.Begin when invoking this method.
    /// </summary>
    public void DrawWorld(GameState state, float interpolationAlpha = 1f, int viewerFaction = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        DrawWorld(state, state.Camera, DeviceViewportRectangle(), interpolationAlpha, viewerFaction);
    }

    /// <summary>
    /// Draws using a HUD-safe screen viewport. The camera is centred inside this
    /// rectangle rather than the full back buffer; UI may overdraw its edges, so
    /// no scissor state is required.
    /// </summary>
    public void DrawWorld(GameState state, Rectangle screenViewport, float interpolationAlpha = 1f, int viewerFaction = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        DrawWorld(state, state.Camera, screenViewport, interpolationAlpha, viewerFaction);
    }

    /// <summary>
    /// Draws the complete world with an explicit camera, useful for replays and
    /// spectator views. state.Fog is interpreted as the caller's current fog
    /// perspective; enemy post-fog overlays are always visibility-gated.
    /// </summary>
    public void DrawWorld(GameState state, CameraState camera, float interpolationAlpha = 1f, int viewerFaction = 0)
    {
        DrawWorld(state, camera, DeviceViewportRectangle(), interpolationAlpha, viewerFaction);
    }

    /// <summary>
    /// Explicit-camera and explicit-screen-viewport rendering entry point.
    /// </summary>
    public void DrawWorld(GameState state, CameraState camera, Rectangle screenViewport, float interpolationAlpha = 1f, int viewerFaction = 0)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(camera);
        if (!_assets.IsLoaded)
        {
            throw new InvalidOperationException("GameAssets.Load must be called before DrawWorld.");
        }

        interpolationAlpha = Math.Clamp(interpolationAlpha, 0f, 1f);
        _viewerFaction = viewerFaction;
        _time = state.Time + interpolationAlpha * GameConstants.FixedStep;
        ConfigureCamera(camera, screenViewport);
        PrepareFrame(state, interpolationAlpha);

        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            effect: null,
            transformMatrix: _cameraTransform);

        DrawTerrain(state);
        DrawScenery(state);
        DrawUnits(state);
        DrawProjectiles(state);
        DrawFog(state);
        DrawFogSafeOverlays(state);
        _batch.End();

        if (_effects.Count > 0)
        {
            _batch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Additive,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: _cameraTransform);
            DrawAdditiveEffects();
            _batch.End();
        }
    }

    /// <summary>
    /// Draws a compact, fog-safe minimap into a screen-space rectangle. Call it
    /// outside any other active SpriteBatch block.
    /// </summary>
    public void DrawMinimap(GameState state, Rectangle destination, int viewerFaction = 0, bool drawCamera = true)
    {
        DrawMinimap(state, destination, DeviceViewportRectangle(), viewerFaction, drawCamera);
    }

    /// <summary>
    /// Minimap overload whose camera rectangle matches the same HUD-safe screen
    /// viewport used by DrawWorld.
    /// </summary>
    public void DrawMinimap(GameState state, Rectangle destination, Rectangle screenViewport, int viewerFaction = 0, bool drawCamera = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(state);
        if (!_assets.IsLoaded)
        {
            throw new InvalidOperationException("GameAssets.Load must be called before DrawMinimap.");
        }

        if (destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        _viewerFaction = viewerFaction;
        CacheTeamColors(state);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        DrawScreenRect(destination, new Color(5, 11, 14));

        for (var y = 0; y < GameConstants.MapHeight; y++)
        {
            for (var x = 0; x < GameConstants.MapWidth; x++)
            {
                var x0 = destination.X + x * destination.Width / GameConstants.MapWidth;
                var x1 = destination.X + (x + 1) * destination.Width / GameConstants.MapWidth;
                var y0 = destination.Y + y * destination.Height / GameConstants.MapHeight;
                var y1 = destination.Y + (y + 1) * destination.Height / GameConstants.MapHeight;
                var terrain = TerrainAt(state, x, y);
                var color = terrain switch
                {
                    1 => new Color(37, 87, 96),
                    2 => new Color(125, 91, 53),
                    3 => new Color(112, 112, 104),
                    _ => new Color(77, 105, 62)
                };
                var fog = FogAtCell(state, x, y);
                color = fog switch
                {
                    0 => Color.Lerp(color, new Color(2, 5, 8), .9f),
                    1 => Color.Lerp(color, new Color(4, 9, 13), .53f),
                    _ => color
                };
                DrawScreenRect(new Rectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)), color);
            }
        }

        // Current-state neutral/enemy objects only appear while visible. This
        // avoids leaking depletion, capture or movement through explored fog.
        for (var i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            if (node.Dead || !IsVisible(state, node.X, node.Y))
            {
                continue;
            }
            DrawMinimapPoint(destination, node.X, node.Y, node.Type == "gold" ? new Color(242, 194, 78) : node.Type == "stone" ? new Color(184, 191, 190) : node.Type == "food" ? new Color(205, 105, 75) : new Color(68, 116, 68), 2);
        }

        for (var i = 0; i < state.Sites.Count; i++)
        {
            var site = state.Sites[i];
            if (!IsVisible(state, site.X, site.Y))
            {
                continue;
            }
            DrawMinimapPoint(destination, site.X, site.Y, TeamColor(site.Owner), 4);
        }

        for (var i = 0; i < state.Entities.Count; i++)
        {
            var entity = state.Entities[i];
            if (entity.Dead || (entity.Faction != viewerFaction && !IsVisible(state, entity.X, entity.Y)))
            {
                continue;
            }
            DrawMinimapPoint(destination, entity.X, entity.Y, TeamColor(entity.Faction), entity.Kind == "building" ? 4 : 3);
        }

        if (drawCamera)
        {
            var zoom = Math.Clamp((float)state.Camera.Zoom, .1f, 8f);
            var viewportWidth = Math.Max(1, screenViewport.Width);
            var viewportHeight = Math.Max(1, screenViewport.Height);
            var left = (float)state.Camera.X - viewportWidth / (zoom * 2f);
            var top = (float)state.Camera.Y - viewportHeight / (zoom * 2f);
            var right = (float)state.Camera.X + viewportWidth / (zoom * 2f);
            var bottom = (float)state.Camera.Y + viewportHeight / (zoom * 2f);
            var cameraRect = WorldRectToMinimap(destination, left, top, right, bottom);
            DrawScreenRectangleOutline(cameraRect, Color.White * .82f, 1);
        }

        DrawScreenRectangleOutline(destination, new Color(226, 191, 112) * .8f, 1);
        _batch.End();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _softEllipse.Dispose();
        _ellipseRing.Dispose();
        if (_ownsBatch)
        {
            _batch.Dispose();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ConfigureCamera(CameraState camera, Rectangle screenViewport)
    {
        if (screenViewport.Width <= 0 || screenViewport.Height <= 0)
        {
            screenViewport = DeviceViewportRectangle();
        }
        _zoom = Math.Clamp((float)camera.Zoom, .2f, 4f);
        _inverseZoom = 1f / _zoom;
        var cameraX = (float)camera.X;
        var cameraY = (float)camera.Y;
        _cameraTransform =
            Matrix.CreateTranslation(-cameraX, -cameraY, 0f) *
            Matrix.CreateScale(_zoom, _zoom, 1f) *
            Matrix.CreateTranslation(screenViewport.X + screenViewport.Width * .5f, screenViewport.Y + screenViewport.Height * .5f, 0f);

        var halfWidth = screenViewport.Width * _inverseZoom * .5f;
        var halfHeight = screenViewport.Height * _inverseZoom * .5f;
        _worldView = new RectangleF(cameraX - halfWidth, cameraY - halfHeight, halfWidth * 2f, halfHeight * 2f);
    }

    private void PrepareFrame(GameState state, float interpolationAlpha)
    {
        _scenery.Clear();
        _units.Clear();
        _projectiles.Clear();
        _effects.Clear();
        _entitiesById.Clear();
        _nodesById.Clear();
        CacheTeamColors(state);

        for (var i = 0; i < state.Entities.Count; i++)
        {
            var entity = state.Entities[i];
            if (!entity.Dead)
            {
                _entitiesById[entity.Id] = entity;
            }
        }
        for (var i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            if (!node.Dead)
            {
                _nodesById[node.Id] = node;
            }
        }

        var cullMargin = 180f * _inverseZoom;
        for (var i = 0; i < state.Nodes.Count; i++)
        {
            var node = state.Nodes[i];
            // Drawing live resource state through explored fog would reveal an
            // unseen enemy's depletion, so neutral resources are visibility-only.
            if (!node.Dead && InView((float)node.X, (float)node.Y, cullMargin) && IsVisible(state, node.X, node.Y))
            {
                _scenery.Add(SceneryItem.ForNode(node));
            }
        }

        for (var i = 0; i < state.Sites.Count; i++)
        {
            var site = state.Sites[i];
            if (InView((float)site.X, (float)site.Y, cullMargin) && IsVisible(state, site.X, site.Y))
            {
                _scenery.Add(SceneryItem.ForSite(site));
            }
        }

        for (var i = 0; i < state.Entities.Count; i++)
        {
            var entity = state.Entities[i];
            if (entity.Dead)
            {
                continue;
            }
            var x = Lerp(entity.PrevX, entity.X, interpolationAlpha);
            var y = Lerp(entity.PrevY, entity.Y, interpolationAlpha);
            if (!InView(x, y, cullMargin))
            {
                continue;
            }
            if (entity.Faction != _viewerFaction && !IsVisible(state, x, y))
            {
                continue;
            }
            if (entity.Kind == "unit")
            {
                _units.Add(new UnitItem(entity, new Vector2(x, y)));
            }
            else
            {
                _scenery.Add(SceneryItem.ForBuilding(entity));
            }
        }

        for (var i = 0; i < state.Projectiles.Count; i++)
        {
            var projectile = state.Projectiles[i];
            if (!projectile.Dead && InView((float)projectile.X, (float)projectile.Y, cullMargin) && IsVisible(state, projectile.X, projectile.Y))
            {
                _projectiles.Add(new ProjectileItem(projectile));
            }
        }

        _scenery.Sort(ScenerySort);
        _units.Sort(UnitSort);
        _projectiles.Sort(ProjectileSort);
    }

    private void CacheTeamColors(GameState state)
    {
        for (var i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = i < GameConstants.FactionColors.Length
                ? ParseHexColor(GameConstants.FactionColors[i], Color.White)
                : Color.White;
        }
        for (var i = 0; i < state.Players.Count && i < _teamColors.Length; i++)
        {
            var player = state.Players[i];
            if ((uint)player.Faction < (uint)_teamColors.Length)
            {
                _teamColors[player.Faction] = ParseHexColor(player.Color, _teamColors[player.Faction]);
            }
        }
    }

    private void DrawTerrain(GameState state)
    {
        var tile = GameConstants.TileSize;
        var minX = Math.Clamp((int)MathF.Floor(_worldView.Left / tile) - 1, 0, GameConstants.MapWidth - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(_worldView.Right / tile) + 1, 0, GameConstants.MapWidth - 1);
        var minY = Math.Clamp((int)MathF.Floor(_worldView.Top / tile) - 1, 0, GameConstants.MapHeight - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(_worldView.Bottom / tile) + 1, 0, GameConstants.MapHeight - 1);

        var material = _assets.MaterialTerrainAtlas;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var terrain = TerrainAt(state, x, y);
                var source = TerrainSource(material, terrain, x, y, medievalLayout: false);
                _batch.Draw(material, new Rectangle(x * tile, y * tile, tile + 1, tile + 1), source, Color.White);
            }
        }

        var medieval = _assets.MedievalTerrainAtlas;
        var waterPulse = .39f + MathF.Sin((float)_time * 1.35f) * .035f;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var terrain = TerrainAt(state, x, y);
                var source = TerrainSource(medieval, terrain, x, y, medievalLayout: true);
                var opacity = terrain == 1 ? waterPulse : .42f;
                _batch.Draw(medieval, new Rectangle(x * tile, y * tile, tile + 1, tile + 1), source, Color.White * opacity);
            }
        }
    }

    private void DrawScenery(GameState state)
    {
        for (var i = 0; i < _scenery.Count; i++)
        {
            var item = _scenery[i];
            switch (item.Kind)
            {
                case SceneryKind.Resource:
                    DrawResource(state, item.Node!);
                    break;
                case SceneryKind.Building:
                    DrawBuilding(state, item.Entity!);
                    break;
                case SceneryKind.Site:
                    DrawSite(state, item.Site!);
                    break;
            }
        }
    }

    private void DrawResource(GameState state, ResourceNodeState node)
    {
        var key = node.Type == "wood" ? ((Hash(node.Id, 17) & 1) == 0 ? "oak" : "pine") : node.Type;
        if (!_assets.TryGetEnvironmentSprite(key, out var sprite))
        {
            return;
        }
        var sway = node.Type == "wood" ? MathF.Sin((float)_time * .78f + (float)node.Wiggle) * .035f : 0f;
        var pulse = 1f + MathF.Sin((float)_time * 1.2f + node.Id * .71f) * .012f;
        var position = new Vector2((float)node.X, (float)node.Y);
        DrawSoftEllipse(position + new Vector2(4f, 6f), node.Type == "wood" ? 35f : 29f, node.Type == "wood" ? 18f : 14f, Color.Black * .72f);
        DrawAnchoredSprite(sprite, position, Color.White, sway, new Vector2(pulse, 2f - pulse), .78f);
        if (node.Type == "gold" && MathF.Sin((float)_time * 2.1f + node.Id) > .92f)
        {
            QueueEffect("embers", position - new Vector2(0f, 10f), 34f, .28f, node.Id * .13f);
        }
    }

    private void DrawBuilding(GameState state, EntityState building)
    {
        if (!_assets.TryGetBuildingSprite(building.Type, out var sprite))
        {
            return;
        }
        var position = new Vector2((float)building.X, (float)building.Y);
        var team = TeamColor(building.Faction);
        var progress = Math.Clamp((float)building.Construction, 0f, 1f);
        var busy = progress < 1f || building.Queue.Count > 0 || BuildingHasAgeProgress(state, building) || building.Type == "wonder";
        var pulse = busy ? 1f + MathF.Sin((float)_time * 2.3f + building.Id * .41f) * .012f : 1f;

        var radius = (float)Math.Max(28d, building.Radius);
        DrawSoftEllipse(position + new Vector2(5f, 7f), radius * 1.65f, radius * .82f, Color.Black * .8f);
        DrawEllipseRing(position + new Vector2(0f, radius * .22f), radius * 1.35f, radius * .55f, Color.Black * .82f);
        DrawEllipseRing(position + new Vector2(0f, radius * .22f), radius * 1.28f, radius * .49f, team * .75f);

        if (progress < 1f && _assets.TryGetEnvironmentSprite("construction", out var scaffold))
        {
            var scaffoldBob = MathF.Sin((float)_time * 3.1f + building.Id) * 1.2f;
            DrawAnchoredSprite(scaffold, position + new Vector2(0f, scaffoldBob), Color.White * .82f, 0f, Vector2.One, .78f);
        }

        var tint = progress < 1f ? Color.Lerp(new Color(84, 91, 91), Color.White, .3f + progress * .7f) * (.45f + progress * .55f) : Color.White;
        DrawAnchoredSprite(sprite, position, tint, 0f, new Vector2(pulse, 2f - pulse), .78f);

        if (busy)
        {
            var activity = .5f + MathF.Sin((float)_time * 4f + building.Id) * .25f;
            QueueEffect(progress < 1f ? "dust" : building.Type == "wonder" ? "healAura" : "embers", position - new Vector2(0f, sprite.DisplaySize.Y * .22f), radius * 1.15f, .12f + activity * .12f, (float)_time * .22f);
        }
        if (building.ActivityFlash > 0)
        {
            QueueEffect("siegeExplosion", position, radius * 1.7f, Math.Clamp((float)building.ActivityFlash, 0f, 1f) * .42f, building.Id);
        }
    }

    private void DrawSite(GameState state, SiteState site)
    {
        if (!_assets.TryGetEnvironmentSprite("site", out var sprite))
        {
            return;
        }
        var position = new Vector2((float)site.X, (float)site.Y);
        var owner = TeamColor(site.Owner);
        var pulse = 1f + MathF.Sin((float)_time * 1.8f + site.Id) * .018f;
        DrawSoftEllipse(position + new Vector2(4f, 7f), 75f, 39f, Color.Black * .78f);
        DrawEllipseRing(position + new Vector2(0f, 6f), 74f, 38f, Color.Black * .9f);
        DrawEllipseRing(position + new Vector2(0f, 6f), 69f, 34f, owner * .9f);
        DrawAnchoredSprite(sprite, position, Color.White, MathF.Sin((float)_time * .8f + site.Id) * .012f, new Vector2(pulse, 2f - pulse), .76f);
        if (site.Contested)
        {
            QueueEffect("swordSlash", position - new Vector2(0f, 24f), 70f, .38f, (float)_time * 1.8f);
        }
        else if (site.CaptureBy >= 0 && site.Progress < 6)
        {
            QueueEffect("selectionRing", position, 78f, .3f, (float)_time * .65f);
        }
    }

    private void DrawUnits(GameState state)
    {
        for (var i = 0; i < _units.Count; i++)
        {
            var item = _units[i];
            DrawUnit(state, item.Entity, item.Position);
        }
    }

    private void DrawUnit(GameState state, EntityState unit, Vector2 basePosition)
    {
        if (!_assets.TryGetUnitSprite(unit.Type, out var sprite))
        {
            return;
        }
        GameData.Units.TryGetValue(unit.Type, out var definition);
        var role = definition?.Role;
        var cavalry = role == "cavalry";
        var siege = role == "siege";
        var elephant = unit.Type == "warElephant";
        var moving = unit.Path.Count > 0 || DistanceSquared(unit.PrevX, unit.PrevY, unit.X, unit.Y) > .2;
        var action = unit.Order.Type;
        var phase = (float)_time * (moving ? 7.4f : 2.15f) + unit.Id * 1.731f;
        var slow = (float)_time * .73f + unit.Id * 2.117f;
        var work = !moving && (action == "gather" || action == "build") ? MathF.Sin((float)_time * 7.6f + unit.Id * .83f) : 0f;
        var attack = !moving && action == "attack" ? MathF.Sin((float)_time * 8.8f + unit.Id * .61f) : 0f;
        var active = MathF.Abs(work) > MathF.Abs(attack) ? work : attack;
        var bob = moving
            ? MathF.Abs(MathF.Sin(phase)) * 2.8f
            : MathF.Sin(phase) * 1.55f + MathF.Sin(slow) * .45f;
        var breath = 1f + (moving ? MathF.Sin(phase) * .018f : MathF.Sin(phase) * .052f);
        var sway = moving ? MathF.Sin(phase) * .045f : MathF.Sin(slow) * .075f;
        var facing = (float)unit.Angle;
        var lunge = active * 3.1f;
        var position = basePosition + new Vector2(MathF.Cos(facing), MathF.Sin(facing)) * lunge - new Vector2(0f, bob);
        var team = TeamColor(unit.Faction);
        var baseScale = elephant ? 1.18f : siege ? 1.08f : 1f;

        var groundWidth = elephant ? 48f : siege ? 43f : cavalry ? 36f : 29f;
        var groundHeight = elephant ? 25f : siege ? 23f : cavalry ? 20f : 16f;
        var groundPulse = 1f + MathF.Sin((float)_time * 2.4f + unit.Id) * .025f;
        DrawSoftEllipse(basePosition + new Vector2(3f, 5f), groundWidth * 1.15f, groundHeight * 1.12f, Color.Black * .93f);
        DrawEllipseRing(basePosition + new Vector2(0f, 4f), groundWidth * groundPulse, groundHeight / groundPulse, Color.Black * .96f);
        DrawEllipseRing(basePosition + new Vector2(0f, 4f), groundWidth * .88f * groundPulse, groundHeight * .82f / groundPulse, team * .94f);

        var rotation = facing - HalfPi + sway + active * (action == "gather" || action == "build" ? .13f : .09f);
        var scale = SpriteScale(sprite, new Vector2(baseScale * breath * (1f + MathF.Abs(active) * .13f), baseScale / breath * (1f - MathF.Abs(active) * .08f)));
        var origin = new Vector2(sprite.SourceRectangle.Width * .5f, sprite.SourceRectangle.Height * .5f);
        var outline = Math.Max(1.9f, 2.35f * _inverseZoom);
        DrawSpriteOutline(sprite, position, rotation, origin, scale, outline);
        _batch.Draw(sprite.Texture, position, sprite.SourceRectangle, Color.White, rotation, origin, scale, SpriteEffects.None, 0f);

        // A compact heraldic marker remains legible in dense formations.
        var badge = position + RotateLocal(new Vector2(-14f, 14f), rotation);
        DrawEllipseRing(badge, 14f * _inverseZoom, 14f * _inverseZoom, Color.Black * .95f);
        DrawSoftEllipse(badge, 10f * _inverseZoom, 10f * _inverseZoom, team);

        if (unit.Type == "villager" && TryGetActiveWork(unit, out var workTarget, out var material))
        {
            DrawVillagerTool(unit, basePosition, facing, workTarget, material);
        }

        if (attack != 0f && IsVisible(state, unit.X, unit.Y))
        {
            QueueEffect(definition?.IsRanged == true ? "arrowImpact" : "swordSlash", position + new Vector2(MathF.Cos(facing), MathF.Sin(facing)) * 18f, cavalry ? 50f : 42f, .16f + MathF.Abs(attack) * .14f, facing);
        }
        if (unit.Flash > 0 && IsVisible(state, unit.X, unit.Y))
        {
            QueueEffect("arrowImpact", position, 44f, Math.Clamp((float)unit.Flash * 2.5f, 0f, .5f), unit.Id);
        }
    }

    private void DrawVillagerTool(EntityState unit, Vector2 unitPosition, float facing, Vector2 target, WorkMaterial material)
    {
        var cycle = Fract((float)_time * 1.72f + unit.Id * .137f);
        var windup = cycle < .58f ? cycle / .58f : 1f - (cycle - .58f) / .42f;
        var swing = -.95f + Math.Clamp(windup, 0f, 1f) * 1.58f;
        var toolAngle = facing + swing;
        var hand = unitPosition + RotateLocal(new Vector2(11f, -2f), facing - HalfPi);
        var tip = hand + new Vector2(MathF.Cos(toolAngle), MathF.Sin(toolAngle)) * 25f;
        DrawLine(hand, tip, new Color(103, 70, 43), 3.2f * _inverseZoom);

        var perpendicular = new Vector2(-MathF.Sin(toolAngle), MathF.Cos(toolAngle));
        switch (material)
        {
            case WorkMaterial.Wood:
                DrawLine(tip - perpendicular * 2f, tip + perpendicular * 10f, new Color(210, 220, 220), 5f * _inverseZoom);
                break;
            case WorkMaterial.Gold:
                DrawLine(tip - perpendicular * 10f, tip + perpendicular * 10f, new Color(242, 210, 118), 3f * _inverseZoom);
                break;
            case WorkMaterial.Stone:
                DrawLine(tip - perpendicular * 10f, tip + perpendicular * 10f, new Color(207, 217, 218), 3f * _inverseZoom);
                break;
            case WorkMaterial.Food:
                DrawLine(tip, tip + perpendicular * 12f, new Color(228, 210, 148), 2.5f * _inverseZoom);
                DrawLine(tip + perpendicular * 12f, tip + perpendicular * 16f - new Vector2(MathF.Cos(toolAngle), MathF.Sin(toolAngle)) * 4f, new Color(182, 196, 112), 2f * _inverseZoom);
                break;
            default:
                DrawLine(tip - perpendicular * 8f, tip + perpendicular * 8f, new Color(197, 205, 204), 7f * _inverseZoom);
                break;
        }

        if (cycle >= .55f && cycle < .68f)
        {
            var color = material switch
            {
                WorkMaterial.Wood => new Color(183, 211, 137),
                WorkMaterial.Gold => new Color(246, 201, 88),
                WorkMaterial.Stone => new Color(194, 205, 208),
                WorkMaterial.Food => new Color(215, 145, 91),
                _ => new Color(239, 198, 112)
            };
            QueueEffect(material == WorkMaterial.Build ? "embers" : "arrowImpact", target, 36f, .38f, toolAngle, color);
        }
    }

    private void DrawProjectiles(GameState state)
    {
        for (var i = 0; i < _projectiles.Count; i++)
        {
            var projectile = _projectiles[i].Projectile;
            var position = new Vector2((float)projectile.X, (float)projectile.Y);
            var direction = new Vector2(1f, 0f);
            if (_entitiesById.TryGetValue(projectile.TargetId, out var target))
            {
                direction = new Vector2((float)(target.X - projectile.X), (float)(target.Y - projectile.Y));
                if (direction.LengthSquared() > .001f)
                {
                    direction.Normalize();
                }
            }
            var siege = projectile.Splash > 0 || projectile.Speed < 360;
            var color = siege ? new Color(207, 174, 125) : new Color(245, 218, 149);
            DrawLine(position - direction * (siege ? 12f : 8f), position + direction * 4f, color, (siege ? 4f : 2f) * _inverseZoom);
            DrawSoftEllipse(position, siege ? 7f : 4f, siege ? 7f : 4f, color);
        }
    }

    private void DrawFog(GameState state)
    {
        var tile = GameConstants.TileSize;
        var minX = Math.Clamp((int)MathF.Floor(_worldView.Left / tile) - 1, 0, GameConstants.MapWidth - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(_worldView.Right / tile) + 1, 0, GameConstants.MapWidth - 1);
        var minY = Math.Clamp((int)MathF.Floor(_worldView.Top / tile) - 1, 0, GameConstants.MapHeight - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(_worldView.Bottom / tile) + 1, 0, GameConstants.MapHeight - 1);
        var unseen = new Color(2, 5, 8) * .94f;
        var explored = new Color(5, 12, 17) * .52f;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var fog = FogAtCell(state, x, y);
                if (fog < 2)
                {
                    DrawWorldRect(new Rectangle(x * tile, y * tile, tile + 1, tile + 1), fog == 0 ? unseen : explored);
                }
            }
        }
    }

    private void DrawFogSafeOverlays(GameState state)
    {
        for (var i = 0; i < _scenery.Count; i++)
        {
            var item = _scenery[i];
            if (item.Kind == SceneryKind.Building)
            {
                var building = item.Entity!;
                if (building.Faction == _viewerFaction || IsVisible(state, building.X, building.Y))
                {
                    DrawBuildingOverlay(state, building);
                }
            }
            else if (item.Kind == SceneryKind.Site)
            {
                var site = item.Site!;
                if (IsVisible(state, site.X, site.Y))
                {
                    DrawSiteOverlay(site);
                }
            }
        }

        for (var i = 0; i < _units.Count; i++)
        {
            var item = _units[i];
            var unit = item.Entity;
            var friendly = unit.Faction == _viewerFaction;
            if (!friendly && !IsVisible(state, item.Position.X, item.Position.Y))
            {
                continue;
            }
            var selected = friendly && (unit.Selected || state.Selected.Contains(unit.Id));
            if (selected)
            {
                DrawSelection(item.Position, unit);
            }
            if (selected || unit.Hp < unit.MaxHp)
            {
                DrawHealthBar(item.Position, unit, unit.Type == "warElephant" ? 62f : 46f);
            }
        }
    }

    private void DrawBuildingOverlay(GameState state, EntityState building)
    {
        var position = new Vector2((float)building.X, (float)building.Y);
        var selected = building.Faction == _viewerFaction && (building.Selected || state.Selected.Contains(building.Id));
        if (selected)
        {
            DrawSelection(position, building);
        }

        var anchor = (float)(_assets.TryGetBuildingSprite(building.Type, out var sprite) ? sprite.DisplaySize.Y * .57f : building.Radius);
        if (selected || building.Hp < building.MaxHp)
        {
            DrawHealthBar(position, building, anchor + 9f * _inverseZoom);
        }

        var y = position.Y - anchor - 1f * _inverseZoom;
        if (building.Construction < 1)
        {
            DrawProgressBar(new Vector2(position.X, y), 64f * _inverseZoom, Math.Clamp((float)building.Construction, 0f, 1f), new Color(230, 185, 96));
            y -= 8f * _inverseZoom;
        }

        // Detailed production and strategic timers are friendly-only. Visible
        // enemies receive health/construction feedback but no queue intelligence.
        if (building.Faction != _viewerFaction)
        {
            return;
        }
        if (building.Queue.Count > 0)
        {
            var queued = building.Queue[0];
            var ratio = queued.Total > 0 ? 1f - (float)(queued.Remaining / queued.Total) : 0f;
            DrawProgressBar(new Vector2(position.X, y), 58f * _inverseZoom, ratio, new Color(105, 207, 222));
            y -= 8f * _inverseZoom;
        }
        if (building.Type == "town" && (uint)building.Faction < (uint)state.Players.Count)
        {
            var age = state.Players[building.Faction].AgeUp;
            if (age is not null)
            {
                var ratio = age.Total > 0 ? 1f - (float)(age.Remaining / age.Total) : 0f;
                DrawProgressBar(new Vector2(position.X, y), 62f * _inverseZoom, ratio, new Color(242, 197, 91));
                y -= 8f * _inverseZoom;
            }
        }
        if (building.Type == "wonder" && building.Construction >= 1)
        {
            DrawProgressBar(new Vector2(position.X, y), 68f * _inverseZoom, Math.Clamp((float)building.WonderTimer / 180f, 0f, 1f), new Color(228, 167, 75));
        }
    }

    private void DrawSiteOverlay(SiteState site)
    {
        var position = new Vector2((float)site.X, (float)site.Y);
        if (site.Contested)
        {
            var radius = 38f;
            var spin = (float)_time * 1.4f;
            var a = position + new Vector2(MathF.Cos(spin), MathF.Sin(spin)) * radius;
            var b = position - new Vector2(MathF.Cos(spin), MathF.Sin(spin)) * radius;
            DrawLine(a, b, new Color(242, 100, 84), 2f * _inverseZoom);
        }
        else if (site.CaptureBy >= 0 && site.Progress < 6)
        {
            DrawProgressBar(position - new Vector2(0f, 54f), 70f * _inverseZoom, Math.Clamp((float)site.Progress / 6f, 0f, 1f), TeamColor(site.CaptureBy));
        }
    }

    private void DrawSelection(Vector2 position, EntityState entity)
    {
        var building = entity.Kind == "building";
        var width = building ? (float)Math.Max(56d, entity.Radius * 2.15) : entity.Type == "warElephant" ? 58f : 42f;
        var height = building ? width * .48f : width * .5f;
        DrawEllipseRing(position + new Vector2(0f, building ? (float)entity.Radius * .18f : 4f), width, height, Color.Black);
        DrawEllipseRing(position + new Vector2(0f, building ? (float)entity.Radius * .18f : 4f), width - 4f * _inverseZoom, height - 3f * _inverseZoom, TeamColor(entity.Faction));
        QueueEffect("selectionRing", position, width * 1.35f, .32f + MathF.Sin((float)_time * 3f + entity.Id) * .07f, (float)_time * .12f);
    }

    private void DrawHealthBar(Vector2 position, EntityState entity, float verticalOffset)
    {
        var width = (entity.Kind == "building" ? 64f : 40f) * _inverseZoom;
        var height = (entity.Kind == "building" ? 6f : 5f) * _inverseZoom;
        var ratio = entity.MaxHp > 0 ? Math.Clamp((float)(entity.Hp / entity.MaxHp), 0f, 1f) : 0f;
        var barPosition = position - new Vector2(width * .5f, verticalOffset);
        DrawWorldRect(barPosition, new Vector2(width, height), new Color(2, 7, 10) * .94f);
        var fill = ratio > .5f ? new Color(102, 218, 139) : ratio > .25f ? new Color(237, 191, 87) : new Color(248, 91, 82);
        DrawWorldRect(barPosition + new Vector2(_inverseZoom, _inverseZoom), new Vector2(Math.Max(0f, (width - 2f * _inverseZoom) * ratio), Math.Max(_inverseZoom, height - 2f * _inverseZoom)), fill);
    }

    private void DrawProgressBar(Vector2 center, float width, float ratio, Color color)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        var height = 6f * _inverseZoom;
        var left = center - new Vector2(width * .5f, height * .5f);
        DrawWorldRect(left, new Vector2(width, height), new Color(3, 8, 11) * .92f);
        var inset = _inverseZoom;
        DrawWorldRect(left + new Vector2(inset, inset), new Vector2(Math.Max(0f, (width - inset * 2f) * ratio), Math.Max(inset, height - inset * 2f)), color);
    }

    private void DrawAdditiveEffects()
    {
        for (var i = 0; i < _effects.Count; i++)
        {
            var effect = _effects[i];
            var origin = new Vector2(effect.Sprite.SourceRectangle.Width * .5f, effect.Sprite.SourceRectangle.Height * .5f);
            var uniformScale = effect.Size / Math.Max(1f, effect.Sprite.DisplaySize.Y);
            var scale = SpriteScale(effect.Sprite, new Vector2(uniformScale, uniformScale));
            _batch.Draw(effect.Sprite.Texture, effect.Position, effect.Sprite.SourceRectangle, effect.Color * effect.Opacity, effect.Rotation, origin, scale, SpriteEffects.None, 0f);
        }
    }

    private void QueueEffect(string type, Vector2 position, float size, float opacity, float rotation, Color? color = null)
    {
        if (_effects.Count >= MaxQueuedEffects || !_assets.TryGetEffectSprite(type, out var sprite))
        {
            return;
        }
        _effects.Add(new EffectItem(sprite, position, Math.Max(1f, size), Math.Clamp(opacity, 0f, 1f), rotation, color ?? Color.White));
    }

    private bool TryGetActiveWork(EntityState unit, out Vector2 targetPosition, out WorkMaterial material)
    {
        targetPosition = default;
        material = WorkMaterial.None;
        if (unit.Order.TargetId is not int targetId || unit.Path.Count > 0)
        {
            return false;
        }
        if (unit.Order.Type == "gather")
        {
            if (_nodesById.TryGetValue(targetId, out var node))
            {
                var reach = node.Radius + unit.Radius + 8;
                if (DistanceSquared(unit.X, unit.Y, node.X, node.Y) > reach * reach)
                {
                    return false;
                }
                targetPosition = new Vector2((float)node.X, (float)node.Y);
                material = node.Type switch
                {
                    "wood" => WorkMaterial.Wood,
                    "gold" => WorkMaterial.Gold,
                    "stone" => WorkMaterial.Stone,
                    _ => WorkMaterial.Food
                };
                return true;
            }
            if (_entitiesById.TryGetValue(targetId, out var farm) && farm.Type == "farm")
            {
                var reach = farm.Radius + unit.Radius + 8;
                if (DistanceSquared(unit.X, unit.Y, farm.X, farm.Y) <= reach * reach)
                {
                    targetPosition = new Vector2((float)farm.X, (float)farm.Y);
                    material = WorkMaterial.Food;
                    return true;
                }
            }
        }
        else if (unit.Order.Type == "build" && _entitiesById.TryGetValue(targetId, out var building) && building.Construction < 1)
        {
            var reach = building.Radius + unit.Radius + 10;
            if (DistanceSquared(unit.X, unit.Y, building.X, building.Y) <= reach * reach)
            {
                targetPosition = new Vector2((float)building.X, (float)building.Y);
                material = WorkMaterial.Build;
                return true;
            }
        }
        return false;
    }

    private bool BuildingHasAgeProgress(GameState state, EntityState building) =>
        building.Type == "town" &&
        (uint)building.Faction < (uint)state.Players.Count &&
        state.Players[building.Faction].AgeUp is not null;

    private void DrawAnchoredSprite(SpriteAsset sprite, Vector2 position, Color color, float rotation, Vector2 animationScale, float anchorY)
    {
        var origin = new Vector2(sprite.SourceRectangle.Width * .5f, sprite.SourceRectangle.Height * anchorY);
        var scale = SpriteScale(sprite, animationScale);
        _batch.Draw(sprite.Texture, position, sprite.SourceRectangle, color, rotation, origin, scale, SpriteEffects.None, 0f);
    }

    private void DrawSpriteOutline(SpriteAsset sprite, Vector2 position, float rotation, Vector2 origin, Vector2 scale, float radius)
    {
        var tint = Color.Black * .96f;
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Tau / 8f;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            _batch.Draw(sprite.Texture, position + offset, sprite.SourceRectangle, tint, rotation, origin, scale, SpriteEffects.None, 0f);
        }
    }

    private static Vector2 SpriteScale(SpriteAsset sprite, Vector2 animationScale) =>
        new(
            sprite.DisplaySize.X / sprite.SourceRectangle.Width * animationScale.X,
            sprite.DisplaySize.Y / sprite.SourceRectangle.Height * animationScale.Y);

    private void DrawSoftEllipse(Vector2 center, float width, float height, Color tint)
    {
        var origin = new Vector2(_softEllipse.Width * .5f, _softEllipse.Height * .5f);
        _batch.Draw(_softEllipse, center, null, tint, 0f, origin, new Vector2(width / _softEllipse.Width, height / _softEllipse.Height), SpriteEffects.None, 0f);
    }

    private void DrawEllipseRing(Vector2 center, float width, float height, Color tint)
    {
        var origin = new Vector2(_ellipseRing.Width * .5f, _ellipseRing.Height * .5f);
        _batch.Draw(_ellipseRing, center, null, tint, 0f, origin, new Vector2(width / _ellipseRing.Width, height / _ellipseRing.Height), SpriteEffects.None, 0f);
    }

    private void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= .001f)
        {
            return;
        }
        _batch.Draw(_assets.WhitePixel, start, null, color, MathF.Atan2(delta.Y, delta.X), new Vector2(0f, .5f), new Vector2(length, Math.Max(.2f, thickness)), SpriteEffects.None, 0f);
    }

    private void DrawWorldRect(Rectangle rectangle, Color color) => _batch.Draw(_assets.WhitePixel, rectangle, color);

    private void DrawWorldRect(Vector2 position, Vector2 size, Color color)
    {
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }
        _batch.Draw(_assets.WhitePixel, position, null, color, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
    }

    private void DrawScreenRect(Rectangle rectangle, Color color) => _batch.Draw(_assets.WhitePixel, rectangle, color);

    private void DrawScreenRectangleOutline(Rectangle rectangle, Color color, int thickness)
    {
        DrawScreenRect(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, thickness), color);
        DrawScreenRect(new Rectangle(rectangle.Left, rectangle.Bottom - thickness, rectangle.Width, thickness), color);
        DrawScreenRect(new Rectangle(rectangle.Left, rectangle.Top, thickness, rectangle.Height), color);
        DrawScreenRect(new Rectangle(rectangle.Right - thickness, rectangle.Top, thickness, rectangle.Height), color);
    }

    private void DrawMinimapPoint(Rectangle destination, double worldX, double worldY, Color color, int size)
    {
        var x = destination.X + (int)(worldX / GameConstants.WorldWidth * destination.Width);
        var y = destination.Y + (int)(worldY / GameConstants.WorldHeight * destination.Height);
        DrawScreenRect(new Rectangle(x - size / 2, y - size / 2, size, size), Color.Black);
        if (size > 2)
        {
            DrawScreenRect(new Rectangle(x - size / 2 + 1, y - size / 2 + 1, Math.Max(1, size - 2), Math.Max(1, size - 2)), color);
        }
        else
        {
            DrawScreenRect(new Rectangle(x - size / 2, y - size / 2, size, size), color);
        }
    }

    private static Rectangle WorldRectToMinimap(Rectangle destination, float left, float top, float right, float bottom)
    {
        var x0 = destination.X + (int)(Math.Clamp(left, 0, GameConstants.WorldWidth) / GameConstants.WorldWidth * destination.Width);
        var y0 = destination.Y + (int)(Math.Clamp(top, 0, GameConstants.WorldHeight) / GameConstants.WorldHeight * destination.Height);
        var x1 = destination.X + (int)(Math.Clamp(right, 0, GameConstants.WorldWidth) / GameConstants.WorldWidth * destination.Width);
        var y1 = destination.Y + (int)(Math.Clamp(bottom, 0, GameConstants.WorldHeight) / GameConstants.WorldHeight * destination.Height);
        return new Rectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private Rectangle DeviceViewportRectangle()
    {
        var viewport = _graphicsDevice.Viewport;
        return new Rectangle(viewport.X, viewport.Y, viewport.Width, viewport.Height);
    }

    private static Rectangle TerrainSource(Texture2D texture, byte terrain, int tileX, int tileY, bool medievalLayout)
    {
        var quadrantWidth = texture.Width / 2;
        var quadrantHeight = texture.Height / 2;
        int quadrantX;
        int quadrantY;
        if (medievalLayout)
        {
            quadrantX = terrain is 1 or 3 ? quadrantWidth : 0;
            quadrantY = terrain is 2 or 3 ? quadrantHeight : 0;
        }
        else
        {
            quadrantX = terrain == 2 || terrain == 3 ? quadrantWidth : 0;
            quadrantY = terrain == 1 || terrain == 3 ? quadrantHeight : 0;
        }

        const int margin = 12;
        var sample = Math.Max(32, Math.Min(250, Math.Min(quadrantWidth - margin * 2, quadrantHeight - margin * 2)));
        var roomX = Math.Max(0, quadrantWidth - margin * 2 - sample);
        var roomY = Math.Max(0, quadrantHeight - margin * 2 - sample);
        var hashX = Hash01(tileX * 31 + terrain, tileY * 17 + (medievalLayout ? 5 : 0));
        var hashY = Hash01(tileX * 13 + 7, tileY * 29 + terrain);
        return new Rectangle(
            quadrantX + margin + (int)(hashX * roomX),
            quadrantY + margin + (int)(hashY * roomY),
            sample,
            sample);
    }

    private static Texture2D CreateEllipseTexture(GraphicsDevice graphicsDevice, bool ring)
    {
        const int width = 64;
        const int height = 32;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var ny = (y + .5f - height * .5f) / (height * .5f);
            for (var x = 0; x < width; x++)
            {
                var nx = (x + .5f - width * .5f) / (width * .5f);
                var distance = MathF.Sqrt(nx * nx + ny * ny);
                float alpha;
                if (ring)
                {
                    alpha = 1f - Math.Clamp(MathF.Abs(distance - .84f) / .13f, 0f, 1f);
                }
                else
                {
                    alpha = (1f - SmoothStep(.48f, 1f, distance)) * .72f;
                }
                var value = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
                // Premultiplied white permits the same reusable ellipse to be
                // tinted as a black shadow, team badge or projectile glow.
                pixels[y * width + x] = new Color(value, value, value, value);
            }
        }
        var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        texture.Name = ring ? "runtime-ellipse-ring" : "runtime-soft-ellipse";
        return texture;
    }

    private bool InView(float x, float y, float margin) =>
        x >= _worldView.Left - margin && x <= _worldView.Right + margin &&
        y >= _worldView.Top - margin && y <= _worldView.Bottom + margin;

    private static byte TerrainAt(GameState state, int x, int y) =>
        y >= 0 && y < state.Terrain.Length && state.Terrain[y] is { } row && x >= 0 && x < row.Length ? row[x] : (byte)0;

    private static byte FogAtCell(GameState state, int x, int y)
    {
        if (x < 0 || y < 0 || x >= GameConstants.MapWidth || y >= GameConstants.MapHeight)
        {
            return 0;
        }
        var index = y * GameConstants.MapWidth + x;
        return index < state.Fog.Count ? state.Fog[index] : (byte)2;
    }

    private static bool IsVisible(GameState state, double worldX, double worldY)
    {
        var x = Math.Clamp((int)(worldX / GameConstants.TileSize), 0, GameConstants.MapWidth - 1);
        var y = Math.Clamp((int)(worldY / GameConstants.TileSize), 0, GameConstants.MapHeight - 1);
        return FogAtCell(state, x, y) == 2;
    }

    private Color TeamColor(int faction) => (uint)faction < (uint)_teamColors.Length ? _teamColors[faction] : new Color(211, 181, 104);

    private static Color ParseHexColor(string? value, Color fallback)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return fallback;
        }
        return TryHexByte(value[1], value[2], out var r) &&
               TryHexByte(value[3], value[4], out var g) &&
               TryHexByte(value[5], value[6], out var b)
            ? new Color(r, g, b)
            : fallback;
    }

    private static bool TryHexByte(char high, char low, out byte value)
    {
        var a = HexNibble(high);
        var b = HexNibble(low);
        value = (byte)((a << 4) | b);
        return a >= 0 && b >= 0;
    }

    private static int HexNibble(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };

    private static Vector2 RotateLocal(Vector2 value, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new Vector2(value.X * cosine - value.Y * sine, value.X * sine + value.Y * cosine);
    }

    private static float Lerp(double from, double to, float amount) => (float)(from + (to - from) * amount);
    private static double DistanceSquared(double ax, double ay, double bx, double by) { var x = ax - bx; var y = ay - by; return x * x + y * y; }
    private static float Fract(float value) => value - MathF.Floor(value);
    private static float SmoothStep(float edge0, float edge1, float value) { var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f); return t * t * (3f - 2f * t); }
    private static int Hash(int x, int y) { unchecked { var h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (int)(h ^ (h >> 16)); } }
    private static float Hash01(int x, int y) => (Hash(x, y) & 0x00ffffff) / 16777216f;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum SceneryKind : byte { Resource, Building, Site }
    private enum WorkMaterial : byte { None, Wood, Food, Gold, Stone, Build }

    private readonly struct RectangleF
    {
        public RectangleF(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float Left => X;
        public float Right => X + Width;
        public float Top => Y;
        public float Bottom => Y + Height;
    }

    private readonly struct SceneryItem
    {
        private SceneryItem(SceneryKind kind, float y, int id, ResourceNodeState? node, EntityState? entity, SiteState? site)
        {
            Kind = kind; Y = y; Id = id; Node = node; Entity = entity; Site = site;
        }
        public SceneryKind Kind { get; }
        public float Y { get; }
        public int Id { get; }
        public ResourceNodeState? Node { get; }
        public EntityState? Entity { get; }
        public SiteState? Site { get; }
        public static SceneryItem ForNode(ResourceNodeState node) => new(SceneryKind.Resource, (float)node.Y, node.Id, node, null, null);
        public static SceneryItem ForBuilding(EntityState entity) => new(SceneryKind.Building, (float)entity.Y, entity.Id, null, entity, null);
        public static SceneryItem ForSite(SiteState site) => new(SceneryKind.Site, (float)site.Y, site.Id, null, null, site);
    }

    private readonly struct UnitItem
    {
        public UnitItem(EntityState entity, Vector2 position) { Entity = entity; Position = position; }
        public EntityState Entity { get; }
        public Vector2 Position { get; }
    }

    private readonly struct ProjectileItem
    {
        public ProjectileItem(ProjectileState projectile) { Projectile = projectile; }
        public ProjectileState Projectile { get; }
    }

    private readonly struct EffectItem
    {
        public EffectItem(SpriteAsset sprite, Vector2 position, float size, float opacity, float rotation, Color color)
        {
            Sprite = sprite; Position = position; Size = size; Opacity = opacity; Rotation = rotation; Color = color;
        }
        public SpriteAsset Sprite { get; }
        public Vector2 Position { get; }
        public float Size { get; }
        public float Opacity { get; }
        public float Rotation { get; }
        public Color Color { get; }
    }

    private sealed class SceneryComparer : IComparer<SceneryItem>
    {
        public int Compare(SceneryItem x, SceneryItem y)
        {
            var depth = x.Y.CompareTo(y.Y);
            return depth != 0 ? depth : x.Id.CompareTo(y.Id);
        }
    }

    private sealed class UnitComparer : IComparer<UnitItem>
    {
        public int Compare(UnitItem x, UnitItem y)
        {
            var depth = x.Position.Y.CompareTo(y.Position.Y);
            return depth != 0 ? depth : x.Entity.Id.CompareTo(y.Entity.Id);
        }
    }

    private sealed class ProjectileComparer : IComparer<ProjectileItem>
    {
        public int Compare(ProjectileItem x, ProjectileItem y)
        {
            var depth = x.Projectile.Y.CompareTo(y.Projectile.Y);
            return depth != 0 ? depth : x.Projectile.TargetId.CompareTo(y.Projectile.TargetId);
        }
    }
}
