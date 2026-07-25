using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Empire.Game.Graphics;

public readonly record struct SpriteSpec(
    AtlasId Atlas,
    int Column,
    int Row,
    int DisplayHeight)
{
    public Rectangle GetSourceRectangle(Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return AtlasCatalog.GetAtlas(Atlas).GetSourceRectangle(Column, Row, texture.Width, texture.Height);
    }

    public Vector2 GetDisplaySize(Rectangle sourceRectangle)
    {
        var width = DisplayHeight * sourceRectangle.Width / (float)sourceRectangle.Height;
        return new Vector2(width, DisplayHeight);
    }
}

/// <summary>
/// A resolved atlas sprite ready for SpriteBatch.Draw.
/// </summary>
public readonly record struct SpriteAsset(
    Texture2D Texture,
    Rectangle SourceRectangle,
    Vector2 DisplaySize,
    SpriteBlendMode BlendMode)
{
    public bool RequiresAdditiveBlend => BlendMode == SpriteBlendMode.Additive;
}
