#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Empire.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Empire.Game.Ui;

/// <summary>
/// Native immediate-mode presentation for the menu and in-game HUD. The caller owns
/// SpriteBatch.Begin/End and applies returned actions to GameEngine or platform services.
/// Hitboxes and all data-dependent caches are reused across frames.
/// </summary>
public sealed class GameUiRenderer
{
    private const int HitboxCapacity = 192;
    private const int MinimumUiMargin = 12;
    private const int TutorialWidth = 430;

    private static readonly Color[] FactionColors =
    [
        new(91, 197, 216),
        new(236, 100, 91),
        new(167, 123, 232),
        new(229, 185, 85)
    ];

    private static readonly string[] BuildKeys =
    [
        "house", "mill", "lumber", "farm", "barracks", "blacksmith", "range",
        "stable", "tower", "wall", "castle", "workshop", "wonder"
    ];

    private readonly SpriteBatch _batch;
    private readonly UiToolkit _ui;
    private readonly Texture2D _menuBackground;
    private readonly Texture2D? _empireIcon;
    private readonly List<UiHitbox> _hitboxes = new(HitboxCapacity);
    private readonly CivilizationDefinition[] _civilizations;
    private readonly DifficultyDefinition[] _difficulties;
    private readonly Color[] _civilizationColors;
    private readonly Color[] _civilizationAccents;
    private readonly string[] _civilizationUniqueLabels;
    private readonly string[] _civilizationPowerDescriptions;
    private readonly string[] _difficultyDescriptions;
    private readonly string[] _tutorialBodies;
    private readonly string[] _tutorialHints;

    public GameUiRenderer(
        SpriteBatch batch,
        UiToolkit ui,
        Texture2D menuBackground,
        Texture2D? empireIcon = null)
    {
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _menuBackground = menuBackground ?? throw new ArgumentNullException(nameof(menuBackground));
        _empireIcon = empireIcon;

        _civilizations = new CivilizationDefinition[GameData.Civilizations.Count];
        _civilizationColors = new Color[_civilizations.Length];
        _civilizationAccents = new Color[_civilizations.Length];
        _civilizationUniqueLabels = new string[_civilizations.Length];
        _civilizationPowerDescriptions = new string[_civilizations.Length];
        var civilizationIndex = 0;
        foreach (var civilization in GameData.Civilizations.Values)
        {
            _civilizations[civilizationIndex] = civilization;
            _civilizationColors[civilizationIndex] = ParseHexColor(civilization.Color, UiTheme.Cyan);
            _civilizationAccents[civilizationIndex] = ParseHexColor(civilization.Accent, UiTheme.Gold);
            _civilizationUniqueLabels[civilizationIndex] = GameData.Units.TryGetValue(civilization.UniqueUnit, out var unique)
                ? $"獨特兵種：{unique.Name}"
                : "獨特兵種";
            _civilizationPowerDescriptions[civilizationIndex] = _ui.Wrap(civilization.PowerDescription, 12, 318);
            civilizationIndex++;
        }

        _difficulties = new DifficultyDefinition[GameData.Difficulties.Count];
        _difficultyDescriptions = new string[_difficulties.Length];
        var difficultyIndex = 0;
        foreach (var difficulty in GameData.Difficulties.Values)
        {
            _difficulties[difficultyIndex] = difficulty;
            _difficultyDescriptions[difficultyIndex] = _ui.Wrap(difficulty.Description, 12, 318);
            difficultyIndex++;
        }

        _tutorialBodies = new string[TutorialCatalog.Steps.Count];
        _tutorialHints = new string[TutorialCatalog.Steps.Count];
        for (var stepIndex = 0; stepIndex < TutorialCatalog.Steps.Count; stepIndex++)
        {
            var step = TutorialCatalog.Steps[stepIndex];
            _tutorialBodies[stepIndex] = _ui.Wrap(LocalizeTutorialText(step.Body), 14, TutorialWidth - 42);
            _tutorialHints[stepIndex] = _ui.Wrap(LocalizeTutorialText(step.Hint), 13, TutorialWidth - 42);
        }
    }

    /// <summary>Hit targets from the latest draw call; the same list instance is reused.</summary>
    public IReadOnlyList<UiHitbox> Hitboxes => _hitboxes;

    public UiLayoutSnapshot CurrentLayout { get; private set; }

    /// <summary>
    /// Resolves the topmost enabled action at a pointer position. Modal hitboxes are emitted
    /// last, so reverse traversal naturally gives them priority.
    /// </summary>
    public bool TryHit(Point pointer, out UiHitbox hitbox)
    {
        for (var index = _hitboxes.Count - 1; index >= 0; index--)
        {
            var candidate = _hitboxes[index];
            if (candidate.Contains(pointer))
            {
                hitbox = candidate;
                return true;
            }
        }

        hitbox = default;
        return false;
    }

    public bool TryGetAction(Point pointer, out UiAction action)
    {
        if (TryHit(pointer, out var hitbox))
        {
            action = hitbox.Action;
            return true;
        }

        action = UiAction.None;
        return false;
    }

    /// <summary>Draws the full native setup menu. SpriteBatch must already be active.</summary>
    public void DrawMainMenu(
        Rectangle viewport,
        Point pointer,
        in MainMenuUiState state,
        float animationSeconds)
    {
        ResetFrame(viewport);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        DrawCover(_menuBackground, viewport, Color.White);
        _ui.Fill(viewport, new Color(2, 8, 12, 142));
        _ui.Fill(new Rectangle(viewport.X, viewport.Y, viewport.Width, 132), new Color(3, 9, 13, 184));
        _ui.Fill(new Rectangle(viewport.X, viewport.Bottom - 90, viewport.Width, 90), new Color(2, 7, 10, 178));

        var pulse = 0.72f + MathF.Sin(animationSeconds * 1.35f) * 0.12f;
        var header = new Rectangle(viewport.X + 24, viewport.Y + 16, viewport.Width - 48, 90);
        _ui.Line(new Vector2(header.X, header.Bottom - 2), new Vector2(header.Right, header.Bottom - 2), UiTheme.Gold * pulse, 2);

        var titleX = header.X;
        if (_empireIcon is not null)
        {
            var icon = new Rectangle(header.X, header.Y + 2, 76, 76);
            _batch.Draw(_empireIcon, icon, Color.White);
            _ui.Stroke(icon, UiTheme.Gold * .9f, 2);
            titleX = icon.Right + 18;
        }

        _ui.TextShadowed("帝國餘燼：百族爭霸", new Vector2(titleX, header.Y + 7), 31, UiTheme.Gold);
        _ui.Text("十三文明各有所長 · 在真正的即時戰場寫下王朝", new Vector2(titleX + 2, header.Y + 52), 15, UiTheme.Ink);
        _ui.Text("原生高效戰略版", new Vector2(header.Right, header.Y + 24), 14, UiTheme.Cyan, TextAnchor.TopRight);

        var outerMargin = Math.Max(MinimumUiMargin, viewport.Width / 80);
        var gap = 14;
        var contentTop = header.Bottom + 10;
        var contentBottom = viewport.Bottom - outerMargin;
        var setupWidth = Math.Clamp(viewport.Width / 4, 310, 390);
        var setupPanel = new Rectangle(viewport.Right - outerMargin - setupWidth, contentTop, setupWidth, Math.Max(420, contentBottom - contentTop));
        var civilizationGrid = new Rectangle(viewport.X + outerMargin, contentTop, setupPanel.X - gap - (viewport.X + outerMargin), setupPanel.Height);

        _ui.Panel(civilizationGrid, new Color(7, 17, 22, 225), new Color(151, 129, 72, 185), 1);
        _ui.Panel(setupPanel, new Color(7, 17, 22, 238), new Color(151, 129, 72, 210), 2);

        var selectedCivilization = FindCivilizationIndex(state.Civilization);
        var selectedDifficulty = FindDifficultyIndex(state.Difficulty);
        DrawCivilizationGrid(civilizationGrid, pointer, selectedCivilization);
        DrawSetupPanel(setupPanel, pointer, state, selectedCivilization, selectedDifficulty);

        CurrentLayout = new UiLayoutSnapshot
        {
            Viewport = viewport,
            Header = header,
            CivilizationGrid = civilizationGrid,
            SetupPanel = setupPanel
        };
    }

    /// <summary>Draws the HUD and any active native modal. SpriteBatch must already be active.</summary>
    public void DrawGameplay(
        Rectangle viewport,
        Point pointer,
        GameState game,
        in GameplayUiState view,
        float animationSeconds)
    {
        ArgumentNullException.ThrowIfNull(game);
        ResetFrame(viewport);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var topHeight = 70;
        var bottomHeight = Math.Clamp(viewport.Height / 4, 184, 220);
        var topBar = new Rectangle(viewport.X, viewport.Y, viewport.Width, topHeight);
        var bottomBar = new Rectangle(viewport.X, viewport.Bottom - bottomHeight, viewport.Width, bottomHeight);
        var selectionWidth = Math.Clamp(viewport.Width / 5, 272, 338);
        var minimapWidth = Math.Clamp(viewport.Width / 6, 238, 286);
        var selectionPanel = new Rectangle(bottomBar.X + 10, bottomBar.Y + 10, selectionWidth, bottomBar.Height - 20);
        var minimap = new Rectangle(bottomBar.Right - minimapWidth - 10, bottomBar.Y + 10, minimapWidth, bottomBar.Height - 20);
        var commandPanel = new Rectangle(selectionPanel.Right + 10, bottomBar.Y + 10, minimap.X - selectionPanel.Right - 20, bottomBar.Height - 20);
        var queuePanel = new Rectangle(commandPanel.X + 8, commandPanel.Y + 7, commandPanel.Width - 16, 55);

        CurrentLayout = new UiLayoutSnapshot
        {
            Viewport = viewport,
            TopBar = topBar,
            BottomBar = bottomBar,
            SelectionPanel = selectionPanel,
            CommandPanel = commandPanel,
            QueuePanel = queuePanel,
            Minimap = minimap
        };

        DrawTopBar(topBar, pointer, game, view);
        _ui.Fill(bottomBar, new Color(4, 11, 15, 239));
        _ui.Line(new Vector2(bottomBar.X, bottomBar.Y), new Vector2(bottomBar.Right, bottomBar.Y), UiTheme.Gold * .72f, 2);

        var selected = FindPrimarySelected(game, out var selectionCount);
        DrawSelectionPanel(selectionPanel, game, selected, selectionCount);
        DrawQueuePanel(queuePanel, pointer, game, selected);
        DrawCommandPanel(commandPanel, pointer, game, view.CommandPage, selected);
        DrawMinimap(minimap, pointer, game);

        DrawNotice(viewport, view.Notice, view.NoticeRemainingSeconds);
        DrawTutorial(viewport, pointer, game, view.TutorialCollapsed);

        if (game.Ended)
        {
            DrawVictoryOverlay(viewport, pointer, game, animationSeconds);
        }
        else if (view.GuideOpen)
        {
            DrawGuideOverlay(viewport, pointer);
        }
        else if (view.PauseMenuOpen)
        {
            DrawPauseOverlay(viewport, pointer, game, view);
        }
    }

    private void DrawCivilizationGrid(Rectangle panel, Point pointer, int selectedIndex)
    {
        _ui.Text("選擇文明", new Vector2(panel.X + 16, panel.Y + 12), 18, UiTheme.Gold);
        _ui.Text("每個文明都有鮮明優勢、代價與獨特軍令", new Vector2(panel.Right - 16, panel.Y + 16), 12, UiTheme.Muted, TextAnchor.TopRight);

        var area = new Rectangle(panel.X + 12, panel.Y + 43, panel.Width - 24, panel.Height - 55);
        var columns = area.Width >= 940 ? 4 : area.Width >= 540 ? 3 : 2;
        var rows = (_civilizations.Length + columns - 1) / columns;
        const int gap = 8;
        var cardWidth = Math.Max(120, (area.Width - gap * (columns - 1)) / columns);
        var cardHeight = Math.Max(76, (area.Height - gap * (rows - 1)) / rows);

        for (var index = 0; index < _civilizations.Length; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var card = new Rectangle(
                area.X + column * (cardWidth + gap),
                area.Y + row * (cardHeight + gap),
                cardWidth,
                cardHeight);
            var selected = index == selectedIndex;
            var hovered = card.Contains(pointer);
            var civilization = _civilizations[index];
            var accent = _civilizationAccents[index];
            var faction = _civilizationColors[index];
            var fill = selected
                ? new Color((int)faction.R, faction.G, faction.B, 88)
                : hovered ? new Color(32, 51, 55, 239) : new Color(14, 28, 33, 230);

            _ui.Panel(card, fill, selected || hovered ? accent : UiTheme.Border, selected ? 2 : 1);
            var sealBox = new Rectangle(card.X + 9, card.Y + 10, Math.Min(46, card.Height - 20), Math.Min(46, card.Height - 20));
            _ui.Fill(sealBox, new Color((int)faction.R, faction.G, faction.B, 178));
            _ui.Stroke(sealBox, accent, 2);
            _ui.Text(civilization.Seal, new Vector2(sealBox.Center.X, sealBox.Center.Y), 22, Color.White, TextAnchor.Center);
            _ui.Text(civilization.Name, new Vector2(sealBox.Right + 9, card.Y + 11), 16, selected ? Color.White : UiTheme.Ink);
            if (cardHeight >= 86)
            {
                _ui.Text(civilization.Style, new Vector2(sealBox.Right + 9, card.Y + 37), 12, UiTheme.Muted);
            }
            if (cardHeight >= 112)
            {
                _ui.Text(_civilizationUniqueLabels[index], new Vector2(card.X + 10, card.Bottom - 24), 12, accent);
            }

            AddHitbox(card, new UiAction(UiActionId.SelectCivilization, civilization.Key));
        }
    }

    private void DrawSetupPanel(
        Rectangle panel,
        Point pointer,
        in MainMenuUiState state,
        int selectedCivilizationIndex,
        int selectedDifficultyIndex)
    {
        var civilization = _civilizations[selectedCivilizationIndex];
        var accent = _civilizationAccents[selectedCivilizationIndex];
        var x = panel.X + 18;
        var width = panel.Width - 36;
        var y = panel.Y + 16;
        var compact = panel.Height < 600;

        _ui.Text(civilization.Seal, new Vector2(x + 19, y + 20), 27, accent, TextAnchor.Center);
        _ui.Text(civilization.Name, new Vector2(x + 48, y), 22, UiTheme.Gold);
        _ui.Text(civilization.Style, new Vector2(x + 49, y + 31), 13, UiTheme.Muted);
        y += 63;

        if (compact)
        {
            var advantage = civilization.Pros.Count > 0 ? Shorten(civilization.Pros[0], 14) : "均衡發展";
            var drawback = civilization.Cons.Count > 0 ? Shorten(civilization.Cons[0], 14) : "無明顯代價";
            _ui.Text($"優勢：{advantage}", new Vector2(x, y), 12, UiTheme.Good);
            _ui.Text($"代價：{drawback}", new Vector2(x, y + 20), 12, UiTheme.Danger);
            _ui.Text(_civilizationUniqueLabels[selectedCivilizationIndex], new Vector2(x, y + 40), 12, accent);
            _ui.Text($"軍令：{civilization.PowerName}", new Vector2(x, y + 60), 12, UiTheme.Gold);
        }
        else
        {
            _ui.Text("文明優勢", new Vector2(x, y), 13, UiTheme.Good);
            y += 21;
            for (var index = 0; index < civilization.Pros.Count && index < 2; index++)
            {
                _ui.Text($"＋ {civilization.Pros[index]}", new Vector2(x, y), 12, UiTheme.Ink);
                y += 19;
            }

            _ui.Text("平衡代價", new Vector2(x, y + 2), 13, UiTheme.Danger);
            y += 23;
            if (civilization.Cons.Count > 0)
            {
                _ui.Text($"－ {civilization.Cons[0]}", new Vector2(x, y), 12, UiTheme.Ink);
            }
            y += 23;
            _ui.Text(_civilizationUniqueLabels[selectedCivilizationIndex], new Vector2(x, y), 12, accent);
            _ui.Text($"軍令：{civilization.PowerName}", new Vector2(x, y + 20), 13, UiTheme.Gold);
            _ui.Text(_civilizationPowerDescriptions[selectedCivilizationIndex], new Vector2(x, y + 40), 12, UiTheme.Muted);
        }

        var difficultyTop = compact ? panel.Y + 164 : panel.Y + Math.Min(286, panel.Height - 316);
        _ui.Line(new Vector2(x, difficultyTop - 8), new Vector2(panel.Right - 18, difficultyTop - 8), UiTheme.Border);
        _ui.Text("人工智慧難度", new Vector2(x, difficultyTop), 15, UiTheme.Gold);
        var difficultyButtonTop = difficultyTop + (compact ? 24 : 28);
        var difficultyGap = 7;
        var difficultyWidth = (width - difficultyGap) / 2;
        var difficultyButtonHeight = compact ? 29 : 35;
        var difficultyRowStride = compact ? 34 : 42;
        for (var index = 0; index < _difficulties.Length; index++)
        {
            var difficulty = _difficulties[index];
            var bounds = new Rectangle(
                x + (index % 2) * (difficultyWidth + difficultyGap),
                difficultyButtonTop + (index / 2) * difficultyRowStride,
                difficultyWidth,
                difficultyButtonHeight);
            DrawActionButton(bounds, difficulty.Key, pointer, new UiAction(UiActionId.SelectDifficulty, difficulty.Key), true, index == selectedDifficultyIndex, 14);
        }

        var descriptionY = difficultyTop + (compact ? 91 : 114);
        _ui.Text(_difficultyDescriptions[selectedDifficultyIndex], new Vector2(x, descriptionY), 12, UiTheme.Muted);

        var playersY = descriptionY + (compact ? 40 : 49);
        _ui.Text("交戰人數", new Vector2(x, playersY + 8), 14, UiTheme.Gold);
        var playerButtonX = x + 88;
        var playerButtonWidth = Math.Max(48, (width - 88 - 12) / 3);
        var normalizedPlayers = Math.Clamp(state.PlayerCount, 2, 4);
        for (var playerCount = 2; playerCount <= 4; playerCount++)
        {
            var bounds = new Rectangle(playerButtonX + (playerCount - 2) * (playerButtonWidth + 6), playersY, playerButtonWidth, 36);
            DrawActionButton(bounds, $"{playerCount} 人", pointer, new UiAction(UiActionId.SelectPlayerCount, Value: playerCount), true, playerCount == normalizedPlayers, 14);
        }

        var actionsTop = panel.Bottom - 116;
        var actionGap = 8;
        var actionWidth = (width - actionGap) / 2;
        DrawActionButton(new Rectangle(x, actionsTop, actionWidth, 48), "開始征服", pointer, new UiAction(UiActionId.StartGame), true, false, 16, UiTheme.Gold);
        DrawActionButton(new Rectangle(x + actionWidth + actionGap, actionsTop, actionWidth, 48), "新手教學", pointer, new UiAction(UiActionId.StartTutorial), true, false, 16, UiTheme.Cyan);
        DrawActionButton(new Rectangle(x, actionsTop + 56, actionWidth, 42), "繼續戰局", pointer, new UiAction(UiActionId.ContinueGame), state.HasContinueGame, false, 14);
        DrawActionButton(new Rectangle(x + actionWidth + actionGap, actionsTop + 56, actionWidth, 42), "匯入存檔", pointer, new UiAction(UiActionId.ImportSave), true, false, 14);
    }

    private void DrawTopBar(Rectangle bar, Point pointer, GameState game, in GameplayUiState view)
    {
        _ui.Fill(bar, new Color(4, 11, 15, 242));
        _ui.Line(new Vector2(bar.X, bar.Bottom - 1), new Vector2(bar.Right, bar.Bottom - 1), UiTheme.Gold * .65f, 2);

        if (game.Players.Count == 0)
        {
            _ui.Text("正在建立帝國……", new Vector2(bar.X + 18, bar.Center.Y), 15, UiTheme.Muted, TextAnchor.CenterLeft);
            return;
        }

        var player = game.Players[0];
        var chipY = bar.Y + 9;
        var chipWidth = Math.Clamp(bar.Width / 15, 82, 108);
        var x = bar.X + 10;
        DrawInfoChip(new Rectangle(x, chipY, chipWidth, 51), "食物", ResourceText(player.Resources.Food), new Color(220, 157, 78));
        x += chipWidth + 6;
        DrawInfoChip(new Rectangle(x, chipY, chipWidth, 51), "木材", ResourceText(player.Resources.Wood), new Color(90, 175, 106));
        x += chipWidth + 6;
        DrawInfoChip(new Rectangle(x, chipY, chipWidth, 51), "黃金", ResourceText(player.Resources.Gold), UiTheme.Gold);
        x += chipWidth + 6;
        DrawInfoChip(new Rectangle(x, chipY, chipWidth, 51), "石材", ResourceText(player.Resources.Stone), new Color(165, 178, 184));
        x += chipWidth + 6;
        DrawInfoChip(new Rectangle(x, chipY, chipWidth, 51), "人口", $"{player.Pop}/{Math.Min(GameConstants.MaxPopulation, player.PopCap)}", UiTheme.Cyan);

        var age = GameConstants.Ages[Math.Clamp(player.Age - 1, 0, GameConstants.Ages.Length - 1)];
        if (bar.Width >= 1100)
        {
            _ui.Text(age, new Vector2(bar.Center.X, bar.Y + 10), 17, UiTheme.Gold, TextAnchor.TopCenter);
            _ui.Text($"戰局 {FormatClock(game.Time)} · 速度 {game.Speed:0.##} 倍", new Vector2(bar.Center.X, bar.Y + 38), 12, UiTheme.Muted, TextAnchor.TopCenter);
        }

        var controlY = bar.Y + 10;
        var controlWidth = 74;
        var controlGap = 5;
        var right = bar.Right - 10;
        var muteBounds = new Rectangle(right - controlWidth, controlY, controlWidth, 48);
        right = muteBounds.X - controlGap;
        var fullscreenBounds = new Rectangle(right - controlWidth, controlY, controlWidth, 48);
        right = fullscreenBounds.X - controlGap;
        var pauseBounds = new Rectangle(right - controlWidth, controlY, controlWidth, 48);
        right = pauseBounds.X - controlGap;
        var speedBounds = new Rectangle(right - controlWidth, controlY, controlWidth, 48);
        right = speedBounds.X - controlGap;
        var powerWidth = Math.Clamp(bar.Width / 9, 142, 188);
        var powerBounds = new Rectangle(right - powerWidth, controlY, powerWidth, 48);

        var civilization = GameData.Civilizations[player.Civ];
        var powerActive = player.PowerUntil > game.Time;
        var powerReady = player.Age >= 2 && player.PowerReady <= game.Time;
        var powerLabel = powerActive
            ? $"{civilization.PowerName} {Math.Ceiling(player.PowerUntil - game.Time):0} 秒"
            : powerReady ? civilization.PowerName
            : player.Age < 2 ? "封建時代解鎖軍令" : $"軍令冷卻 {Math.Ceiling(player.PowerReady - game.Time):0} 秒";
        DrawActionButton(powerBounds, powerLabel, pointer, new UiAction(UiActionId.UseCivilizationPower), powerReady, powerActive, 12, UiTheme.Gold);
        DrawActionButton(speedBounds, $"速度\n{game.Speed:0.##} 倍", pointer, new UiAction(UiActionId.CycleGameSpeed), true, false, 12);
        DrawActionButton(pauseBounds, game.Paused || view.PauseMenuOpen ? "繼續" : "暫停", pointer, new UiAction(UiActionId.TogglePauseMenu), true, view.PauseMenuOpen, 14);
        DrawActionButton(fullscreenBounds, view.IsFullscreen ? "離開全螢幕" : "全螢幕", pointer, new UiAction(UiActionId.ToggleFullscreen), true, view.IsFullscreen, 12);
        DrawActionButton(muteBounds, view.IsMuted ? "開啟音效" : "靜音", pointer, new UiAction(UiActionId.ToggleMute), true, view.IsMuted, 13);
    }

    private void DrawSelectionPanel(Rectangle panel, GameState game, EntityState? selected, int count)
    {
        _ui.Panel(panel, UiTheme.PanelSoft, UiTheme.Border);
        _ui.Text("目前選取", new Vector2(panel.X + 13, panel.Y + 10), 13, UiTheme.Gold);
        if (selected is null || count == 0)
        {
            _ui.Text("尚未選取單位", new Vector2(panel.Center.X, panel.Y + 70), 16, UiTheme.Muted, TextAnchor.TopCenter);
            _ui.Text("左鍵點選或拖曳框選", new Vector2(panel.Center.X, panel.Y + 101), 12, UiTheme.Muted, TextAnchor.TopCenter);
            return;
        }

        var name = EntityName(selected);
        var glyph = EntityGlyph(selected);
        _ui.Fill(new Rectangle(panel.X + 13, panel.Y + 39, 54, 54), new Color(45, 66, 69, 230));
        _ui.Stroke(new Rectangle(panel.X + 13, panel.Y + 39, 54, 54), FactionColors[0], 2);
        _ui.Text(glyph, new Vector2(panel.X + 40, panel.Y + 66), 24, UiTheme.Ink, TextAnchor.Center);
        _ui.Text(count > 1 ? $"{name} · 共 {count} 個" : name, new Vector2(panel.X + 78, panel.Y + 41), 17, UiTheme.Ink);
        _ui.Text(OrderLabel(selected), new Vector2(panel.X + 79, panel.Y + 69), 12, UiTheme.Cyan);

        var healthRatio = selected.MaxHp <= 0 ? 0f : (float)(selected.Hp / selected.MaxHp);
        _ui.Text($"生命 {Math.Ceiling(selected.Hp):0} / {Math.Ceiling(selected.MaxHp):0}", new Vector2(panel.X + 14, panel.Y + 105), 12, UiTheme.Muted);
        _ui.Progress(new Rectangle(panel.X + 13, panel.Y + 127, panel.Width - 26, 13), healthRatio, healthRatio > .45f ? UiTheme.Good : UiTheme.Danger);

        if (selected.Kind == "building" && selected.Construction < 1)
        {
            _ui.Text($"施工進度 {selected.Construction * 100:0}%", new Vector2(panel.X + 14, panel.Y + 150), 12, UiTheme.Gold);
            _ui.Progress(new Rectangle(panel.X + 13, panel.Y + 171, panel.Width - 26, 12), (float)selected.Construction, UiTheme.Gold);
        }
        else if (selected.Kind == "unit" && GameData.Units.TryGetValue(selected.Type, out var unit))
        {
            _ui.Text(unit.Description, new Vector2(panel.X + 14, panel.Y + 151), 12, UiTheme.Muted);
        }
        else if (GameData.Buildings.TryGetValue(selected.Type, out var building))
        {
            _ui.Text(building.Description, new Vector2(panel.X + 14, panel.Y + 151), 12, UiTheme.Muted);
        }
    }

    private void DrawQueuePanel(Rectangle panel, Point pointer, GameState game, EntityState? selected)
    {
        _ui.Fill(panel, new Color(8, 19, 24, 224));
        _ui.Stroke(panel, UiTheme.Border);
        _ui.Text("生產列", new Vector2(panel.X + 8, panel.Y + 7), 12, UiTheme.Gold);
        var itemX = panel.X + 65;
        var availableWidth = panel.Right - itemX - 6;
        var slotWidth = Math.Clamp(availableWidth / 5, 48, 130);
        var slotIndex = 0;

        if (selected is not null && selected.Kind == "building")
        {
            for (var queueIndex = 0; queueIndex < selected.Queue.Count && slotIndex < 5; queueIndex++, slotIndex++)
            {
                var item = selected.Queue[queueIndex];
                var bounds = new Rectangle(itemX + slotIndex * slotWidth, panel.Y + 5, slotWidth - 5, panel.Height - 10);
                _ui.Fill(bounds, new Color(25, 40, 45, 230));
                _ui.Stroke(bounds, UiTheme.Border);
                var label = GameData.Units.TryGetValue(item.Type, out var unit)
                    ? slotWidth < 82 ? unit.Glyph : unit.Name
                    : "生產中";
                _ui.Text(label, new Vector2(bounds.X + 6, bounds.Y + 4), 12, UiTheme.Ink);
                var ratio = item.Total <= 0 ? 0f : (float)(1d - item.Remaining / item.Total);
                _ui.Progress(new Rectangle(bounds.X + 5, bounds.Bottom - 11, bounds.Width - 27, 7), ratio, UiTheme.Cyan);
                var cancel = new Rectangle(bounds.Right - 20, bounds.Y + 4, 16, 16);
                _ui.Fill(cancel, new Color(100, 38, 35, 220));
                _ui.Text("撤", new Vector2(cancel.Center.X, cancel.Center.Y), 12, UiTheme.Ink, TextAnchor.Center);
                AddHitbox(cancel, new UiAction(UiActionId.CancelQueueItem, Value: queueIndex));
            }
        }

        if (game.Players.Count > 0 && game.Players[0].AgeUp is { } ageUp && slotIndex < 5)
        {
            var bounds = new Rectangle(itemX + slotIndex * slotWidth, panel.Y + 5, slotWidth - 5, panel.Height - 10);
            _ui.Fill(bounds, new Color(63, 51, 24, 230));
            _ui.Stroke(bounds, UiTheme.Gold);
            _ui.Text($"晉升{GameConstants.Ages[Math.Clamp(ageUp.To - 1, 0, 3)]}", new Vector2(bounds.X + 6, bounds.Y + 4), 12, UiTheme.Gold);
            var ratio = ageUp.Total <= 0 ? 0f : (float)(1d - ageUp.Remaining / ageUp.Total);
            _ui.Progress(new Rectangle(bounds.X + 5, bounds.Bottom - 11, bounds.Width - 10, 7), ratio, UiTheme.Gold);
            slotIndex++;
        }

        if (slotIndex == 0)
        {
            _ui.Text("沒有進行中的生產", new Vector2(itemX + 8, panel.Center.Y), 12, UiTheme.Muted, TextAnchor.CenterLeft);
        }
    }

    private void DrawCommandPanel(Rectangle panel, Point pointer, GameState game, int requestedPage, EntityState? selected)
    {
        _ui.Panel(panel, UiTheme.PanelSoft, UiTheme.Border);
        var area = new Rectangle(panel.X + 8, panel.Y + 69, panel.Width - 16, panel.Height - 77);
        if (selected is null || game.Players.Count == 0)
        {
            _ui.Text("選取村民以建造，或選取軍隊與建築下達命令", new Vector2(area.Center.X, area.Center.Y), 14, UiTheme.Muted, TextAnchor.Center);
            return;
        }

        var player = game.Players[0];
        if (selected.Kind == "unit" && selected.Type == "villager")
        {
            DrawBuildCommands(area, pointer, game, player, requestedPage);
            return;
        }

        if (selected.Kind == "building")
        {
            DrawBuildingCommands(area, pointer, game, player, selected);
            return;
        }

        DrawMilitaryCommands(area, pointer);
    }

    private void DrawBuildCommands(Rectangle area, Point pointer, GameState game, PlayerState player, int requestedPage)
    {
        const int pageSize = 8;
        var pageCount = (BuildKeys.Length + pageSize - 1) / pageSize;
        var page = Math.Clamp(requestedPage, 0, pageCount - 1);
        _ui.Text($"建造建築 · 第 {page + 1} 頁", new Vector2(area.X + 2, area.Y - 3), 12, UiTheme.Gold);
        if (page > 0)
        {
            DrawActionButton(new Rectangle(area.Right - 98, area.Y - 8, 42, 26), "前頁", pointer, new UiAction(UiActionId.PreviousCommandPage, Value: page - 1), true, false, 12);
        }
        if (page + 1 < pageCount)
        {
            DrawActionButton(new Rectangle(area.Right - 50, area.Y - 8, 42, 26), "次頁", pointer, new UiAction(UiActionId.NextCommandPage, Value: page + 1), true, false, 12);
        }

        var buttonsArea = new Rectangle(area.X, area.Y + 23, area.Width, Math.Max(44, area.Height - 23));
        var start = page * pageSize;
        for (var slot = 0; slot < pageSize && start + slot < BuildKeys.Length; slot++)
        {
            var key = BuildKeys[start + slot];
            var definition = GameData.Buildings[key];
            var cost = AdjustedBuildingCost(definition, player.Civ);
            var enabled = player.Age >= definition.Age && GameEngine.CanAfford(player, cost) && MeetsPrerequisites(game, key);
            var bounds = CommandSlot(buttonsArea, slot, pageSize);
            DrawActionButton(bounds, $"{definition.Glyph} {definition.Name}", pointer, new UiAction(UiActionId.BuildBuilding, key), enabled, false, 12, UiTheme.Gold);
        }
    }

    private void DrawBuildingCommands(Rectangle area, Point pointer, GameState game, PlayerState player, EntityState building)
    {
        _ui.Text("建築命令", new Vector2(area.X + 2, area.Y - 3), 12, UiTheme.Gold);
        if (building.Construction < 1)
        {
            _ui.Text("建築尚未完工", new Vector2(area.Center.X, area.Center.Y), 14, UiTheme.Muted, TextAnchor.Center);
            return;
        }

        var buttonsArea = new Rectangle(area.X, area.Y + 23, area.Width, Math.Max(44, area.Height - 23));
        var slot = 0;
        var queuedPopulation = QueuedPopulation(game, player.Faction);
        if (GameData.Buildings.TryGetValue(building.Type, out var definition))
        {
            for (var trainIndex = 0; trainIndex < definition.Trains.Count && slot < 8; trainIndex++, slot++)
            {
                var unit = GameData.Units[definition.Trains[trainIndex]];
                var cost = AdjustedUnitCost(unit, player.Civ);
                var enabled = player.Age >= unit.Age && player.Pop + queuedPopulation + unit.Population <= Math.Min(GameConstants.MaxPopulation, player.PopCap) && GameEngine.CanAfford(player, cost);
                DrawActionButton(CommandSlot(buttonsArea, slot, 8), $"{unit.Glyph} {unit.Name}", pointer, new UiAction(UiActionId.TrainUnit, unit.Key), enabled, false, 12, UiTheme.Cyan);
            }
        }

        if (building.Type == "castle" && GameData.Civilizations.TryGetValue(player.Civ, out var civilization) && GameData.Units.TryGetValue(civilization.UniqueUnit, out var unique) && slot < 8)
        {
            var cost = AdjustedUnitCost(unique, player.Civ);
            var enabled = player.Age >= unique.Age && player.Pop + queuedPopulation + unique.Population <= Math.Min(GameConstants.MaxPopulation, player.PopCap) && GameEngine.CanAfford(player, cost);
            DrawActionButton(CommandSlot(buttonsArea, slot++, 8), $"{unique.Glyph} {unique.Name}", pointer, new UiAction(UiActionId.TrainUnit, unique.Key), enabled, false, 12, UiTheme.Gold);
        }

        if (building.Type == "blacksmith")
        {
            DrawTechnologyButton(buttonsArea, pointer, player, "economy", "經濟技術", player.Tech.Economy, 2, ref slot);
            DrawTechnologyButton(buttonsArea, pointer, player, "attack", "武器技術", player.Tech.Attack, 3, ref slot);
            DrawTechnologyButton(buttonsArea, pointer, player, "armor", "護甲技術", player.Tech.Armor, 3, ref slot);
        }

        if (slot < 8)
        {
            DrawActionButton(CommandSlot(buttonsArea, slot++, 8), "設定集合點", pointer, new UiAction(UiActionId.SetRallyPoint), true, false, 12);
        }
        if (player.Age < 4 && player.AgeUp is null && slot < 8)
        {
            DrawActionButton(CommandSlot(buttonsArea, slot, 8), $"晉升{GameConstants.Ages[player.Age]}", pointer, new UiAction(UiActionId.AdvanceAge), true, false, 12, UiTheme.Gold);
        }
    }

    private void DrawTechnologyButton(Rectangle area, Point pointer, PlayerState player, string key, string label, int level, int maximum, ref int slot)
    {
        if (slot >= 8)
        {
            return;
        }

        var enabled = player.Age >= 2 && level < maximum;
        DrawActionButton(CommandSlot(area, slot++, 8), $"{label} {level}/{maximum}", pointer, new UiAction(UiActionId.ResearchTechnology, key), enabled, false, 12, UiTheme.Gold);
    }

    private void DrawMilitaryCommands(Rectangle area, Point pointer)
    {
        _ui.Text("軍隊命令", new Vector2(area.X + 2, area.Y - 3), 12, UiTheme.Gold);
        var buttonsArea = new Rectangle(area.X, area.Y + 23, area.Width, Math.Max(44, area.Height - 23));
        DrawActionButton(CommandSlot(buttonsArea, 0, 8), "移動", pointer, new UiAction(UiActionId.MoveOrder), true, false, 13, UiTheme.Cyan);
        DrawActionButton(CommandSlot(buttonsArea, 1, 8), "攻擊移動", pointer, new UiAction(UiActionId.AttackMoveOrder), true, false, 13, UiTheme.Danger);
        DrawActionButton(CommandSlot(buttonsArea, 2, 8), "停止", pointer, new UiAction(UiActionId.StopOrder), true, false, 13);
        _ui.Text("快速點按右鍵前進或攻擊；不使用滑鼠中鍵", new Vector2(buttonsArea.X + 4, buttonsArea.Bottom - 18), 12, UiTheme.Muted);
    }

    private void DrawMinimap(Rectangle panel, Point pointer, GameState game)
    {
        _ui.Panel(panel, new Color(5, 15, 18, 242), UiTheme.Gold * .75f, 2);
        _ui.Text("戰略小地圖", new Vector2(panel.X + 10, panel.Y + 7), 12, UiTheme.Gold);
        var map = new Rectangle(panel.X + 9, panel.Y + 28, panel.Width - 18, panel.Height - 37);
        _ui.Fill(map, new Color(18, 38, 35, 255));
        _ui.Stroke(map, new Color(83, 109, 95, 220));

        for (var gridLine = 1; gridLine < 4; gridLine++)
        {
            var lineX = map.X + map.Width * gridLine / 4;
            var lineY = map.Y + map.Height * gridLine / 4;
            _ui.Line(new Vector2(lineX, map.Y), new Vector2(lineX, map.Bottom), new Color(104, 126, 102, 35));
            _ui.Line(new Vector2(map.X, lineY), new Vector2(map.Right, lineY), new Color(104, 126, 102, 35));
        }

        for (var nodeIndex = 0; nodeIndex < game.Nodes.Count; nodeIndex++)
        {
            var node = game.Nodes[nodeIndex];
            if (node.Dead || !IsExplored(game, node.X, node.Y))
            {
                continue;
            }
            var color = node.Type switch
            {
                "wood" => new Color(59, 128, 72),
                "food" => new Color(205, 130, 64),
                "gold" => UiTheme.Gold,
                _ => new Color(150, 160, 164)
            };
            var point = MiniPoint(map, node.X, node.Y);
            _ui.Fill(new Rectangle(point.X - 1, point.Y - 1, 3, 3), color * .75f);
        }

        for (var siteIndex = 0; siteIndex < game.Sites.Count; siteIndex++)
        {
            var site = game.Sites[siteIndex];
            if (!IsExplored(game, site.X, site.Y))
            {
                continue;
            }
            var point = MiniPoint(map, site.X, site.Y);
            var color = site.Owner >= 0 && site.Owner < FactionColors.Length ? FactionColors[site.Owner] : Color.White;
            _ui.Fill(new Rectangle(point.X - 3, point.Y - 3, 7, 7), Color.Black * .75f);
            _ui.Fill(new Rectangle(point.X - 2, point.Y - 2, 5, 5), color);
        }

        for (var entityIndex = 0; entityIndex < game.Entities.Count; entityIndex++)
        {
            var entity = game.Entities[entityIndex];
            if (entity.Dead || entity.Faction != 0 && !IsVisible(game, entity.X, entity.Y))
            {
                continue;
            }
            var point = MiniPoint(map, entity.X, entity.Y);
            var color = entity.Faction >= 0 && entity.Faction < FactionColors.Length ? FactionColors[entity.Faction] : UiTheme.Muted;
            var size = entity.Kind == "building" ? 4 : 2;
            _ui.Fill(new Rectangle(point.X - size / 2, point.Y - size / 2, size, size), color);
        }

        var zoom = Math.Max(.25, game.Camera.Zoom);
        var viewWidth = Math.Clamp((int)(map.Width * (CurrentLayout.Viewport.Width / zoom) / GameConstants.WorldWidth), 12, map.Width);
        var worldHeightOnScreen = Math.Max(1, CurrentLayout.Viewport.Height - CurrentLayout.TopBar.Height - CurrentLayout.BottomBar.Height);
        var viewHeight = Math.Clamp((int)(map.Height * (worldHeightOnScreen / zoom) / GameConstants.WorldHeight), 10, map.Height);
        var cameraPoint = MiniPoint(map, game.Camera.X, game.Camera.Y);
        var cameraBox = new Rectangle(cameraPoint.X - viewWidth / 2, cameraPoint.Y - viewHeight / 2, viewWidth, viewHeight);
        cameraBox.X = Math.Clamp(cameraBox.X, map.X, Math.Max(map.X, map.Right - cameraBox.Width));
        cameraBox.Y = Math.Clamp(cameraBox.Y, map.Y, Math.Max(map.Y, map.Bottom - cameraBox.Height));
        _ui.Stroke(cameraBox, Color.White * .9f, 1);

        AddHitbox(map, new UiAction(UiActionId.NavigateMinimap));
        CurrentLayout = CurrentLayout with { Minimap = map };
    }

    private void DrawTutorial(Rectangle viewport, Point pointer, GameState game, bool collapsed)
    {
        if (!game.Tutorial.Active || game.Tutorial.Completed || TutorialCatalog.Steps.Count == 0)
        {
            return;
        }

        var stepIndex = Math.Clamp(game.Tutorial.Step, 0, TutorialCatalog.Steps.Count - 1);
        if (collapsed)
        {
            var collapsedBounds = new Rectangle(viewport.Right - 224, CurrentLayout.TopBar.Bottom + 12, 208, 38);
            DrawActionButton(collapsedBounds, $"展開教學 {stepIndex + 1}/{TutorialCatalog.Steps.Count}", pointer, new UiAction(UiActionId.ToggleTutorialPanel), true, false, 13, UiTheme.Cyan);
            CurrentLayout = CurrentLayout with { TutorialPanel = collapsedBounds };
            return;
        }

        var height = 244;
        var panel = new Rectangle(viewport.Right - TutorialWidth - 16, CurrentLayout.TopBar.Bottom + 12, TutorialWidth, height);
        _ui.Panel(panel, new Color(7, 19, 25, 244), UiTheme.Cyan * .9f, 2);
        AddHitbox(panel, UiAction.None);
        _ui.Text($"新手教學 {stepIndex + 1} / {TutorialCatalog.Steps.Count}", new Vector2(panel.X + 16, panel.Y + 13), 13, UiTheme.Cyan);
        var collapse = new Rectangle(panel.Right - 78, panel.Y + 8, 62, 28);
        DrawActionButton(collapse, "收合", pointer, new UiAction(UiActionId.ToggleTutorialPanel), true, false, 12);
        var step = TutorialCatalog.Steps[stepIndex];
        _ui.Text(step.Title, new Vector2(panel.X + 16, panel.Y + 44), 19, UiTheme.Gold);
        _ui.Text(_tutorialBodies[stepIndex], new Vector2(panel.X + 16, panel.Y + 76), 14, UiTheme.Ink);
        _ui.Fill(new Rectangle(panel.X + 14, panel.Bottom - 75, panel.Width - 28, 43), new Color(16, 48, 56, 210));
        _ui.Text(_tutorialHints[stepIndex], new Vector2(panel.X + 23, panel.Bottom - 68), 13, UiTheme.Cyan);
        _ui.Progress(new Rectangle(panel.X + 15, panel.Bottom - 22, panel.Width - 118, 9), (stepIndex + 1f) / TutorialCatalog.Steps.Count, UiTheme.Cyan);
        var exit = new Rectangle(panel.Right - 92, panel.Bottom - 30, 77, 22);
        DrawActionButton(exit, "結束教學", pointer, new UiAction(UiActionId.ExitTutorial), true, false, 12, UiTheme.Danger);
        CurrentLayout = CurrentLayout with { TutorialPanel = panel };
    }

    private void DrawNotice(Rectangle viewport, string? notice, float remainingSeconds)
    {
        if (string.IsNullOrWhiteSpace(notice) || remainingSeconds <= 0f)
        {
            return;
        }

        var opacity = Math.Clamp(remainingSeconds * 3f, 0f, 1f);
        var width = Math.Min(580, viewport.Width - 40);
        var panel = new Rectangle(viewport.Center.X - width / 2, CurrentLayout.TopBar.Bottom + 12, width, 48);
        _ui.Panel(panel, new Color(11, 28, 33, (int)(226 * opacity)), UiTheme.Gold * opacity, 1);
        _ui.Text(notice, new Vector2(panel.Center.X, panel.Center.Y), 14, UiTheme.Ink * opacity, TextAnchor.Center);
    }

    private void DrawPauseOverlay(Rectangle viewport, Point pointer, GameState game, in GameplayUiState view)
    {
        _hitboxes.Clear();
        _ui.Fill(viewport, new Color(0, 0, 0, 178));
        var modal = Centered(viewport, Math.Min(560, viewport.Width - 40), Math.Min(594, viewport.Height - 40));
        _ui.Panel(modal, new Color(7, 17, 22, 250), UiTheme.Gold, 2);
        _ui.TextShadowed("戰局暫停", new Vector2(modal.Center.X, modal.Y + 26), 28, UiTheme.Gold, TextAnchor.TopCenter);
        _ui.Text($"{GameConstants.Ages[Math.Clamp(game.Players.Count > 0 ? game.Players[0].Age - 1 : 0, 0, 3)]} · {FormatClock(game.Time)}", new Vector2(modal.Center.X, modal.Y + 68), 13, UiTheme.Muted, TextAnchor.TopCenter);

        var x = modal.X + 42;
        var width = modal.Width - 84;
        var gap = 10;
        var half = (width - gap) / 2;
        var y = modal.Y + 112;
        DrawActionButton(new Rectangle(x, y, width, 50), "返回戰場", pointer, new UiAction(UiActionId.ResumeGame), true, false, 17, UiTheme.Cyan);
        y += 63;
        DrawActionButton(new Rectangle(x, y, half, 45), "儲存戰局", pointer, new UiAction(UiActionId.SaveGame), true, false, 14);
        DrawActionButton(new Rectangle(x + half + gap, y, half, 45), "載入戰局", pointer, new UiAction(UiActionId.LoadGame), view.CanLoadGame, false, 14);
        y += 55;
        DrawActionButton(new Rectangle(x, y, half, 45), "匯出存檔", pointer, new UiAction(UiActionId.ExportSave), true, false, 14);
        DrawActionButton(new Rectangle(x + half + gap, y, half, 45), "匯入存檔", pointer, new UiAction(UiActionId.ImportSave), true, false, 14);
        y += 55;
        DrawActionButton(new Rectangle(x, y, width, 45), "開啟完整指南", pointer, new UiAction(UiActionId.OpenGuide), true, false, 14, UiTheme.Gold);
        y += 58;
        DrawActionButton(new Rectangle(x, y, half, 42), view.IsFullscreen ? "離開全螢幕" : "切換全螢幕", pointer, new UiAction(UiActionId.ToggleFullscreen), true, view.IsFullscreen, 13);
        DrawActionButton(new Rectangle(x + half + gap, y, half, 42), view.IsMuted ? "開啟音效" : "關閉音效", pointer, new UiAction(UiActionId.ToggleMute), true, view.IsMuted, 13);
        y += 61;
        DrawActionButton(new Rectangle(x, y, width, 43), "返回主選單", pointer, new UiAction(UiActionId.ReturnToMainMenu), true, false, 14, UiTheme.Danger);
        _ui.Text("遊戲會定期自動儲存，也可匯出至其他電腦。", new Vector2(modal.Center.X, modal.Bottom - 30), 12, UiTheme.Muted, TextAnchor.TopCenter);
        CurrentLayout = CurrentLayout with { Modal = modal };
    }

    private void DrawGuideOverlay(Rectangle viewport, Point pointer)
    {
        _hitboxes.Clear();
        _ui.Fill(viewport, new Color(0, 0, 0, 192));
        var modal = Centered(viewport, Math.Min(920, viewport.Width - 36), Math.Min(650, viewport.Height - 36));
        _ui.Panel(modal, new Color(7, 17, 22, 252), UiTheme.Cyan, 2);
        _ui.TextShadowed("帝國戰略指南", new Vector2(modal.Center.X, modal.Y + 22), 27, UiTheme.Gold, TextAnchor.TopCenter);
        _ui.Text("所有操作皆為俯視二維戰場；滑鼠中鍵不參與任何控制。", new Vector2(modal.Center.X, modal.Y + 61), 13, UiTheme.Muted, TextAnchor.TopCenter);

        var left = modal.X + 34;
        var right = modal.Center.X + 18;
        var top = modal.Y + 105;
        DrawGuideSection(left, top, "視角與選取", "按住滑鼠右鍵拖曳平移地圖\n滑鼠滾輪縮放；小地圖快速跳轉\n左鍵點選或拖曳框選單位\n按住追加鍵可保留原本的選取", UiTheme.Cyan);
        DrawGuideSection(left, top + 157, "經濟與建造", "選取村民後從下方面板選擇建築\n右鍵點資源即可採集\n磨坊解鎖農田；房舍提高人口\n四種資源共同支撐時代晉升", UiTheme.Good);
        DrawGuideSection(left, top + 314, "存檔與續戰", "暫停選單可儲存、載入與匯出\n自動存檔會跨遊戲工作階段保留\n匯入存檔可在另一部電腦續戰", UiTheme.Gold);

        DrawGuideSection(right, top, "命令與戰鬥", "快速右鍵會依目標移動、採集或攻擊\n攻擊移動會沿途迎戰敵軍\n長槍制騎兵、騎兵制遠程\n弓兵壓長槍、攻城器摧毀建築", UiTheme.Danger);
        DrawGuideSection(right, top + 157, "勝利道路", "摧毀所有敵方城鎮中心\n控制三座王旗並維持霸權\n完成世界奇觀並守住倒數\n文明軍令可在關鍵時刻扭轉戰局", UiTheme.Gold);
        DrawGuideSection(right, top + 314, "快捷鍵與思路", "Shift＋1～4 建立編隊；1～4 召回\nWASD／方向鍵移動視角；R 攻擊移動\nX 停止、F 軍令、H 返回城鎮中心\n全螢幕模式可獲得更寬廣視野", UiTheme.Cyan);

        var close = new Rectangle(modal.Center.X - 105, modal.Bottom - 61, 210, 42);
        DrawActionButton(close, "關閉指南", pointer, new UiAction(UiActionId.CloseGuide), true, false, 15, UiTheme.Cyan);
        CurrentLayout = CurrentLayout with { Modal = modal };
    }

    private void DrawGuideSection(int x, int y, string title, string body, Color accent)
    {
        _ui.Text(title, new Vector2(x, y), 17, accent);
        _ui.Line(new Vector2(x, y + 27), new Vector2(x + 350, y + 27), accent * .5f);
        _ui.Text(body, new Vector2(x, y + 38), 13, UiTheme.Ink);
    }

    private void DrawVictoryOverlay(Rectangle viewport, Point pointer, GameState game, float animationSeconds)
    {
        _hitboxes.Clear();
        _ui.Fill(viewport, new Color(0, 0, 0, 190));
        var modal = Centered(viewport, Math.Min(650, viewport.Width - 36), Math.Min(500, viewport.Height - 36));
        var victory = game.WinnerFaction == 0;
        var accent = victory ? UiTheme.Gold : UiTheme.Danger;
        var glow = .76f + MathF.Sin(animationSeconds * 2f) * .16f;
        _ui.Panel(modal, new Color(7, 16, 21, 252), accent * glow, 3);
        _ui.Text(victory ? "天下歸一" : "王朝傾覆", new Vector2(modal.Center.X, modal.Y + 43), 35, accent, TextAnchor.TopCenter);
        _ui.Text(victory ? "你的旗幟已覆蓋戰場" : "重整軍略，下一個帝國仍在等待", new Vector2(modal.Center.X, modal.Y + 94), 15, UiTheme.Ink, TextAnchor.TopCenter);
        _ui.Text($"勝負方式：{game.VictoryWay ?? "征服"}", new Vector2(modal.Center.X, modal.Y + 137), 16, UiTheme.Cyan, TextAnchor.TopCenter);

        var stats = new Rectangle(modal.X + 58, modal.Y + 182, modal.Width - 116, 112);
        _ui.Fill(stats, new Color(18, 31, 36, 232));
        _ui.Stroke(stats, UiTheme.Border);
        _ui.Text($"採集總量\n{game.Stats.Gathered:0}", new Vector2(stats.X + stats.Width / 6, stats.Y + 22), 14, UiTheme.Ink, TextAnchor.TopCenter);
        _ui.Text($"訓練單位\n{game.Stats.Trained}", new Vector2(stats.Center.X, stats.Y + 22), 14, UiTheme.Ink, TextAnchor.TopCenter);
        _ui.Text($"完成建築\n{game.Stats.Built}", new Vector2(stats.Right - stats.Width / 6, stats.Y + 22), 14, UiTheme.Ink, TextAnchor.TopCenter);
        _ui.Text($"戰局時間 {FormatClock(game.Time)}", new Vector2(stats.Center.X, stats.Bottom - 23), 12, UiTheme.Muted, TextAnchor.TopCenter);

        var buttonWidth = (modal.Width - 126) / 2;
        DrawActionButton(new Rectangle(modal.X + 58, modal.Bottom - 102, buttonWidth, 48), "匯出戰局", pointer, new UiAction(UiActionId.ExportSave), true, false, 15, UiTheme.Cyan);
        DrawActionButton(new Rectangle(modal.X + 68 + buttonWidth, modal.Bottom - 102, buttonWidth, 48), "返回主選單", pointer, new UiAction(UiActionId.ReturnToMainMenu), true, false, 15, accent);
        CurrentLayout = CurrentLayout with { Modal = modal };
    }

    private void DrawInfoChip(Rectangle bounds, string label, string value, Color accent)
    {
        _ui.Fill(bounds, new Color(17, 30, 34, 234));
        _ui.Stroke(bounds, new Color((int)accent.R, accent.G, accent.B, 130));
        _ui.Text(label, new Vector2(bounds.X + 8, bounds.Y + 5), 12, UiTheme.Muted);
        _ui.Text(value, new Vector2(bounds.Right - 8, bounds.Bottom - 7), 17, accent, TextAnchor.CenterRight);
    }

    private void DrawActionButton(
        Rectangle bounds,
        string label,
        Point pointer,
        UiAction action,
        bool enabled = true,
        bool selected = false,
        float fontSize = 14,
        Color? accent = null)
    {
        _ui.Button(bounds, label, pointer, enabled, selected, Math.Max(12f, fontSize), accent);
        AddHitbox(bounds, action, enabled);
    }

    private void AddHitbox(Rectangle bounds, UiAction action, bool enabled = true)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _hitboxes.Add(new UiHitbox(bounds, action, enabled));
        }
    }

    private void ResetFrame(Rectangle viewport)
    {
        _hitboxes.Clear();
        CurrentLayout = new UiLayoutSnapshot { Viewport = viewport };
    }

    private EntityState? FindPrimarySelected(GameState game, out int count)
    {
        EntityState? first = null;
        count = 0;
        for (var entityIndex = 0; entityIndex < game.Entities.Count; entityIndex++)
        {
            var entity = game.Entities[entityIndex];
            if (!entity.Dead && game.Selected.Contains(entity.Id))
            {
                first ??= entity;
                count++;
            }
        }
        return first;
    }

    private int FindCivilizationIndex(string? key)
    {
        for (var index = 0; index < _civilizations.Length; index++)
        {
            if (string.Equals(_civilizations[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return 0;
    }

    private int FindDifficultyIndex(string? key)
    {
        for (var index = 0; index < _difficulties.Length; index++)
        {
            if (string.Equals(_difficulties[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return Math.Min(1, _difficulties.Length - 1);
    }

    private static Rectangle CommandSlot(Rectangle area, int slot, int totalSlots)
    {
        const int gap = 6;
        var columns = Math.Min(4, totalSlots);
        var rows = Math.Max(1, (totalSlots + columns - 1) / columns);
        var width = Math.Max(1, (area.Width - gap * (columns - 1)) / columns);
        var height = Math.Max(1, (area.Height - gap * (rows - 1)) / rows);
        return new Rectangle(
            area.X + slot % columns * (width + gap),
            area.Y + slot / columns * (height + gap),
            width,
            height);
    }

    private static string EntityName(EntityState entity)
    {
        if (entity.Kind == "unit" && GameData.Units.TryGetValue(entity.Type, out var unit))
        {
            return unit.Name;
        }
        if (GameData.Buildings.TryGetValue(entity.Type, out var building))
        {
            return building.Name;
        }
        return "未知單位";
    }

    private static string EntityGlyph(EntityState entity)
    {
        if (entity.Kind == "unit" && GameData.Units.TryGetValue(entity.Type, out var unit))
        {
            return unit.Glyph;
        }
        if (GameData.Buildings.TryGetValue(entity.Type, out var building))
        {
            return building.Glyph;
        }
        return "軍";
    }

    private static string OrderLabel(EntityState entity) => entity.Order.Type switch
    {
        "move" => "正在移動",
        "attackMove" => "攻擊移動中",
        "attack" => "正在交戰",
        "gather" => entity.Carrying is null ? "正在採集" : $"運送{ResourceName(entity.Carrying)}",
        "build" => "正在施工",
        _ => entity.Kind == "building" && entity.Queue.Count > 0 ? "生產進行中" : "待命"
    };

    private static string ResourceName(string key) => key switch
    {
        "food" => "食物",
        "wood" => "木材",
        "gold" => "黃金",
        "stone" => "石材",
        _ => "資源"
    };

    private static ResourceBag AdjustedUnitCost(UnitDefinition definition, string civilization) =>
        GameRules.Cost(definition.Cost, GameRules.Modifier(GameData.Civilizations[civilization].Modifiers.UnitCost, definition));

    private static ResourceBag AdjustedBuildingCost(BuildingDefinition definition, string civilization) =>
        GameRules.Cost(definition.Cost, GameRules.Modifier(GameData.Civilizations[civilization].Modifiers.BuildingCost, definition.Key));

    private static int QueuedPopulation(GameState game, int faction)
    {
        var population = 0;
        for (var entityIndex = 0; entityIndex < game.Entities.Count; entityIndex++)
        {
            var entity = game.Entities[entityIndex];
            if (entity.Dead || entity.Faction != faction || entity.Kind != "building")
            {
                continue;
            }
            for (var queueIndex = 0; queueIndex < entity.Queue.Count; queueIndex++)
            {
                population += GameData.Units.GetValueOrDefault(entity.Queue[queueIndex].Type)?.Population ?? 0;
            }
        }
        return population;
    }

    private static bool MeetsPrerequisites(GameState game, string buildingKey)
    {
        if (!GameData.BuildingPrerequisites.TryGetValue(buildingKey, out var required) || required.Count == 0)
        {
            return true;
        }

        for (var requirementIndex = 0; requirementIndex < required.Count; requirementIndex++)
        {
            var found = false;
            for (var entityIndex = 0; entityIndex < game.Entities.Count; entityIndex++)
            {
                var entity = game.Entities[entityIndex];
                if (!entity.Dead && entity.Faction == 0 && entity.Type == required[requirementIndex] && entity.Construction >= 1)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsExplored(GameState game, double worldX, double worldY)
    {
        if (game.RevealUntil > game.Time)
        {
            return true;
        }
        var index = FogIndex(worldX, worldY);
        return index >= 0 && index < game.Fog.Count && game.Fog[index] > 0;
    }

    private static bool IsVisible(GameState game, double worldX, double worldY)
    {
        if (game.RevealUntil > game.Time)
        {
            return true;
        }
        var index = FogIndex(worldX, worldY);
        return index >= 0 && index < game.Fog.Count && game.Fog[index] == 2;
    }

    private static int FogIndex(double worldX, double worldY)
    {
        var tileX = Math.Clamp((int)(worldX / GameConstants.TileSize), 0, GameConstants.MapWidth - 1);
        var tileY = Math.Clamp((int)(worldY / GameConstants.TileSize), 0, GameConstants.MapHeight - 1);
        return tileY * GameConstants.MapWidth + tileX;
    }

    private static Point MiniPoint(Rectangle map, double worldX, double worldY) => new(
        map.X + Math.Clamp((int)(worldX / GameConstants.WorldWidth * map.Width), 0, map.Width - 1),
        map.Y + Math.Clamp((int)(worldY / GameConstants.WorldHeight * map.Height), 0, map.Height - 1));

    private static Rectangle Centered(Rectangle outer, int width, int height) =>
        new(outer.Center.X - width / 2, outer.Center.Y - height / 2, width, height);

    private static string ResourceText(double value) => Math.Max(0, Math.Floor(value)).ToString("0", CultureInfo.InvariantCulture);

    private static string Shorten(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : string.Concat(value.AsSpan(0, maximumCharacters - 1), "…");

    private static string FormatClock(double seconds)
    {
        var wholeSeconds = Math.Max(0, (int)Math.Floor(seconds));
        return $"{wholeSeconds / 60:00}:{wholeSeconds % 60:00}";
    }

    private static string LocalizeTutorialText(string text) => text
        .Replace("WASD", "鍵盤移動鍵", StringComparison.Ordinal)
        .Replace("Shift", "追加鍵", StringComparison.Ordinal)
        .Replace("JSON", "通用文字格式", StringComparison.Ordinal)
        .Replace("2D", "二維", StringComparison.Ordinal);

    private static Color ParseHexColor(string value, Color fallback)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return fallback;
        }
        if (byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
            byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
            byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return new Color(red, green, blue);
        }
        return fallback;
    }

    private void DrawCover(Texture2D texture, Rectangle destination, Color tint)
    {
        var scale = Math.Max(destination.Width / (double)texture.Width, destination.Height / (double)texture.Height);
        var sourceWidth = Math.Max(1, (int)Math.Round(destination.Width / scale));
        var sourceHeight = Math.Max(1, (int)Math.Round(destination.Height / scale));
        var source = new Rectangle(
            Math.Max(0, (texture.Width - sourceWidth) / 2),
            Math.Max(0, (texture.Height - sourceHeight) / 2),
            Math.Min(texture.Width, sourceWidth),
            Math.Min(texture.Height, sourceHeight));
        _batch.Draw(texture, destination, source, tint);
    }
}
