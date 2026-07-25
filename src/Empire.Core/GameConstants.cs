namespace Empire.Core;

public static class GameConstants
{
    public const int TileSize = 48;
    public const int MapWidth = 58;
    public const int MapHeight = 42;
    public const int WorldWidth = MapWidth * TileSize;
    public const int WorldHeight = MapHeight * TileSize;
    public const int MaxPopulation = 80;
    public const double FixedStep = 1d / 30d;
    public const string Projection = "topdown-v1";
    public const string GameVersion = "4.0.0-native";

    public static readonly string[] Ages = ["黑暗時代", "封建時代", "城堡時代", "帝王時代"];
    public static readonly string[] ResourceKeys = ["food", "wood", "gold", "stone"];
    public static readonly string[] FactionColors = ["#5bc5d8", "#ec645b", "#a77be8", "#e5b955"];
}
