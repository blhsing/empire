#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontStashSharp;

namespace Empire.Game.Platform;

/// <summary>
/// Loads a system font with Traditional Chinese coverage and supplies dynamically
/// cached MonoGame fonts. Construct and use this service on the game/render thread.
/// </summary>
public sealed class TraditionalChineseFontService : IDisposable
{
    public const float MinimumFontSize = 12f;
    public const string FontOverrideEnvironmentVariable = "EMPIRE_TRADITIONAL_CHINESE_FONT";

    private static readonly string[] PreferredFileNames =
    [
        // Bundled/current Noto variable fonts are ordinary TrueType files and work
        // with the stb rasterizer. Font collections (.ttc) are intentionally not
        // listed because stb cannot reliably select a face from them.
        "NotoSansTC-VF.ttf",
        "NotoSansHK-VF.ttf",
        "NotoSansTC-Regular.ttf",
        "NotoSansTC-Regular.otf",
        "NotoSansCJKtc-Regular.otf",
        "NotoSansCJK-TC-Regular.otf",

        // Windows: single-face Traditional Chinese TrueType fallbacks.
        "msjh.ttf",
        "kaiu.ttf",

        // macOS: prefer a single-face OpenType font when installed.
        "PingFangTC-Regular.otf",
    ];

    private static readonly string[] UnixFontDirectories =
    [
        "/System/Library/Fonts",
        "/Library/Fonts",
        "/usr/share/fonts",
        "/usr/local/share/fonts"
    ];

    private readonly FontSystem _fontSystem;
    private bool _disposed;

    /// <summary>The resolved system font file used by this service.</summary>
    public string FontPath { get; }

    /// <summary>
    /// Resolves and loads a Traditional Chinese font. <paramref name="preferredFontPath"/>
    /// has priority, followed by EMPIRE_TRADITIONAL_CHINESE_FONT and known system fonts.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// No suitable font exists, or an explicit override points at a missing file.
    /// </exception>
    public TraditionalChineseFontService(string? preferredFontPath = null)
    {
        FontPath = ResolveFontPath(preferredFontPath);

        _fontSystem = new FontSystem(new FontSystemSettings
        {
            // A larger atlas avoids frequent atlas switches for the broad CJK glyph set.
            TextureWidth = 2048,
            TextureHeight = 2048,
            FontResolutionFactor = 1f
        });

        try
        {
            _fontSystem.AddFont(File.ReadAllBytes(FontPath));
        }
        catch (Exception exception)
        {
            _fontSystem.Dispose();
            throw new InvalidOperationException(
                $"無法載入繁體中文字型「{FontPath}」。請以 {FontOverrideEnvironmentVariable} 指定可讀取的單一 .ttf 或 .otf 字型檔。",
                exception);
        }
    }

    /// <summary>
    /// Gets a dynamic font. Values below 12px are intentionally clamped to 12px.
    /// </summary>
    public DynamicSpriteFont GetFont(float pixelSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NaN cannot pass through MathF.Max safely, so treat it as the UI minimum.
        var safeSize = float.IsFinite(pixelSize) ? pixelSize : MinimumFontSize;
        return _fontSystem.GetFont(MathF.Max(MinimumFontSize, safeSize));
    }

    /// <summary>
    /// Gets a dynamic font. Values below 12px are intentionally clamped to 12px.
    /// </summary>
    public DynamicSpriteFont GetFont(int pixelSize) => GetFont((float)pixelSize);

    /// <summary>
    /// Attempts to construct the service while returning a user-facing diagnostic instead
    /// of throwing. This is useful for an error screen during game startup.
    /// </summary>
    public static bool TryCreate(
        out TraditionalChineseFontService? service,
        out string? error,
        string? preferredFontPath = null)
    {
        try
        {
            service = new TraditionalChineseFontService(preferredFontPath);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException)
        {
            service = null;
            error = exception.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fontSystem.Dispose();
    }

    private static string ResolveFontPath(string? preferredFontPath)
    {
        var explicitPath = string.IsNullOrWhiteSpace(preferredFontPath)
            ? Environment.GetEnvironmentVariable(FontOverrideEnvironmentVariable)
            : preferredFontPath;

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(explicitPath.Trim());
            var fullPath = Path.GetFullPath(expandedPath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            throw new FileNotFoundException(
                $"指定的繁體中文字型不存在：「{fullPath}」。請修正路徑或移除 {FontOverrideEnvironmentVariable}。",
                fullPath);
        }

        // Native releases carry Noto Sans TC so the game remains self-contained
        // and its Traditional Chinese UI never depends on a particular OS image.
        var bundledFontPath = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "fonts",
            "NotoSansTC-VF.ttf");
        if (File.Exists(bundledFontPath))
        {
            return Path.GetFullPath(bundledFontPath);
        }

        var searchedDirectories = new List<string>(UnixFontDirectories.Length + 2);
        var systemFontDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(systemFontDirectory))
        {
            searchedDirectories.Add(systemFontDirectory);
        }

        for (var i = 0; i < UnixFontDirectories.Length; i++)
        {
            if (!searchedDirectories.Contains(UnixFontDirectories[i], StringComparer.OrdinalIgnoreCase))
            {
                searchedDirectories.Add(UnixFontDirectories[i]);
            }
        }

        // Fast path for normal system installations.
        for (var directoryIndex = 0; directoryIndex < searchedDirectories.Count; directoryIndex++)
        {
            var directory = searchedDirectories[directoryIndex];
            for (var nameIndex = 0; nameIndex < PreferredFileNames.Length; nameIndex++)
            {
                var candidate = Path.Combine(directory, PreferredFileNames[nameIndex]);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        // Linux distributions commonly nest Noto under truetype/ or opentype/.
        for (var directoryIndex = 0; directoryIndex < searchedDirectories.Count; directoryIndex++)
        {
            var found = FindPreferredFontBelow(searchedDirectories[directoryIndex]);
            if (found is not null)
            {
                return found;
            }
        }

        var locations = string.Join("、", searchedDirectories.Where(Directory.Exists));
        throw new FileNotFoundException(
            "找不到支援繁體中文的 Microsoft JhengHei、PingFang TC 或 Noto Sans TC 字型。" +
            $"已搜尋：{(locations.Length == 0 ? "系統字型目錄不可用" : locations)}。" +
            $"請安裝其中一款字型，或以 {FontOverrideEnvironmentVariable} 指定字型檔。"
        );
    }

    private static string? FindPreferredFontBelow(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var candidates = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
            foreach (var candidate in candidates)
            {
                var fileName = Path.GetFileName(candidate);
                for (var nameIndex = 0; nameIndex < PreferredFileNames.Length; nameIndex++)
                {
                    if (string.Equals(fileName, PreferredFileNames[nameIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Other readable font roots are still valid fallbacks.
        }

        return null;
    }
}
