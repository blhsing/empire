namespace Empire.Core;

public sealed record NewGameOptions
{
    public string Civilization { get; init; } = "britons";
    public string Difficulty { get; init; } = "征戰";
    public int PlayerCount { get; init; } = 2;
    public int Seed { get; init; } = Environment.TickCount;
    public bool Tutorial { get; init; }
}
