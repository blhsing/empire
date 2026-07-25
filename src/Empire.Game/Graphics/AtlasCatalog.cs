using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;

namespace Empire.Game.Graphics;

/// <summary>
/// Identifies the seven runtime sprite atlases. Source/master artwork is
/// deliberately absent: it is preserved in the repository but is not loaded by
/// the game.
/// </summary>
public enum AtlasId
{
    UnitsCommon,
    UnitsUniqueA,
    UnitsUniqueB,
    BuildingsCommon,
    BuildingsAdvanced,
    Environment,
    EffectsUi
}

/// <summary>
/// Describes how an atlas must be composited. The effects atlas has an opaque
/// black background, so it must not be drawn with ordinary alpha blending.
/// </summary>
public enum SpriteBlendMode
{
    Alpha,
    Additive
}

public sealed record AtlasSpec(
    AtlasId Id,
    string RelativePath,
    int Columns,
    int Rows,
    SpriteBlendMode BlendMode = SpriteBlendMode.Alpha,
    int InsetPixels = 2)
{
    /// <summary>
    /// Reproduces the browser atlas calculation exactly: proportional cell
    /// edges are rounded like JavaScript Math.round and then inset by two
    /// pixels to prevent bilinear sampling from a neighbouring cell.
    /// </summary>
    public Rectangle GetSourceRectangle(int column, int row, int textureWidth, int textureHeight)
    {
        if ((uint)column >= (uint)Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        if ((uint)row >= (uint)Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureHeight);

        var x0 = JavaScriptRound(column * (double)textureWidth / Columns);
        var x1 = JavaScriptRound((column + 1) * (double)textureWidth / Columns);
        var y0 = JavaScriptRound(row * (double)textureHeight / Rows);
        var y1 = JavaScriptRound((row + 1) * (double)textureHeight / Rows);

        return new Rectangle(
            x0 + InsetPixels,
            y0 + InsetPixels,
            Math.Max(1, x1 - x0 - InsetPixels * 2),
            Math.Max(1, y1 - y0 - InsetPixels * 2));
    }

    private static int JavaScriptRound(double value) => (int)Math.Floor(value + 0.5d);
}

/// <summary>
/// Exact native counterpart of js/generated-art.js. Keys intentionally retain
/// their JavaScript spelling so save data and simulation identifiers need no
/// translation.
/// </summary>
public static class AtlasCatalog
{
    public const string MenuBackgroundPath = "assets/empire-dawn.jpg";
    public const string EmpireIconPath = "assets/empire-icon.png";
    public const string MaterialTerrainAtlasPath = "assets/isometric-material-atlas.jpg";
    public const string MedievalTerrainAtlasPath = "assets/medieval-terrain-atlas-v2.png";

    public static IReadOnlyDictionary<AtlasId, AtlasSpec> Atlases { get; } =
        ReadOnly(new Dictionary<AtlasId, AtlasSpec>
        {
            [AtlasId.UnitsCommon] = new(AtlasId.UnitsCommon, "assets/generated/units-common.png", 3, 3),
            [AtlasId.UnitsUniqueA] = new(AtlasId.UnitsUniqueA, "assets/generated/units-unique-a.png", 4, 2),
            [AtlasId.UnitsUniqueB] = new(AtlasId.UnitsUniqueB, "assets/generated/units-unique-b.png", 3, 2),
            [AtlasId.BuildingsCommon] = new(AtlasId.BuildingsCommon, "assets/generated/buildings-common.png", 4, 2),
            [AtlasId.BuildingsAdvanced] = new(AtlasId.BuildingsAdvanced, "assets/generated/buildings-advanced.png", 3, 2),
            [AtlasId.Environment] = new(AtlasId.Environment, "assets/generated/environment.png", 4, 2),
            [AtlasId.EffectsUi] = new(AtlasId.EffectsUi, "assets/generated/effects-ui.png", 4, 4, SpriteBlendMode.Additive)
        });

    public static IReadOnlyDictionary<string, SpriteSpec> Units { get; } =
        ReadOnly(new Dictionary<string, SpriteSpec>(StringComparer.Ordinal)
        {
            ["villager"] = new(AtlasId.UnitsCommon, 0, 0, 70),
            ["scout"] = new(AtlasId.UnitsCommon, 1, 0, 84),
            ["swordsman"] = new(AtlasId.UnitsCommon, 2, 0, 70),
            ["spear"] = new(AtlasId.UnitsCommon, 0, 1, 74),
            ["archer"] = new(AtlasId.UnitsCommon, 1, 1, 72),
            ["cavalry"] = new(AtlasId.UnitsCommon, 2, 1, 86),
            ["crossbow"] = new(AtlasId.UnitsCommon, 0, 2, 72),
            ["ram"] = new(AtlasId.UnitsCommon, 1, 2, 82),
            ["catapult"] = new(AtlasId.UnitsCommon, 2, 2, 82),

            ["longbowman"] = new(AtlasId.UnitsUniqueA, 0, 0, 88),
            ["cataphract"] = new(AtlasId.UnitsUniqueA, 1, 0, 94),
            ["woadRaider"] = new(AtlasId.UnitsUniqueA, 2, 0, 84),
            ["chuKoNu"] = new(AtlasId.UnitsUniqueA, 3, 0, 86),
            ["throwingAxeman"] = new(AtlasId.UnitsUniqueA, 0, 1, 84),
            ["huskarl"] = new(AtlasId.UnitsUniqueA, 1, 1, 84),
            ["samurai"] = new(AtlasId.UnitsUniqueA, 2, 1, 88),

            ["mangudai"] = new(AtlasId.UnitsUniqueB, 0, 0, 96),
            ["warElephant"] = new(AtlasId.UnitsUniqueB, 1, 0, 112),
            ["mameluke"] = new(AtlasId.UnitsUniqueB, 2, 0, 98),
            ["teutonicKnight"] = new(AtlasId.UnitsUniqueB, 0, 1, 90),
            ["janissary"] = new(AtlasId.UnitsUniqueB, 1, 1, 90),
            ["berserk"] = new(AtlasId.UnitsUniqueB, 2, 1, 88)
        });

    public static IReadOnlyDictionary<string, SpriteSpec> Buildings { get; } =
        ReadOnly(new Dictionary<string, SpriteSpec>(StringComparer.Ordinal)
        {
            ["town"] = new(AtlasId.BuildingsCommon, 0, 0, 144),
            ["house"] = new(AtlasId.BuildingsCommon, 1, 0, 110),
            ["mill"] = new(AtlasId.BuildingsCommon, 2, 0, 110),
            ["lumber"] = new(AtlasId.BuildingsCommon, 3, 0, 98),
            ["farm"] = new(AtlasId.BuildingsCommon, 0, 1, 88),
            ["barracks"] = new(AtlasId.BuildingsCommon, 1, 1, 116),
            ["blacksmith"] = new(AtlasId.BuildingsCommon, 2, 1, 112),
            ["range"] = new(AtlasId.BuildingsCommon, 3, 1, 102),
            ["stable"] = new(AtlasId.BuildingsAdvanced, 0, 0, 112),
            ["tower"] = new(AtlasId.BuildingsAdvanced, 1, 0, 120),
            ["wall"] = new(AtlasId.BuildingsAdvanced, 2, 0, 76),
            ["castle"] = new(AtlasId.BuildingsAdvanced, 0, 1, 152),
            ["workshop"] = new(AtlasId.BuildingsAdvanced, 1, 1, 130),
            ["wonder"] = new(AtlasId.BuildingsAdvanced, 2, 1, 160)
        });

    public static IReadOnlyDictionary<string, SpriteSpec> Environment { get; } =
        ReadOnly(new Dictionary<string, SpriteSpec>(StringComparer.Ordinal)
        {
            ["oak"] = new(AtlasId.Environment, 0, 0, 96),
            ["pine"] = new(AtlasId.Environment, 1, 0, 100),
            ["food"] = new(AtlasId.Environment, 2, 0, 70),
            ["gold"] = new(AtlasId.Environment, 3, 0, 76),
            ["stone"] = new(AtlasId.Environment, 0, 1, 76),
            ["site"] = new(AtlasId.Environment, 1, 1, 92),
            ["construction"] = new(AtlasId.Environment, 2, 1, 112),
            ["campfire"] = new(AtlasId.Environment, 3, 1, 74)
        });

    public static IReadOnlyDictionary<string, SpriteSpec> Effects { get; } =
        ReadOnly(new Dictionary<string, SpriteSpec>(StringComparer.Ordinal)
        {
            ["swordSlash"] = new(AtlasId.EffectsUi, 0, 0, 72),
            ["arrowImpact"] = new(AtlasId.EffectsUi, 1, 0, 72),
            ["dust"] = new(AtlasId.EffectsUi, 2, 0, 72),
            ["siegeExplosion"] = new(AtlasId.EffectsUi, 3, 0, 84),
            ["embers"] = new(AtlasId.EffectsUi, 0, 1, 72),
            ["healAura"] = new(AtlasId.EffectsUi, 1, 1, 72),
            ["selectionRing"] = new(AtlasId.EffectsUi, 2, 1, 72),
            ["waterRipple"] = new(AtlasId.EffectsUi, 3, 1, 72),
            ["foodIcon"] = new(AtlasId.EffectsUi, 0, 2, 48),
            ["woodIcon"] = new(AtlasId.EffectsUi, 1, 2, 48),
            ["goldIcon"] = new(AtlasId.EffectsUi, 2, 2, 48),
            ["stoneIcon"] = new(AtlasId.EffectsUi, 3, 2, 48),
            ["houseIcon"] = new(AtlasId.EffectsUi, 0, 3, 48),
            ["castleIcon"] = new(AtlasId.EffectsUi, 1, 3, 48),
            ["ageIcon"] = new(AtlasId.EffectsUi, 2, 3, 48),
            ["powerIcon"] = new(AtlasId.EffectsUi, 3, 3, 48)
        });

    public static AtlasSpec GetAtlas(AtlasId id) => Atlases[id];

    private static IReadOnlyDictionary<TKey, TValue> ReadOnly<TKey, TValue>(Dictionary<TKey, TValue> values)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(values);
}
