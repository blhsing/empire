using Empire.Core;
using Empire.Game.Graphics;
using Empire.Game.Input;
using Empire.Game.Platform;
using Empire.Game.Rendering;
using Empire.Game.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Empire.Game;

public sealed class EmpireGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string[] _launchArguments;
    private readonly bool _smokeMode;
    private readonly GamePreferences _preferences;
    private readonly GameSaveService _saveService = new();
    private SpriteBatch? _spriteBatch;
    private GameAssets? _assets;
    private TraditionalChineseFontService? _fonts;
    private ProceduralAudioService? _audio;
    private WorldRenderer? _worldRenderer;
    private UiToolkit? _ui;
    private GameUiRenderer? _gameUi;
    private GameEngine? _engine;
    private GameController? _controller;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private ScreenMode _screen = ScreenMode.MainMenu;
    private double _simulationAccumulator;
    private float _interpolationAlpha = 1;
    private float _animationTime;
    private bool _pauseMenuOpen;
    private bool _guideOpen;
    private bool _tutorialCollapsed;
    private int _commandPage;
    private string? _notice;
    private float _noticeRemaining;
    private float _smokeElapsed;

    public EmpireGame(string[]? launchArguments = null)
    {
        _launchArguments = launchArguments ?? [];
        _smokeMode = _launchArguments.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        _preferences = GamePreferencesStore.Load();
        NormalizePreferences();

        _graphics = new GraphicsDeviceManager(this)
        {
            // Native smoke deliberately exercises the minimum supported layout.
            PreferredBackBufferWidth = _smokeMode ? 960 : _preferences.WindowWidth,
            PreferredBackBufferHeight = _smokeMode ? 600 : _preferences.WindowHeight,
            SynchronizeWithVerticalRetrace = true,
            HardwareModeSwitch = false,
            IsFullScreen = _preferences.Fullscreen
        };
        Window.Title = "帝國餘燼：百族爭霸 — 原生版";
        Window.AllowUserResizing = true;
        Window.FileDrop += OnFileDrop;
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Content.RootDirectory = "Content";
        InactiveSleepTime = TimeSpan.FromMilliseconds(20);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _assets = new GameAssets(GraphicsDevice);
        _assets.Load();
        _fonts = new TraditionalChineseFontService();
        _ui = new UiToolkit(_spriteBatch, _assets.WhitePixel, _fonts);
        _gameUi = new GameUiRenderer(_spriteBatch, _ui, _assets.MenuBackground, _assets.EmpireIcon);
        _worldRenderer = new WorldRenderer(GraphicsDevice, _assets, _spriteBatch);

        if (ProceduralAudioService.TryCreate(out var audio, out _) && audio is not null)
        {
            _audio = audio;
            audio.MasterVolume = _preferences.Volume;
            audio.IsMuted = _preferences.Muted;
            audio.StartAdaptiveAudio();
        }

        _previousMouse = Mouse.GetState();
        _previousKeyboard = Keyboard.GetState();
        if (!_smokeMode) TryOpenLaunchSave();
    }

    protected override void Update(GameTime gameTime)
    {
        var elapsed = (float)Math.Min(gameTime.ElapsedGameTime.TotalSeconds, .25);
        if (_smokeMode)
        {
            _smokeElapsed += elapsed;
            if (_smokeElapsed >= .35f && _engine is null)
            {
                StartNewGame(tutorial: false);
            }
            if (_smokeElapsed >= 2.6f)
            {
                Exit();
            }
        }
        _animationTime += elapsed;
        if (_noticeRemaining > 0)
        {
            _noticeRemaining = Math.Max(0, _noticeRemaining - elapsed);
            if (_noticeRemaining <= 0) _notice = null;
        }

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        var leftReleased = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;

        if (_screen == ScreenMode.MainMenu)
        {
            if (Pressed(Keys.F11, keyboard)) ToggleFullscreen();
            if (Pressed(Keys.Escape, keyboard)) Exit();
            if (leftReleased && _gameUi?.TryGetAction(mouse.Position, out var action) == true)
            {
                HandleUiAction(action, mouse.Position);
            }
            _audio?.Update(elapsed, .08f, 0);
        }
        else if (_engine is not null && _controller is not null)
        {
            if (leftReleased && _gameUi?.TryGetAction(mouse.Position, out var action) == true)
            {
                HandleUiAction(action, mouse.Position);
            }

            // An overlay action can detach the active match (for example,
            // 「返回主選單」). Do not continue through the gameplay branch with
            // references that the action intentionally cleared.
            if (_screen != ScreenMode.Playing || _engine is null || _controller is null)
            {
                _previousMouse = mouse;
                _previousKeyboard = keyboard;
                base.Update(gameTime);
                return;
            }

            var modalOpen = _pauseMenuOpen || _guideOpen || _engine.State.Ended;
            if (modalOpen)
            {
                if (Pressed(Keys.F11, keyboard)) ToggleFullscreen();
                if (Pressed(Keys.Escape, keyboard) || Pressed(Keys.Space, keyboard))
                {
                    if (_guideOpen) CloseGuide();
                    else if (!_engine.State.Ended) ResumeGame();
                }
                if (Pressed(Keys.F5, keyboard)) SaveGame(showNotice: true);
                if (Pressed(Keys.F6, keyboard)) ExportGame();
            }
            else
            {
                var worldViewport = GetWorldViewport(GraphicsDevice.Viewport.Bounds);
                var uiCaptured = _gameUi?.TryHit(mouse.Position, out _) == true;
                _controller.Update(gameTime, worldViewport, uiCaptured);
            }

            AdvanceSimulation(elapsed);
            var activity = Math.Clamp(_engine.State.Entities.Count(entity => !entity.Dead && entity.Order.Type != "idle") / 24f, 0, 1);
            _audio?.Update(elapsed, activity, (float)Math.Clamp(_engine.State.Combat, 0, 1));
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(7, 13, 17));
        var viewport = GraphicsDevice.Viewport.Bounds;
        var pointer = Mouse.GetState().Position;

        if (_screen == ScreenMode.MainMenu || _engine is null || _controller is null)
        {
            _spriteBatch!.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            _gameUi!.DrawMainMenu(
                viewport,
                pointer,
                new MainMenuUiState(_preferences.Civilization, _preferences.Difficulty, _preferences.PlayerCount, _saveService.HasAutosave),
                _animationTime);
            _spriteBatch.End();
        }
        else
        {
            var worldViewport = GetWorldViewport(viewport);
            _worldRenderer!.DrawWorld(_engine.State, worldViewport, _interpolationAlpha);

            _spriteBatch!.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            DrawWorldInputOverlay(worldViewport, pointer);
            _gameUi!.DrawGameplay(
                viewport,
                pointer,
                _engine.State,
                new GameplayUiState(
                    _graphics.IsFullScreen,
                    _preferences.Muted,
                    _pauseMenuOpen,
                    _guideOpen,
                    _saveService.HasAutosave,
                    _commandPage,
                    _tutorialCollapsed,
                    _notice,
                    _noticeRemaining),
                _animationTime);
            _spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (!_smokeMode && _engine is { State.Ended: false })
        {
            TrySaveAutosave(_engine);
        }
        if (!_smokeMode) SavePreferences();
        base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Window.FileDrop -= OnFileDrop;
            DetachEngine();
            _worldRenderer?.Dispose();
            _audio?.Dispose();
            _fonts?.Dispose();
            _assets?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AdvanceSimulation(float elapsed)
    {
        if (_engine is null || _engine.State.Paused || _engine.State.Ended)
        {
            _interpolationAlpha = 1;
            return;
        }

        _simulationAccumulator += Math.Min(elapsed, .25f) * Math.Clamp(_engine.State.Speed, .25, 4);
        var updates = 0;
        while (_simulationAccumulator >= GameConstants.FixedStep && updates < 15 && !_engine.State.Ended)
        {
            _engine.Step();
            _simulationAccumulator -= GameConstants.FixedStep;
            updates++;
        }
        _interpolationAlpha = (float)Math.Clamp(_simulationAccumulator / GameConstants.FixedStep, 0, 1);
    }

    private void HandleUiAction(UiAction action, Point pointer)
    {
        switch (action.Id)
        {
            case UiActionId.SelectCivilization when action.Key is not null && GameData.Civilizations.ContainsKey(action.Key):
                _preferences.Civilization = action.Key;
                Play(AudioCue.Select);
                SavePreferences();
                break;
            case UiActionId.SelectDifficulty when action.Key is not null && GameData.Difficulties.ContainsKey(action.Key):
                _preferences.Difficulty = action.Key;
                Play(AudioCue.Click);
                SavePreferences();
                break;
            case UiActionId.SelectPlayerCount:
                _preferences.PlayerCount = Math.Clamp(action.Value, 2, 4);
                Play(AudioCue.Click);
                SavePreferences();
                break;
            case UiActionId.StartGame:
                StartNewGame(tutorial: false);
                break;
            case UiActionId.StartTutorial:
                StartNewGame(tutorial: true);
                break;
            case UiActionId.ContinueGame:
            case UiActionId.LoadGame:
                LoadAutosave();
                break;
            case UiActionId.ImportSave:
                ImportNewestOrPrompt();
                break;
            case UiActionId.TogglePauseMenu:
                if (_pauseMenuOpen) ResumeGame(); else PauseGame();
                break;
            case UiActionId.ResumeGame:
                ResumeGame();
                break;
            case UiActionId.ToggleFullscreen:
                ToggleFullscreen();
                break;
            case UiActionId.ToggleMute:
                _preferences.Muted = !_preferences.Muted;
                if (_audio is not null) _audio.IsMuted = _preferences.Muted;
                SavePreferences();
                Notify(_preferences.Muted ? "音效已關閉。" : "音效已開啟。");
                break;
            case UiActionId.CycleGameSpeed when _engine is not null:
                _engine.State.Speed = _engine.State.Speed == 1 ? 2 : _engine.State.Speed == 2 ? .5 : 1;
                Play(AudioCue.Click);
                Notify($"遊戲速度：{_engine.State.Speed:0.##} 倍");
                break;
            case UiActionId.UseCivilizationPower when _engine is not null:
                if (_engine.UsePower(0))
                {
                    Play(AudioCue.Age);
                    Notify($"已發動「{GameData.Civilizations[_engine.Player(0).Civ].PowerName}」！");
                }
                else Notify("軍令尚未就緒，或需先進入封建時代。", error: true);
                break;
            case UiActionId.MoveOrder:
                _controller?.BeginMoveTarget();
                Notify("左鍵點選移動目的地；右鍵可隨時取消。");
                break;
            case UiActionId.AttackMoveOrder:
                _controller?.BeginAttackMove();
                Notify("左鍵點選進軍目的地。");
                break;
            case UiActionId.StopOrder:
                _controller?.StopSelected();
                break;
            case UiActionId.SetRallyPoint:
                _controller?.BeginRallyTarget();
                Notify("左鍵點選新的集合點。");
                break;
            case UiActionId.BuildBuilding when action.Key is not null:
                _controller?.BeginBuild(action.Key);
                Notify($"請在戰場放置{GameData.Buildings[action.Key].Name}。");
                break;
            case UiActionId.TrainUnit when action.Key is not null:
                QueueSelectedBuilding(action.Key);
                break;
            case UiActionId.ResearchTechnology when action.Key is not null:
                Research(action.Key);
                break;
            case UiActionId.AdvanceAge:
                AdvanceAge();
                break;
            case UiActionId.CancelQueueItem:
                CancelQueue(action.Value);
                break;
            case UiActionId.PreviousCommandPage:
            case UiActionId.NextCommandPage:
                _commandPage = Math.Max(0, action.Value);
                break;
            case UiActionId.NavigateMinimap:
                NavigateMinimap(pointer);
                break;
            case UiActionId.SaveGame:
                SaveGame(showNotice: true);
                break;
            case UiActionId.ExportSave:
                ExportGame();
                break;
            case UiActionId.OpenGuide:
                OpenGuide();
                break;
            case UiActionId.CloseGuide:
                CloseGuide();
                break;
            case UiActionId.ReturnToMainMenu:
                ReturnToMenu();
                break;
            case UiActionId.ToggleTutorialPanel:
                _tutorialCollapsed = !_tutorialCollapsed;
                break;
            case UiActionId.ExitTutorial when _engine is not null:
                _engine.CompleteTutorial(markComplete: true);
                Notify("教學已完成；敵軍將全面投入戰鬥。 ");
                Play(AudioCue.Win);
                break;
        }
    }

    private void StartNewGame(bool tutorial)
    {
        var options = new NewGameOptions
        {
            Civilization = _preferences.Civilization,
            Difficulty = tutorial ? "休閒" : _preferences.Difficulty,
            PlayerCount = tutorial ? 2 : _preferences.PlayerCount,
            Tutorial = tutorial,
            Seed = Environment.TickCount
        };
        AttachEngine(GameEngine.CreateNew(options), centerCamera: true);
        Notify(tutorial ? "新手教學已開始。" : "斥候已進入戰場；建立你的帝國吧！");
        Play(AudioCue.Age);
    }

    private void AttachEngine(GameEngine engine, bool centerCamera)
    {
        DetachEngine();
        _engine = engine;
        if (!_engine.State.Ended)
        {
            _engine.State.Paused = false;
        }
        _engine.GameEnded += OnGameEnded;
        _engine.AutosaveRequested += OnAutosaveRequested;
        _controller = new GameController(_engine, centerCamera);
        _controller.Signaled += OnControllerSignal;
        _screen = ScreenMode.Playing;
        _pauseMenuOpen = false;
        _guideOpen = false;
        _tutorialCollapsed = false;
        _commandPage = 0;
        _simulationAccumulator = 0;
        _interpolationAlpha = 1;
        _preferences.Civilization = _engine.Player(0).Civ;
        _preferences.Difficulty = _engine.State.Difficulty;
        _preferences.PlayerCount = _engine.State.PlayerCount;
        SavePreferences();
    }

    private void DetachEngine()
    {
        if (_controller is not null)
        {
            _controller.Signaled -= OnControllerSignal;
            _controller = null;
        }
        if (_engine is not null)
        {
            _engine.GameEnded -= OnGameEnded;
            _engine.AutosaveRequested -= OnAutosaveRequested;
            _engine = null;
        }
    }

    private void PauseGame()
    {
        if (_engine is null || _engine.State.Ended) return;
        _pauseMenuOpen = true;
        _guideOpen = false;
        _engine.State.Paused = true;
        _controller?.SynchronizeInput();
        Play(AudioCue.Click);
    }

    private void ResumeGame()
    {
        if (_engine is null || _engine.State.Ended) return;
        _pauseMenuOpen = false;
        _guideOpen = false;
        _engine.State.Paused = false;
        _simulationAccumulator = 0;
        _controller?.SynchronizeInput();
        Play(AudioCue.Click);
    }

    private void OpenGuide()
    {
        if (_engine is null) return;
        _guideOpen = true;
        _pauseMenuOpen = false;
        _engine.State.Paused = true;
        _controller?.SynchronizeInput();
    }

    private void CloseGuide()
    {
        if (_engine is null) return;
        _guideOpen = false;
        _engine.State.Paused = false;
        _simulationAccumulator = 0;
        _controller?.SynchronizeInput();
    }

    private void ReturnToMenu()
    {
        if (_engine is { State.Ended: false }) TrySaveAutosave(_engine);
        DetachEngine();
        _screen = ScreenMode.MainMenu;
        _pauseMenuOpen = false;
        _guideOpen = false;
        _notice = null;
        Play(AudioCue.Click);
    }

    private void QueueSelectedBuilding(string unitType)
    {
        if (_engine is null) return;
        var building = PrimarySelected(entity => entity is { Kind: "building", Faction: 0 });
        if (building is not null && _engine.QueueUnit(building.Id, unitType))
        {
            Notify($"已排入訓練：{GameData.Units[unitType].Name}");
            Play(AudioCue.Build);
        }
        else Notify("無法訓練：請檢查時代、人口、資源與建築。", error: true);
    }

    private void Research(string technology)
    {
        if (_engine is null) return;
        if (_engine.Research(0, technology))
        {
            _engine.TutorialEvent("researched");
            var name = technology switch { "attack" => "鍛造攻擊", "armor" => "精製護甲", _ => "經濟技術" };
            Notify($"研究完成：{name}");
            Play(AudioCue.Age);
        }
        else Notify("研究條件或資源不足。", error: true);
    }

    private void AdvanceAge()
    {
        if (_engine is null) return;
        if (_engine.BeginAgeUp(0))
        {
            Notify($"開始晉升{GameConstants.Ages[Math.Min(3, _engine.Player(0).Age)]}。");
            Play(AudioCue.Age);
        }
        else Notify("無法晉升：請完成前置建築並準備足夠資源。", error: true);
    }

    private void CancelQueue(int queueIndex)
    {
        if (_engine is null) return;
        var building = PrimarySelected(entity => entity is { Kind: "building", Faction: 0 });
        if (building is not null && _engine.CancelQueueItem(building.Id, queueIndex))
        {
            Notify("已取消生產並退還部分資源。");
            Play(AudioCue.Click);
        }
    }

    private EntityState? PrimarySelected(Func<EntityState, bool> predicate)
    {
        if (_engine is null) return null;
        foreach (var id in _engine.State.Selected)
        {
            if (_engine.Entity(id) is { Dead: false } entity && predicate(entity)) return entity;
        }
        return null;
    }

    private void NavigateMinimap(Point pointer)
    {
        if (_engine is null || _gameUi is null) return;
        var map = _gameUi.CurrentLayout.Minimap;
        if (map.Width <= 0 || map.Height <= 0) return;
        var normalizedX = Math.Clamp((pointer.X - map.X) / (double)map.Width, 0, 1);
        var normalizedY = Math.Clamp((pointer.Y - map.Y) / (double)map.Height, 0, 1);
        _engine.State.Camera.X = normalizedX * GameConstants.WorldWidth;
        _engine.State.Camera.Y = normalizedY * GameConstants.WorldHeight;
        _engine.TutorialEvent("camera");
    }

    private void ToggleFullscreen()
    {
        _graphics.IsFullScreen = !_graphics.IsFullScreen;
        _graphics.ApplyChanges();
        _preferences.Fullscreen = _graphics.IsFullScreen;
        SavePreferences();
        Play(AudioCue.Click);
    }

    private void SaveGame(bool showNotice)
    {
        if (_engine is null) return;
        try
        {
            _saveService.SaveAutosave(_engine);
            if (showNotice) Notify("戰局已保存，可在下次啟動時繼續。 ");
            Play(AudioCue.Age);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify($"儲存失敗：{exception.Message}", error: true);
        }
    }

    private void TrySaveAutosave(GameEngine engine)
    {
        try
        {
            _saveService.SaveAutosave(engine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify($"自動儲存失敗：{exception.Message}", error: true);
        }
    }

    private void LoadAutosave()
    {
        try
        {
            AttachEngine(_saveService.LoadAutosave(), centerCamera: false);
            Notify("已接續上次戰局。 ");
            Play(AudioCue.Age);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            Notify($"無法載入存檔：{exception.Message}", error: true);
        }
    }

    private void ExportGame()
    {
        if (_engine is null) return;
        try
        {
            Directory.CreateDirectory(PortableSaveDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(PortableSaveDirectory, $"帝國餘燼-存檔-{stamp}.json");
            _saveService.Export(_engine, path);
            Notify($"可攜式存檔已匯出：{path}", seconds: 6);
            Play(AudioCue.Age);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify($"匯出失敗：{exception.Message}", error: true);
        }
    }

    private void ImportNewestOrPrompt()
    {
        try
        {
            var file = Directory.Exists(PortableSaveDirectory)
                ? Directory.EnumerateFiles(PortableSaveDirectory, "*.json").MaxBy(File.GetLastWriteTimeUtc)
                : null;
            if (file is null)
            {
                Notify("請把 JSON 存檔拖放到遊戲視窗；匯出的檔案位於「文件／帝國餘燼／存檔」。", seconds: 7);
                return;
            }
            ImportGame(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify($"無法尋找存檔：{exception.Message}", error: true);
        }
    }

    private void ImportGame(string path)
    {
        try
        {
            AttachEngine(_saveService.Import(path), centerCamera: false);
            _saveService.SaveAutosave(_engine!);
            Notify($"存檔匯入成功：{Path.GetFileName(path)}", seconds: 5);
            Play(AudioCue.Age);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            Notify($"無法匯入存檔：{exception.Message}", error: true, seconds: 6);
        }
    }

    private void TryOpenLaunchSave()
    {
        var path = _launchArguments.FirstOrDefault(argument => argument.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(argument));
        if (path is not null) ImportGame(path);
    }

    private void OnFileDrop(object? sender, FileDropEventArgs eventArgs)
    {
        var path = eventArgs.Files.FirstOrDefault(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (path is not null) ImportGame(path);
        else Notify("請拖放有效的 JSON 戰局存檔。", error: true);
    }

    private void OnAutosaveRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is GameEngine engine) TrySaveAutosave(engine);
    }

    private void OnGameEnded(object? sender, GameEndedEventArgs eventArgs)
    {
        _pauseMenuOpen = false;
        _guideOpen = false;
        Play(eventArgs.HumanWon ? AudioCue.Win : AudioCue.Lose);
        TrySaveAutosave((GameEngine)sender!);
    }

    private void OnControllerSignal(object? sender, ControllerEvent eventArgs)
    {
        switch (eventArgs.Signal)
        {
            case ControllerSignal.TogglePause:
                if (_pauseMenuOpen) ResumeGame(); else PauseGame();
                return;
            case ControllerSignal.ToggleFullscreen:
                ToggleFullscreen();
                return;
            case ControllerSignal.Save:
                SaveGame(showNotice: true);
                return;
            case ControllerSignal.Export:
                ExportGame();
                return;
            case ControllerSignal.Import:
                ImportNewestOrPrompt();
                return;
            case ControllerSignal.SelectionChanged:
                Play(AudioCue.Select);
                return;
            case ControllerSignal.CommandIssued:
                Play(AudioCue.Move);
                break;
            case ControllerSignal.BuildPlaced:
                Play(AudioCue.Build);
                break;
            case ControllerSignal.Power:
                Play(AudioCue.Age);
                break;
            case ControllerSignal.Error:
                Play(AudioCue.Click);
                break;
        }
        if (!string.IsNullOrWhiteSpace(eventArgs.Message))
        {
            Notify(eventArgs.Message, eventArgs.Signal == ControllerSignal.Error);
        }
    }

    private void DrawWorldInputOverlay(Rectangle worldViewport, Point pointer)
    {
        if (_controller is null || _engine is null || _ui is null || _assets is null) return;
        if (_controller.IsSelecting)
        {
            var selection = _controller.CurrentSelectionRectangle(pointer);
            _ui.Fill(selection, UiTheme.Cyan * .12f);
            _ui.Stroke(selection, UiTheme.Cyan, 1);
        }

        if (_controller.BuildMode is not null && worldViewport.Contains(pointer) && GameData.Buildings.TryGetValue(_controller.BuildMode, out var definition) &&
            _assets.TryGetBuildingSprite(_controller.BuildMode, out var sprite))
        {
            var world = _controller.ScreenToWorld(pointer, worldViewport);
            var player = _engine.Player(0);
            var valid = _engine.CanBuild(0, definition.Key, world.X, world.Y) &&
                        GameEngine.CanAfford(player, _engine.AdjustedBuildingCost(definition, player.Civ));
            var origin = new Vector2(sprite.SourceRectangle.Width * .5f, sprite.SourceRectangle.Height);
            var scale = new Vector2(
                sprite.DisplaySize.X / sprite.SourceRectangle.Width * (float)_engine.State.Camera.Zoom,
                sprite.DisplaySize.Y / sprite.SourceRectangle.Height * (float)_engine.State.Camera.Zoom);
            _spriteBatch!.Draw(sprite.Texture, pointer.ToVector2(), sprite.SourceRectangle, (valid ? UiTheme.Good : UiTheme.Danger) * .68f, 0, origin, scale, SpriteEffects.None, 0);
            _ui.TextShadowed(valid ? "可興建" : "無法興建", new Vector2(pointer.X, pointer.Y + 14), 12, valid ? UiTheme.Good : UiTheme.Danger, TextAnchor.TopCenter);
        }
        else if ((_controller.AttackMoveMode || _controller.MoveTargetMode || _controller.RallyTargetMode) && worldViewport.Contains(pointer))
        {
            var color = _controller.AttackMoveMode ? UiTheme.Danger : _controller.RallyTargetMode ? UiTheme.Gold : UiTheme.Cyan;
            _ui.Line(new Vector2(pointer.X - 12, pointer.Y), new Vector2(pointer.X + 12, pointer.Y), color, 2);
            _ui.Line(new Vector2(pointer.X, pointer.Y - 12), new Vector2(pointer.X, pointer.Y + 12), color, 2);
        }
    }

    private void Notify(string message, bool error = false, float seconds = 4)
    {
        _notice = error ? $"注意：{message}" : message;
        _noticeRemaining = Math.Max(1, seconds);
    }

    private void Play(AudioCue cue) => _audio?.Play(cue);

    private void NormalizePreferences()
    {
        if (string.IsNullOrWhiteSpace(_preferences.Civilization) || !GameData.Civilizations.ContainsKey(_preferences.Civilization)) _preferences.Civilization = "britons";
        if (string.IsNullOrWhiteSpace(_preferences.Difficulty) || !GameData.Difficulties.ContainsKey(_preferences.Difficulty)) _preferences.Difficulty = "征戰";
        _preferences.PlayerCount = Math.Clamp(_preferences.PlayerCount, 2, 4);
    }

    private void SavePreferences()
    {
        if (_smokeMode) return;
        try
        {
            if (!_graphics.IsFullScreen && Window.ClientBounds.Width > 0 && Window.ClientBounds.Height > 0)
            {
                _preferences.WindowWidth = Window.ClientBounds.Width;
                _preferences.WindowHeight = Window.ClientBounds.Height;
            }
            _preferences.Volume = _audio?.MasterVolume ?? _preferences.Volume;
            GamePreferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
        }
    }

    private static Rectangle GetWorldViewport(Rectangle fullViewport)
    {
        const int topHeight = 70;
        var bottomHeight = Math.Clamp(fullViewport.Height / 4, 184, 220);
        return new Rectangle(
            fullViewport.X,
            fullViewport.Y + topHeight,
            fullViewport.Width,
            Math.Max(1, fullViewport.Height - topHeight - bottomHeight));
    }

    private bool Pressed(Keys key, KeyboardState keyboard) => keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    private static string PortableSaveDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "帝國餘燼",
        "存檔");

    private enum ScreenMode
    {
        MainMenu,
        Playing
    }
}
