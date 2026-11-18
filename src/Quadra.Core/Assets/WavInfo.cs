namespace Quadra.Core.Assets;

/// <summary>Reads the sample rate of a WAV file; the original plays samples at a rate relative to it.</summary>
public static class WavInfo
{
    public static int SampleRate(byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            throw new InvalidDataException("Not a WAV file.");
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            int size = wav[pos + 4] | (wav[pos + 5] << 8) | (wav[pos + 6] << 16) | (wav[pos + 7] << 24);
            if (wav[pos] == 'f' && wav[pos + 1] == 'm' && wav[pos + 2] == 't' && wav[pos + 3] == ' ')
                return wav[pos + 12] | (wav[pos + 13] << 8) | (wav[pos + 14] << 16) | (wav[pos + 15] << 24);
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException("WAV has no format chunk.");
    }
}
