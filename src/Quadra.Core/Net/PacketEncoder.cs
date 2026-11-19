namespace Quadra.Core.Net;

/// <summary>
/// Writes packets as <see cref="PacketDecoder"/> reads them: the <c>write</c> side of source/packets.cc.
/// The packet's <see cref="Packet.Id"/> is the first byte, so a caller building a packet sets it
/// (see <see cref="PacketType"/>). Fields that only exist from some net version on are written only
/// when <c>netVersion</c> is high enough, so a 1.1 peer gets the shorter form it expects.
/// </summary>
public static class PacketEncoder
{
    /// <summary>Net version of the 1.1 rules this port plays.</summary>
    public const int CurrentVersion = 20;

    public static byte[] Encode(Packet packet, int netVersion = CurrentVersion)
    {
        var w = new NetWriter();
        w.WriteByte(packet.Id);

        switch (packet)
        {
            case RawPacket raw:
                w.WriteBytes(raw.Payload);
                break;
            case ChatPacket p:
                w.WriteByte(unchecked((byte)p.Team));
                w.WriteString(p.Text, 256);
                w.WriteByte(unchecked((byte)p.ToTeam));
                break;
            case GameServerPacket p:
                WriteGameServer(w, p);
                break;
            case PlayerPacket p:
                WritePlayer(w, p, netVersion);
                break;
            case PausePacket p:
                w.WriteByte(unchecked((byte)p.Player));
                break;
            case StampBlockPacket p:
                w.WriteByte(p.Slot);
                w.WriteByte(p.X);
                w.WriteByte(p.Y);
                w.WriteByte(p.Rotate);
                w.WriteByte(p.Score);
                w.WriteWord(p.Date);
                w.WriteByte(p.BlockRotated);
                w.WriteWord(p.TimeHeld);
                break;
            case DeadPacket p:
                w.WriteByte(p.Slot);
                w.WriteBool(p.ThenGone);
                break;
            case GonePacket p:
                w.WriteByte(p.Slot);
                w.WriteBool(!p.ChatMsg);
                break;
            case LinesPacket p:
                WriteLines(w, p);
                break;
            case MovesPacket p:
                w.WriteByte(p.Slot);
                w.WriteByte((byte)p.Moves.Length);
                w.WriteBytes(p.Moves);
                break;
            case StatPacket p:
                w.WriteByte(p.Slot);
                w.WriteByte((byte)p.Stats.Count);
                foreach (var s in p.Stats)
                {
                    w.WriteByte(s.Stat);
                    w.WriteInt(s.Value);
                }
                break;
            case DownloadPacket p:
                WriteDownload(w, p, netVersion);
                break;
            case StatePacket p:
                w.WriteByte(p.Slot);
                w.WriteByte(p.State);
                break;
            case WantJoinPacket p:
                w.WriteDword(p.Uid);
                w.WriteByte(p.Current ? (byte)1 : (byte)0);
                w.WriteByte(p.NetVersion);
                w.WriteByte(p.Language);
                w.WriteByte(p.Os);
                w.WriteBool(true);
                break;
            case PlayerWantJoinPacket p:
                WritePlayerWantJoin(w, p, netVersion);
                break;
            case PlayerAcceptedPacket p:
                w.WriteDword(p.Uid);
                w.WriteByte(p.Pos);
                w.WriteByte((byte)p.Accepted);
                break;
            case DropPlayerPacket p:
                w.WriteByte(p.Slot);
                w.WriteByte((byte)p.Reason);
                break;
            case TestPingPacket p:
                w.WriteDword(p.Frame);
                break;
            case EndGamePacket p:
                w.WriteBool(p.AutoEnd);
                break;
            case ServerRandomPacket p:
                w.WriteDword(p.Seed);
                break;
            case PlayerPacketBase p: // Respawn, FirstFrag: just the slot
                w.WriteByte(p.Slot);
                break;
            default:
                throw new NotSupportedException($"Cannot encode {packet.GetType().Name}.");
        }
        return w.ToArray();
    }

    private static void WriteAttack(NetWriter w, Attack a)
    {
        w.WriteByte((byte)a.Type);
        w.WriteInt(a.Param);
    }

    private static void WriteDownload(NetWriter w, DownloadPacket p, int netVersion)
    {
        w.WriteByte(p.Slot);
        var cells = (byte[])p.Cells.Clone();
        if (netVersion >= 23)
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != 0)
                    cells[i] |= 0x80;
        w.WriteBytes(cells);
        w.WriteInt(p.Seed);
        w.WriteByte(p.Idle);
        w.WriteByte(p.State);
        w.WriteByte(p.Bloc);
        w.WriteByte(p.Next);
        w.WriteByte(p.Next2);
        w.WriteByte(p.Next3);
        int bonus = Math.Min(p.Garbage.Count, 20);
        w.WriteByte((byte)bonus);
        for (int i = 0; i < 20; i++)
        {
            w.WriteByte(i < bonus ? p.Garbage[i].X : (byte)0);
            w.WriteByte(i < bonus ? p.Garbage[i].Color : (byte)0);
        }
        w.WriteBytes(p.Attacks);
        w.WriteByte(p.LastAttacker);
        w.WriteBytes(p.Blinded);
        for (int i = 0; i < 20; i++)
            w.WriteByte(i < bonus ? p.Garbage[i].BlindTime : (byte)0);
        for (int i = 0; i < bonus; i++)
            w.WriteWord((ushort)((p.Garbage[i].HolePos & 0x3FF) | (p.Garbage[i].Final ? 0x8000 : 0)));
    }

    private static void WriteGameServer(NetWriter w, GameServerPacket p)
    {
        w.WriteDword(p.Uid);
        w.WriteByte(p.Version);
        w.WriteString(p.Name, 32);
        w.WriteBool(p.Accepted);
        w.WriteInt(p.GameSeed);
        w.WriteBool(p.Paused);
        w.WriteByte((byte)p.Players.Count);
        w.WriteBool(p.NoLevelUp);
        w.WriteByte(p.LevelStart);
        w.WriteBool(p.Survivor);
        w.WriteByte(p.ComboMin);
        w.WriteWord(p.DelayStart);
        w.WriteByte((byte)p.GameEnd);
        w.WriteInt(p.GameEndValue);
        foreach (var pl in p.Players)
        {
            w.WriteByte(pl.Slot);
            w.WriteByte(pl.Team);
            w.WriteString(pl.Name, 40);
        }
        w.WriteBool(p.WantsMoves);
        w.WriteByte(p.SyncPoint);
        w.WriteByte(p.PotatoTeam);
        foreach (var pl in p.Players)
            w.WriteInt(pl.Handicap);
        // Reversed so a 1.1.1 peer, which never sends it, reads as "allowed".
        w.WriteBool(!p.AllowHandicap);
        w.WriteBool(p.HotPotato);
        if (p.Version >= 22)
        {
            WriteAttack(w, p.NormalAttack);
            WriteAttack(w, p.CleanAttack);
            WriteAttack(w, p.PotatoNormalAttack);
            WriteAttack(w, p.PotatoCleanAttack);
        }
        w.WriteBool(p.Single);
        w.WriteBool(p.Terminated);
        foreach (var pl in p.Players)
            w.WriteDword(pl.PlayerId);
        w.WriteBool(p.BoringRules);
    }

    private static void WritePlayer(NetWriter w, PlayerPacket p, int netVersion)
    {
        w.WriteDword(p.Uid);
        w.WriteByte(p.Team);
        w.WriteString(p.Name, 40);
        w.WriteByte(p.Player);
        // From net version 23 the shared repeat value is replaced by the separate ones that follow.
        w.WriteInt(netVersion >= 23 ? -1 : p.HRepeat);
        w.WriteInt(p.Smooth);
        w.WriteInt(p.Shadow);
        w.WriteByte(p.Pos);
        w.WriteInt(p.Handicap);
        w.WriteInt(p.HRepeat);
        w.WriteInt(p.VRepeat);
        w.WriteDword(p.PlayerId);
    }

    private static void WritePlayerWantJoin(NetWriter w, PlayerWantJoinPacket p, int netVersion)
    {
        w.WriteDword(p.Uid);
        w.WriteByte(p.Team);
        w.WriteString(p.Name, 40);
        w.WriteByte(p.Player);
        w.WriteInt(netVersion >= 23 ? -1 : p.HRepeat);
        w.WriteInt(p.Smooth);
        w.WriteInt(p.Shadow);
        w.WriteInt(p.Handicap);
        w.WriteBytes(Fixed16(p.PlayerHash));
        w.WriteString(p.TeamName, 40);
        w.WriteBytes(Fixed16(p.TeamHash));
        w.WriteInt(p.HRepeat);
        w.WriteInt(p.VRepeat);
    }

    private static byte[] Fixed16(byte[] hash)
    {
        var fixedSize = new byte[16];
        Array.Copy(hash, fixedSize, Math.Min(16, hash.Length));
        return fixedSize;
    }

    private static void WriteLines(NetWriter w, LinesPacket p)
    {
        w.WriteByte(p.Slot);
        w.WriteByte(p.Nb);
        w.WriteByte(p.Nc);
        w.WriteByte(p.Lx);
        w.WriteByte(p.Sender);
        WriteAttack(w, p.Attack);
        for (int i = 0; i < p.Nb; i++)
            w.WriteWord(i < p.HolePos.Count ? p.HolePos[i] : (ushort)0);
    }
}
