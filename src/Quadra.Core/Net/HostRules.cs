namespace Quadra.Core.Net;

/// <summary>What the host announces and enforces; the fields of the game packet that do not change during a game.</summary>
public sealed record HostRules
{
    public string Name { get; init; } = "Quadra";
    public int NetVersion { get; init; } = PacketEncoder.CurrentVersion;
    public int Seed { get; init; }
    public byte LevelStart { get; init; } = 1;
    public bool NoLevelUp { get; init; }
    public bool Survivor { get; init; }
    public byte ComboMin { get; init; }
    public bool AllowHandicap { get; init; } = true;
    public int MaxPlayers { get; init; } = PacketDecoder.MaxPlayers;
    public GameEnd GameEnd { get; init; }
    public int GameEndValue { get; init; }
}
