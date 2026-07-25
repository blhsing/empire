using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Empire.Game.Graphics;

/// <summary>
/// Loads the repository's runtime artwork directly from the copied assets
/// directory. No Content Pipeline conversion or web server is required.
/// </summary>
public sealed class GameAssets : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<AtlasId, Texture2D> _atlasTextures = [];
    private readonly Dictionary<string, SpriteAsset> _unitSprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteAsset> _buildingSprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteAsset> _environmentSprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteAsset> _effectSprites = new(StringComparer.Ordinal);
    private readonly HashSet<Texture2D> _ownedTextures = [];

    private Texture2D? _menuBackground;
    private Texture2D? _empireIcon;
    private Texture2D? _materialTerrainAtlas;
    private Texture2D? _medievalTerrainAtlas;
    private Texture2D? _whitePixel;
    private bool _disposed;

    public GameAssets(GraphicsDevice graphicsDevice, string? assetRoot = null)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        AssetRoot = Path.GetFullPath(assetRoot ?? Path.Combine(AppContext.BaseDirectory, "assets"));
    }

    public string AssetRoot { get; }
    public bool IsLoaded { get; private set; }

    public Texture2D MenuBackground => RequireLoaded(_menuBackground, nameof(MenuBackground));
    public Texture2D EmpireIcon => RequireLoaded(_empireIcon, nameof(EmpireIcon));
    public Texture2D MaterialTerrainAtlas => RequireLoaded(_materialTerrainAtlas, nameof(MaterialTerrainAtlas));
    public Texture2D MedievalTerrainAtlas => RequireLoaded(_medievalTerrainAtlas, nameof(MedievalTerrainAtlas));

    /// <summary>A one-pixel white texture for efficient rectangles, lines and tint overlays.</summary>
    public Texture2D WhitePixel => RequireLoaded(_whitePixel, nameof(WhitePixel));

    public IReadOnlyDictionary<string, SpriteAsset> UnitSprites
    {
        get { EnsureLoaded(); return _unitSprites; }
    }

    public IReadOnlyDictionary<string, SpriteAsset> BuildingSprites
    {
        get { EnsureLoaded(); return _buildingSprites; }
    }

    public IReadOnlyDictionary<string, SpriteAsset> EnvironmentSprites
    {
        get { EnsureLoaded(); return _environmentSprites; }
    }

    public IReadOnlyDictionary<string, SpriteAsset> EffectSprites
    {
        get { EnsureLoaded(); return _effectSprites; }
    }

    public void Load()
    {
        ThrowIfDisposed();
        if (IsLoaded)
        {
            return;
        }

        try
        {
            _menuBackground = LoadTexture(AtlasCatalog.MenuBackgroundPath);
            _empireIcon = LoadTexture(AtlasCatalog.EmpireIconPath);
            _materialTerrainAtlas = LoadTexture(AtlasCatalog.MaterialTerrainAtlasPath);
            _medievalTerrainAtlas = LoadTexture(AtlasCatalog.MedievalTerrainAtlasPath);

            foreach (var atlas in AtlasCatalog.Atlases.Values)
            {
                _atlasTextures.Add(atlas.Id, LoadTexture(atlas.RelativePath));
            }

            _whitePixel = new Texture2D(_graphicsDevice, 1, 1, false, SurfaceFormat.Color);
            _whitePixel.SetData(new[] { Color.White });
            _ownedTextures.Add(_whitePixel);

            ResolveSprites(AtlasCatalog.Units, _unitSprites);
            ResolveSprites(AtlasCatalog.Buildings, _buildingSprites);
            ResolveSprites(AtlasCatalog.Environment, _environmentSprites);
            ResolveSprites(AtlasCatalog.Effects, _effectSprites);
            IsLoaded = true;
        }
        catch
        {
            ReleaseTextures();
            throw;
        }
    }

    public Texture2D GetAtlasTexture(AtlasId atlas)
    {
        EnsureLoaded();
        return _atlasTextures[atlas];
    }

    public SpriteAsset GetUnitSprite(string type) => GetSprite(_unitSprites, type, "unit");
    public SpriteAsset GetBuildingSprite(string type) => GetSprite(_buildingSprites, type, "building");
    public SpriteAsset GetEnvironmentSprite(string type) => GetSprite(_environmentSprites, type, "environment");
    public SpriteAsset GetEffectSprite(string type) => GetSprite(_effectSprites, type, "effect");

    public bool TryGetUnitSprite(string type, out SpriteAsset sprite)
    {
        EnsureLoaded();
        return _unitSprites.TryGetValue(type, out sprite);
    }

    public bool TryGetBuildingSprite(string type, out SpriteAsset sprite)
    {
        EnsureLoaded();
        return _buildingSprites.TryGetValue(type, out sprite);
    }

    public bool TryGetEnvironmentSprite(string type, out SpriteAsset sprite)
    {
        EnsureLoaded();
        return _environmentSprites.TryGetValue(type, out sprite);
    }

    public bool TryGetEffectSprite(string type, out SpriteAsset sprite)
    {
        EnsureLoaded();
        return _effectSprites.TryGetValue(type, out sprite);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseTextures();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private Texture2D LoadTexture(string catalogPath)
    {
        // Catalog paths start with "assets/" while AssetRoot already points to
        // that directory.
        const string assetPrefix = "assets/";
        var relativePath = catalogPath.StartsWith(assetPrefix, StringComparison.Ordinal)
            ? catalogPath[assetPrefix.Length..]
            : catalogPath;
        var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(AssetRoot, platformPath));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"找不到遊戲圖像資產：{catalogPath}", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        texture.Name = catalogPath;
        _ownedTextures.Add(texture);
        return texture;
    }

    private void ResolveSprites(
        IReadOnlyDictionary<string, SpriteSpec> catalog,
        Dictionary<string, SpriteAsset> destination)
    {
        foreach (var (key, spec) in catalog)
        {
            var texture = _atlasTextures[spec.Atlas];
            var source = spec.GetSourceRectangle(texture);
            var atlas = AtlasCatalog.GetAtlas(spec.Atlas);
            destination.Add(key, new SpriteAsset(texture, source, spec.GetDisplaySize(source), atlas.BlendMode));
        }
    }

    private SpriteAsset GetSprite(Dictionary<string, SpriteAsset> sprites, string type, string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        EnsureLoaded();
        return sprites.TryGetValue(type, out var sprite)
            ? sprite
            : throw new KeyNotFoundException($"Unknown {category} sprite: {type}");
    }

    private Texture2D RequireLoaded(Texture2D? texture, string property)
    {
        EnsureLoaded();
        return texture ?? throw new InvalidOperationException($"Asset {property} was not loaded.");
    }

    private void EnsureLoaded()
    {
        ThrowIfDisposed();
        if (!IsLoaded)
        {
            throw new InvalidOperationException("GameAssets.Load must be called before accessing textures.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ReleaseTextures()
    {
        foreach (var texture in _ownedTextures)
        {
            texture.Dispose();
        }

        _ownedTextures.Clear();
        _atlasTextures.Clear();
        _unitSprites.Clear();
        _buildingSprites.Clear();
        _environmentSprites.Clear();
        _effectSprites.Clear();
        _menuBackground = null;
        _empireIcon = null;
        _materialTerrainAtlas = null;
        _medievalTerrainAtlas = null;
        _whitePixel = null;
        IsLoaded = false;
    }
}
