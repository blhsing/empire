using Empire.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Diagnostics;

namespace Empire.Game.Input;

public enum ControllerSignal
{
    None,
    SelectionChanged,
    CommandIssued,
    BuildPlaced,
    TogglePause,
    ToggleFullscreen,
    Save,
    Export,
    Import,
    Power,
    Error
}

public readonly record struct ControllerEvent(ControllerSignal Signal, string Message = "");

/// <summary>
/// Native RTS camera, selection and order controller. Right-drag is the only
/// mouse-drag camera gesture; the middle mouse button is deliberately ignored.
/// </summary>
public sealed class GameController
{
    private const float DragThreshold = 6f;
    private readonly List<int> _pickCandidates = new(24);
    private readonly List<int> _selectedUnits = new(80);
    private readonly List<int> _builderIds = new(24);
    private readonly List<EntityState> _selectedEntities = new(80);
    private readonly List<int>[] _groups = [[], [], [], []];
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private Point _leftStart;
    private Point _rightStart;
    private Point _lastMouse;
    private bool _leftDragging;
    private bool _rightDragging;
    private bool _leftGestureInWorld;
    private bool _rightGestureInWorld;
    private bool _pointerActivated;
    private Point _lastPickPoint = new(-1000, -1000);
    private long _lastPickTimestamp;
    private int _pickCycle;

    public GameController(GameEngine engine, bool centerCamera = true)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
        _lastMouse = _previousMouse.Position;
        if (centerCamera)
        {
            CenterOnPlayerTown();
        }
    }

    public GameEngine Engine { get; private set; }
    public string? BuildMode { get; private set; }
    public bool AttackMoveMode { get; private set; }
    public bool MoveTargetMode { get; private set; }
    public bool RallyTargetMode { get; private set; }
    public bool IsSelecting => _leftDragging;
    public bool IsPanning => _rightDragging;
    public Point SelectionStart => _leftStart;
    public Point Pointer => _previousMouse.Position;
    public event EventHandler<ControllerEvent>? Signaled;

    /// <summary>
    /// Synchronizes held keys and buttons after a modal UI consumes input so
    /// the same press cannot leak into the next gameplay frame.
    /// </summary>
    public void SynchronizeInput()
    {
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
        _lastMouse = _previousMouse.Position;
        _leftDragging = false;
        _rightDragging = false;
        _leftGestureInWorld = false;
        _rightGestureInWorld = false;
    }

    public void Attach(GameEngine engine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        CancelMode();
        ClearSelection();
        foreach (var group in _groups)
        {
            group.Clear();
        }
        CenterOnPlayerTown();
    }

    public void BeginBuild(string buildingType)
    {
        if (!GameData.Buildings.ContainsKey(buildingType))
        {
            Signal(ControllerSignal.Error, "未知的建築類型。");
            return;
        }
        BuildMode = buildingType;
        AttackMoveMode = false;
    }

    public void BeginAttackMove()
    {
        if (!SelectedFriendlyUnits().Any(entity => GameData.Units[entity.Type].Role != "worker"))
        {
            Signal(ControllerSignal.Error, "請先選取軍事單位。");
            return;
        }
        AttackMoveMode = true;
        BuildMode = null;
        MoveTargetMode = false;
        RallyTargetMode = false;
    }

    public void BeginMoveTarget()
    {
        if (SelectedFriendlyUnits().Count == 0)
        {
            Signal(ControllerSignal.Error, "請先選取單位。");
            return;
        }
        CancelMode();
        MoveTargetMode = true;
    }

    public void BeginRallyTarget()
    {
        if (!Engine.State.Selected.Any(id => Engine.Entity(id) is { Dead: false, Faction: 0, Kind: "building" }))
        {
            Signal(ControllerSignal.Error, "請先選取生產建築。");
            return;
        }
        CancelMode();
        RallyTargetMode = true;
    }

    public void CancelMode()
    {
        BuildMode = null;
        AttackMoveMode = false;
        MoveTargetMode = false;
        RallyTargetMode = false;
    }

    public void Update(GameTime gameTime, Rectangle worldViewport, bool pointerCapturedByUi = false)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var elapsed = (float)Math.Min(gameTime.ElapsedGameTime.TotalSeconds, .1);
        var moved = mouse.Position != _lastMouse;
        if (moved)
        {
            _pointerActivated = true;
        }

        HandleHotkeys(keyboard);
        if (Engine.State.Paused || Engine.State.Ended)
        {
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            _lastMouse = mouse.Position;
            return;
        }
        UpdateCamera(keyboard, mouse, elapsed, worldViewport, pointerCapturedByUi);
        var pointerInWorld = !pointerCapturedByUi && worldViewport.Contains(mouse.Position);
        if (Pressed(mouse.LeftButton, _previousMouse.LeftButton))
        {
            _leftGestureInWorld = pointerInWorld;
        }
        if (Pressed(mouse.RightButton, _previousMouse.RightButton))
        {
            _rightGestureInWorld = pointerInWorld;
        }

        if (pointerInWorld)
        {
            HandlePointer(mouse, worldViewport);
        }
        else
        {
            if (Released(mouse.LeftButton, _previousMouse.LeftButton))
            {
                _leftDragging = false;
                _leftGestureInWorld = false;
            }
            if (Released(mouse.RightButton, _previousMouse.RightButton))
            {
                _rightDragging = false;
                _rightGestureInWorld = false;
            }
        }

        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        _lastMouse = mouse.Position;
    }

    public WorldPoint ScreenToWorld(Point screen, Rectangle viewport)
    {
        var camera = Engine.State.Camera;
        var x = camera.X + (screen.X - viewport.Center.X) / camera.Zoom;
        var y = camera.Y + (screen.Y - viewport.Center.Y) / camera.Zoom;
        return new WorldPoint(x, y);
    }

    public Vector2 WorldToScreen(double x, double y, Rectangle viewport)
    {
        var camera = Engine.State.Camera;
        return new Vector2(
            (float)(viewport.Center.X + (x - camera.X) * camera.Zoom),
            (float)(viewport.Center.Y + (y - camera.Y) * camera.Zoom));
    }

    public Rectangle CurrentSelectionRectangle(Point pointer)
    {
        var left = Math.Min(_leftStart.X, pointer.X);
        var top = Math.Min(_leftStart.Y, pointer.Y);
        return new Rectangle(left, top, Math.Abs(pointer.X - _leftStart.X), Math.Abs(pointer.Y - _leftStart.Y));
    }

    private void HandleHotkeys(KeyboardState keyboard)
    {
        if (Pressed(Keys.Escape, keyboard))
        {
            if (BuildMode is not null || AttackMoveMode || MoveTargetMode || RallyTargetMode)
            {
                CancelMode();
            }
            else
            {
                Signal(ControllerSignal.TogglePause);
            }
        }
        if (Pressed(Keys.Space, keyboard)) Signal(ControllerSignal.TogglePause);
        if (Pressed(Keys.F11, keyboard)) Signal(ControllerSignal.ToggleFullscreen);
        if (Pressed(Keys.F5, keyboard)) Signal(ControllerSignal.Save);
        if (Pressed(Keys.F6, keyboard)) Signal(ControllerSignal.Export);
        if (Pressed(Keys.F7, keyboard)) Signal(ControllerSignal.Import);
        // Keep WASD exclusively available for camera movement. Tactical commands
        // use non-conflicting keys so holding A/S never changes the unit order.
        if (Pressed(Keys.R, keyboard)) BeginAttackMove();
        if (Pressed(Keys.X, keyboard))
        {
            StopSelected();
        }
        if (Pressed(Keys.F, keyboard))
        {
            if (Engine.UsePower(0))
            {
                FlagTutorial("power");
                Signal(ControllerSignal.Power, "文明軍令已發動！");
            }
            else Signal(ControllerSignal.Error, "軍令尚未就緒，或需先進入封建時代。");
        }
        if (Pressed(Keys.H, keyboard)) CenterOnPlayerTown();

        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        for (var index = 0; index < 4; index++)
        {
            var key = (Keys)((int)Keys.D1 + index);
            if (!Pressed(key, keyboard))
            {
                continue;
            }
            if (control)
            {
                _groups[index].Clear();
                _groups[index].AddRange(Engine.State.Selected.Where(id => Engine.Entity(id) is { Dead: false, Faction: 0 }));
                FlagTutorial("group");
                Signal(ControllerSignal.CommandIssued, $"已建立編隊 {index + 1}。");
            }
            else
            {
                SelectIds(_groups[index]);
                var first = _groups[index].Select(Engine.Entity).FirstOrDefault(entity => entity is { Dead: false });
                if (first is not null) CenterCamera(first.X, first.Y);
            }
        }
    }

    private void UpdateCamera(KeyboardState keyboard, MouseState mouse, float elapsed, Rectangle viewport, bool pointerCapturedByUi)
    {
        var camera = Engine.State.Camera;
        var x = 0f;
        var y = 0f;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up)) y--;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)) y++;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left)) x--;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) x++;

        if (!pointerCapturedByUi && _pointerActivated && !_rightDragging && viewport.Contains(mouse.Position))
        {
            const int edge = 4;
            if (mouse.X <= viewport.Left + edge) x--;
            else if (mouse.X >= viewport.Right - edge) x++;
            if (mouse.Y <= viewport.Top + edge) y--;
            else if (mouse.Y >= viewport.Bottom - edge) y++;
        }
        if (x != 0 || y != 0)
        {
            var length = MathF.Sqrt(x * x + y * y);
            camera.X += x / length * 530 * elapsed / camera.Zoom;
            camera.Y += y / length * 530 * elapsed / camera.Zoom;
            FlagTutorial("camera");
        }

        var wheelDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (!pointerCapturedByUi && wheelDelta != 0 && viewport.Contains(mouse.Position))
        {
            var before = ScreenToWorld(mouse.Position, viewport);
            camera.Zoom = Math.Clamp(camera.Zoom * Math.Pow(1.1, wheelDelta / 120d), .62, 1.65);
            var after = ScreenToWorld(mouse.Position, viewport);
            camera.X += before.X - after.X;
            camera.Y += before.Y - after.Y;
            FlagTutorial("camera");
        }
        ClampCamera(viewport);
    }

    private void HandlePointer(MouseState mouse, Rectangle viewport)
    {
        if (Pressed(mouse.RightButton, _previousMouse.RightButton))
        {
            _rightStart = mouse.Position;
            _rightDragging = false;
        }
        if (_rightGestureInWorld && mouse.RightButton == ButtonState.Pressed)
        {
            var deltaFromStart = mouse.Position - _rightStart;
            if (!_rightDragging && deltaFromStart.ToVector2().LengthSquared() >= DragThreshold * DragThreshold)
            {
                _rightDragging = true;
                CancelMode();
            }
            if (_rightDragging)
            {
                var delta = mouse.Position - _previousMouse.Position;
                Engine.State.Camera.X -= delta.X / Engine.State.Camera.Zoom;
                Engine.State.Camera.Y -= delta.Y / Engine.State.Camera.Zoom;
                ClampCamera(viewport);
                FlagTutorial("camera");
            }
        }
        if (_rightGestureInWorld && Released(mouse.RightButton, _previousMouse.RightButton))
        {
            if (!_rightDragging)
            {
                if (BuildMode is not null || AttackMoveMode || MoveTargetMode || RallyTargetMode)
                {
                    CancelMode();
                }
                else
                {
                    IssueContext(ScreenToWorld(mouse.Position, viewport));
                }
            }
            _rightDragging = false;
            _rightGestureInWorld = false;
        }

        if (Pressed(mouse.LeftButton, _previousMouse.LeftButton))
        {
            _leftStart = mouse.Position;
            _leftDragging = false;
        }
        if (_leftGestureInWorld && mouse.LeftButton == ButtonState.Pressed && !_rightDragging)
        {
            var delta = mouse.Position - _leftStart;
            if (delta.ToVector2().LengthSquared() >= DragThreshold * DragThreshold)
            {
                _leftDragging = true;
            }
        }
        if (_leftGestureInWorld && Released(mouse.LeftButton, _previousMouse.LeftButton))
        {
            if (BuildMode is not null)
            {
                PlaceBuilding(ScreenToWorld(mouse.Position, viewport));
            }
            else if (AttackMoveMode)
            {
                IssueAttackMove(ScreenToWorld(mouse.Position, viewport));
            }
            else if (MoveTargetMode)
            {
                IssueMove(ScreenToWorld(mouse.Position, viewport));
            }
            else if (RallyTargetMode)
            {
                IssueRally(ScreenToWorld(mouse.Position, viewport));
            }
            else if (_leftDragging)
            {
                SelectBox(CurrentSelectionRectangle(mouse.Position), viewport);
            }
            else
            {
                SelectPoint(mouse.Position, viewport);
            }
            _leftDragging = false;
            _leftGestureInWorld = false;
        }
    }

    private void PlaceBuilding(WorldPoint point)
    {
        _builderIds.Clear();
        foreach (var entity in SelectedFriendlyUnits())
        {
            if (entity.Type == "villager") _builderIds.Add(entity.Id);
        }
        if (_builderIds.Count == 0)
        {
            Signal(ControllerSignal.Error, "請先選取至少一名村民。");
            return;
        }
        var building = Engine.StartBuilding(BuildMode!, 0, point.X, point.Y, _builderIds);
        if (building is null)
        {
            Signal(ControllerSignal.Error, "無法在此處興建：請檢查地形、前置建築與資源。");
            return;
        }
        BuildMode = null;
        FlagTutorial("built");
        Signal(ControllerSignal.BuildPlaced, $"開始興建{GameData.Buildings[building.Type].Name}。");
    }

    private void IssueAttackMove(WorldPoint point)
    {
        var issued = false;
        foreach (var unit in SelectedFriendlyUnits())
        {
            if (GameData.Units[unit.Type].Role == "worker") continue;
            issued |= Engine.SetMove(unit.Id, point.X, point.Y, attackMove: true);
        }
        AttackMoveMode = false;
        if (issued)
        {
            FlagTutorial("attackMove");
            Signal(ControllerSignal.CommandIssued, "部隊開始進軍。");
        }
    }

    public void StopSelected()
    {
        foreach (var unit in SelectedFriendlyUnits()) Engine.Stop(unit.Id);
        CancelMode();
        Signal(ControllerSignal.CommandIssued, "已停止目前命令。");
    }

    private void IssueMove(WorldPoint point)
    {
        var units = SelectedFriendlyUnits();
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(units.Count)));
        var issued = false;
        for (var index = 0; index < units.Count; index++)
        {
            var offsetX = (index % columns - (columns - 1) * .5) * 28;
            var offsetY = (index / columns - (columns - 1) * .5) * 28;
            issued |= Engine.SetMove(units[index].Id, point.X + offsetX, point.Y + offsetY);
        }
        MoveTargetMode = false;
        if (issued)
        {
            FlagTutorial("order");
            Signal(ControllerSignal.CommandIssued, "移動命令已下達。");
        }
    }

    private void IssueRally(WorldPoint point)
    {
        var issued = false;
        foreach (var id in Engine.State.Selected)
        {
            if (Engine.Entity(id) is { Dead: false, Faction: 0, Kind: "building" } building)
            {
                issued |= Engine.SetRallyPoint(building.Id, point.X, point.Y);
            }
        }
        RallyTargetMode = false;
        if (issued)
        {
            FlagTutorial("rally");
            Signal(ControllerSignal.CommandIssued, "集合點已更新。");
        }
    }

    private void IssueContext(WorldPoint point)
    {
        var targetEntity = PickEntity(point, includeResources: false);
        var targetNode = PickNode(point);
        var units = SelectedFriendlyUnits();
        if (units.Count == 0)
        {
            var building = Engine.State.Selected.Select(Engine.Entity).FirstOrDefault(entity => entity is { Kind: "building", Faction: 0, Dead: false });
            if (building is not null && Engine.SetRallyPoint(building.Id, point.X, point.Y))
            {
                Engine.TutorialEvent("rally");
                Signal(ControllerSignal.CommandIssued, "集合點已更新。");
            }
            return;
        }

        var issued = false;
        if (targetEntity is { Faction: not 0 })
        {
            foreach (var unit in units) issued |= Engine.SetAttack(unit.Id, targetEntity.Id);
        }
        else if (targetEntity is { Faction: 0, Kind: "building", Construction: < 1 })
        {
            foreach (var unit in units.Where(entity => entity.Type == "villager")) issued |= Engine.SetBuild(unit.Id, targetEntity.Id);
        }
        else if (targetNode is not null)
        {
            foreach (var unit in units.Where(entity => entity.Type == "villager")) issued |= Engine.SetGather(unit.Id, targetNode.Id);
        }
        else if (targetEntity is { Faction: 0, Type: "farm" })
        {
            foreach (var unit in units.Where(entity => entity.Type == "villager")) issued |= Engine.SetGather(unit.Id, targetEntity.Id);
        }
        else
        {
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(units.Count)));
            for (var index = 0; index < units.Count; index++)
            {
                var offsetX = (index % columns - (columns - 1) * .5) * 28;
                var offsetY = (index / columns - (columns - 1) * .5) * 28;
                issued |= Engine.SetMove(units[index].Id, point.X + offsetX, point.Y + offsetY);
            }
        }
        if (issued)
        {
            FlagTutorial("order");
            Signal(ControllerSignal.CommandIssued, "命令已下達。");
        }
    }

    private void SelectPoint(Point screen, Rectangle viewport)
    {
        var world = ScreenToWorld(screen, viewport);
        _pickCandidates.Clear();
        var minimum = 20 / Engine.State.Camera.Zoom;
        foreach (var entity in Engine.State.Entities)
        {
            if (entity.Dead || !VisibleAt(entity.X, entity.Y)) continue;
            var dx = entity.X - world.X;
            var dy = entity.Y - world.Y;
            var radius = Math.Max(entity.Radius, minimum);
            if (dx * dx + dy * dy <= radius * radius)
            {
                _pickCandidates.Add(entity.Id);
            }
        }
        _pickCandidates.Sort(ComparePickCandidates);

        if (_pickCandidates.Count == 0)
        {
            if (!ShiftHeld()) ClearSelection();
            return;
        }
        var clickDistance = Vector2.DistanceSquared(screen.ToVector2(), _lastPickPoint.ToVector2());
        var now = Stopwatch.GetTimestamp();
        var clickElapsed = _lastPickTimestamp == 0
            ? double.PositiveInfinity
            : Stopwatch.GetElapsedTime(_lastPickTimestamp, now).TotalSeconds;
        var repeated = clickDistance <= 100 && clickElapsed <= 1.1;
        var doubleClick = _pickCandidates.Count == 1 && clickDistance <= 100 && clickElapsed <= .42;
        _pickCycle = repeated ? (_pickCycle + 1) % _pickCandidates.Count : 0;
        _lastPickPoint = screen;
        _lastPickTimestamp = now;
        var chosen = Engine.Entity(_pickCandidates[_pickCycle])!;
        if (doubleClick && chosen is { Kind: "unit", Faction: 0 })
        {
            ClearSelection();
            foreach (var entity in Engine.State.Entities)
            {
                if (entity is { Dead: false, Kind: "unit", Faction: 0 } && entity.Type == chosen.Type &&
                    viewport.Contains(WorldToScreen(entity.X, entity.Y, viewport).ToPoint()))
                {
                    SetSelected(entity, true);
                }
            }
            FlagTutorial("selected");
            Signal(ControllerSignal.SelectionChanged);
        }
        else if (ShiftHeld())
        {
            SetSelected(chosen, !chosen.Selected);
            FlagTutorial("selected");
            Signal(ControllerSignal.SelectionChanged);
        }
        else
        {
            SelectIds([chosen.Id]);
        }
    }

    private void SelectBox(Rectangle screenBounds, Rectangle viewport)
    {
        if (!ShiftHeld()) ClearSelection();
        foreach (var entity in Engine.State.Entities)
        {
            if (entity.Dead || entity.Kind != "unit" || entity.Faction != 0) continue;
            var screen = WorldToScreen(entity.X, entity.Y, viewport).ToPoint();
            if (screenBounds.Contains(screen)) SetSelected(entity, true);
        }
        FlagTutorial("selected");
        Signal(ControllerSignal.SelectionChanged);
    }

    private EntityState? PickEntity(WorldPoint point, bool includeResources)
    {
        _ = includeResources;
        EntityState? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var entity in Engine.State.Entities)
        {
            if (entity.Dead || !VisibleAt(entity.X, entity.Y)) continue;
            var distance = DistanceSquared(point.X, point.Y, entity.X, entity.Y);
            var radius = Math.Max(entity.Radius, 18 / Engine.State.Camera.Zoom);
            if (distance <= radius * radius && distance < bestDistance)
            {
                best = entity;
                bestDistance = distance;
            }
        }
        return best;
    }

    private ResourceNodeState? PickNode(WorldPoint point)
    {
        ResourceNodeState? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var node in Engine.State.Nodes)
        {
            if (node.Dead || node.Amount <= 0 || !VisibleAt(node.X, node.Y)) continue;
            var distance = DistanceSquared(point.X, point.Y, node.X, node.Y);
            var radius = Math.Max(node.Radius, 16 / Engine.State.Camera.Zoom);
            if (distance <= radius * radius && distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }
        return best;
    }

    private IReadOnlyList<EntityState> SelectedFriendlyUnits()
    {
        _selectedUnits.Clear();
        _selectedUnits.AddRange(Engine.State.Selected);
        _selectedEntities.Clear();
        foreach (var id in _selectedUnits)
        {
            if (Engine.Entity(id) is { Dead: false, Faction: 0, Kind: "unit" } unit) _selectedEntities.Add(unit);
        }
        return _selectedEntities;
    }

    private void SelectIds(IEnumerable<int> ids)
    {
        ClearSelection();
        foreach (var id in ids)
        {
            if (Engine.Entity(id) is { Dead: false } entity) SetSelected(entity, true);
        }
        FlagTutorial("selected");
        Signal(ControllerSignal.SelectionChanged);
    }

    private void ClearSelection()
    {
        foreach (var id in Engine.State.Selected)
        {
            if (Engine.Entity(id) is { } entity) entity.Selected = false;
        }
        Engine.State.Selected.Clear();
    }

    private void SetSelected(EntityState entity, bool selected)
    {
        entity.Selected = selected;
        if (selected) Engine.State.Selected.Add(entity.Id);
        else Engine.State.Selected.Remove(entity.Id);
    }

    private bool VisibleAt(double x, double y)
    {
        var cellX = Math.Clamp((int)(x / GameConstants.TileSize), 0, GameConstants.MapWidth - 1);
        var cellY = Math.Clamp((int)(y / GameConstants.TileSize), 0, GameConstants.MapHeight - 1);
        var index = cellY * GameConstants.MapWidth + cellX;
        return Engine.State.RevealUntil > Engine.State.Time || index < Engine.State.Fog.Count && Engine.State.Fog[index] == 2;
    }

    private void CenterOnPlayerTown()
    {
        var town = Engine.State.Entities.FirstOrDefault(entity => !entity.Dead && entity.Faction == 0 && entity.Type == "town");
        if (town is not null) CenterCamera(town.X, town.Y);
        else if (Engine.State.Spawn.Count > 0) CenterCamera(Engine.State.Spawn[0].X, Engine.State.Spawn[0].Y);
    }

    private void CenterCamera(double x, double y)
    {
        Engine.State.Camera.X = x;
        Engine.State.Camera.Y = y;
    }

    private void ClampCamera(Rectangle viewport)
    {
        var camera = Engine.State.Camera;
        camera.Zoom = Math.Clamp(camera.Zoom, .62, 1.65);
        var halfWidth = viewport.Width / (2d * camera.Zoom);
        var halfHeight = viewport.Height / (2d * camera.Zoom);
        camera.X = halfWidth * 2 >= GameConstants.WorldWidth
            ? GameConstants.WorldWidth * .5
            : Math.Clamp(camera.X, halfWidth, GameConstants.WorldWidth - halfWidth);
        camera.Y = halfHeight * 2 >= GameConstants.WorldHeight
            ? GameConstants.WorldHeight * .5
            : Math.Clamp(camera.Y, halfHeight, GameConstants.WorldHeight - halfHeight);
    }

    private void FlagTutorial(string flag)
    {
        Engine.TutorialEvent(flag);
    }

    private bool Pressed(Keys key, KeyboardState current) => current.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
    private static bool ShiftHeld()
    {
        var keyboard = Keyboard.GetState();
        return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
    }
    private static bool Pressed(ButtonState current, ButtonState previous) => current == ButtonState.Pressed && previous == ButtonState.Released;
    private static bool Released(ButtonState current, ButtonState previous) => current == ButtonState.Released && previous == ButtonState.Pressed;
    private static double DistanceSquared(double x1, double y1, double x2, double y2) => (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);
    private void Signal(ControllerSignal signal, string message = "") => Signaled?.Invoke(this, new ControllerEvent(signal, message));

    private int ComparePickCandidates(int left, int right)
    {
        var a = Engine.Entity(left)!;
        var b = Engine.Entity(right)!;
        var unitOrder = (a.Kind == "unit" ? 0 : 1).CompareTo(b.Kind == "unit" ? 0 : 1);
        if (unitOrder != 0) return unitOrder;
        var factionOrder = (a.Faction == 0 ? 0 : 1).CompareTo(b.Faction == 0 ? 0 : 1);
        return factionOrder != 0 ? factionOrder : b.Y.CompareTo(a.Y);
    }
}
