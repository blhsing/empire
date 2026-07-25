using Empire.Game.Platform;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Empire.Game.Ui;

public enum TextAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    Center,
    CenterLeft,
    CenterRight
}

public static class UiTheme
{
    public static readonly Color Ink = new(235, 226, 196);
    public static readonly Color Muted = new(166, 177, 174);
    public static readonly Color Gold = new(228, 193, 94);
    public static readonly Color Cyan = new(91, 197, 216);
    public static readonly Color Danger = new(236, 100, 91);
    public static readonly Color Good = new(104, 201, 137);
    public static readonly Color Panel = new(10, 19, 24, 232);
    public static readonly Color PanelSoft = new(18, 31, 37, 218);
    public static readonly Color Border = new(101, 119, 111, 178);
    public static readonly Color Shadow = new(0, 0, 0, 180);
}

/// <summary>
/// Allocation-light immediate UI helpers. Every text entry passes through the
/// font service, which enforces the game's 12px minimum.
/// </summary>
public sealed class UiToolkit
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;
    private readonly TraditionalChineseFontService _fonts;

    public UiToolkit(SpriteBatch batch, Texture2D pixel, TraditionalChineseFontService fonts)
    {
        _batch = batch;
        _pixel = pixel;
        _fonts = fonts;
    }

    public void Fill(Rectangle bounds, Color color)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _batch.Draw(_pixel, bounds, color);
        }
    }

    public void Panel(Rectangle bounds, Color? fill = null, Color? border = null, int borderWidth = 1)
    {
        Fill(new Rectangle(bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height), UiTheme.Shadow * .55f);
        Fill(bounds, fill ?? UiTheme.Panel);
        Stroke(bounds, border ?? UiTheme.Border, borderWidth);
    }

    public void Stroke(Rectangle bounds, Color color, int width = 1)
    {
        var thickness = Math.Max(1, width);
        Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
        Fill(new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
        Fill(new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
        Fill(new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
    }

    public bool Button(
        Rectangle bounds,
        string label,
        Point mouse,
        bool enabled = true,
        bool selected = false,
        float fontSize = 16,
        Color? accent = null)
    {
        var hovered = enabled && bounds.Contains(mouse);
        var line = accent ?? UiTheme.Gold;
        var fill = selected
            ? new Color(line.R, line.G, line.B, (byte)72)
            : hovered ? new Color(48, 67, 69, 244) : new Color(23, 38, 43, 238);
        Fill(bounds, enabled ? fill : new Color(22, 28, 31, 190));
        Stroke(bounds, enabled ? (hovered || selected ? line : UiTheme.Border) : UiTheme.Border * .5f, selected ? 2 : 1);
        Text(label, new Vector2(bounds.Center.X, bounds.Center.Y), fontSize, enabled ? UiTheme.Ink : UiTheme.Muted * .65f, TextAnchor.Center);
        return hovered;
    }

    public Vector2 Measure(string text, float fontSize = 16)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Vector2.Zero;
        }
        return _fonts.GetFont(fontSize).MeasureString(text);
    }

    public void Text(
        string text,
        Vector2 position,
        float fontSize = 16,
        Color? color = null,
        TextAnchor anchor = TextAnchor.TopLeft,
        float rotation = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var font = _fonts.GetFont(fontSize);
        var size = font.MeasureString(text);
        var origin = anchor switch
        {
            TextAnchor.TopCenter => new Vector2(size.X * .5f, 0),
            TextAnchor.TopRight => new Vector2(size.X, 0),
            TextAnchor.Center => size * .5f,
            TextAnchor.CenterLeft => new Vector2(0, size.Y * .5f),
            TextAnchor.CenterRight => new Vector2(size.X, size.Y * .5f),
            _ => Vector2.Zero
        };
        font.DrawText(_batch, text, position, color ?? UiTheme.Ink, rotation: rotation, origin: origin);
    }

    public void TextShadowed(
        string text,
        Vector2 position,
        float fontSize = 16,
        Color? color = null,
        TextAnchor anchor = TextAnchor.TopLeft)
    {
        Text(text, position + new Vector2(2, 2), fontSize, Color.Black * .8f, anchor);
        Text(text, position, fontSize, color ?? UiTheme.Ink, anchor);
    }

    public void Progress(Rectangle bounds, float ratio, Color color, Color? background = null)
    {
        var clamped = Math.Clamp(ratio, 0, 1);
        Fill(bounds, background ?? new Color(0, 0, 0, 170));
        var inner = new Rectangle(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));
        Fill(new Rectangle(inner.X, inner.Y, (int)MathF.Round(inner.Width * clamped), inner.Height), color);
        Stroke(bounds, Color.Black * .75f);
    }

    public void Line(Vector2 start, Vector2 end, Color color, float thickness = 1)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= .01f)
        {
            return;
        }
        _batch.Draw(_pixel, start, null, color, MathF.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(length, Math.Max(1, thickness)), SpriteEffects.None, 0);
    }

    public string Wrap(string text, float fontSize, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
        {
            return text;
        }

        var font = _fonts.GetFont(fontSize);
        var builder = new System.Text.StringBuilder(text.Length + 16);
        var lineWidth = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var part = rune.ToString();
            if (part == "\n")
            {
                builder.Append('\n');
                lineWidth = 0;
                continue;
            }
            var width = font.MeasureString(part).X;
            if (lineWidth > 0 && lineWidth + width > maxWidth)
            {
                builder.Append('\n');
                lineWidth = 0;
            }
            builder.Append(part);
            lineWidth += width;
        }
        return builder.ToString();
    }
}
