namespace Quadra.Core.Assets;

/// <summary>The sample files used for the events of one level (<c>Canvas::change_level</c>).</summary>
public sealed record SoundSet(string Flash, string DropLoud, string DropMiddle, string DropSoft, string Rotate);

/// <summary>One sound to play: a file with a gain (0-1), a pan (-1 left, 1 right) and a pitch (in octaves).</summary>
public readonly record struct SoundCue(string File, float Volume, float Pan, float Pitch);

/// <summary>
/// Which sounds the game plays and with which volume, pan and pitch, following the original
/// (<c>Player_base::play_sound</c> calls in <c>legacy/source/player.cc</c>). The original expresses
/// volume as -4096 (silent) to 0 (full), pan as -4096 to 4096 and pitch as a playback rate in Hz; each
/// call adds a little randomness so repeated sounds do not sound identical.
/// </summary>
public sealed class SoundCues
{
    public const string FallFile = "clang3.wav";
    public const string LevelUpFile = "glissup.wav";
    public const string PauseFile = "cuckoo.wav";

    private readonly Random _random;
    private readonly Func<string, int> _sampleRate;

    /// <param name="sampleRate">Sample rate of a sound file; the playback rate is relative to it.</param>
    public SoundCues(Random random, Func<string, int> sampleRate)
    {
        _random = random;
        _sampleRate = sampleRate;
    }

    /// <summary>The five sounds of a level; levels cycle through ten sets.</summary>
    public static SoundSet ForLevel(int level) => (((level - 1) % 10 + 10) % 10) switch
    {
        1 => new SoundSet("pwap2.wav", "knock.wav", "knock.wav", "knock.wav", "click_3.wav"),
        2 => new SoundSet("blip1.wav", "metal3.wav", "metal1.wav", "metal6.wav", "click_1.wav"),
        3 => new SoundSet("whistle1.wav", "tapdrip.wav", "click01.wav", "click_3.wav", "click01.wav"),
        4 => new SoundSet("spring.wav", "pop1.wav", "bloop.wav", "pwap2.wav", "corkpop.wav"),
        5 => new SoundSet("whistle2.wav", "knock.wav", "splodge.wav", "pop1.wav", "tapdrip.wav"),
        6 => new SoundSet("glass04.wav", "glass01.wav", "glass03.wav", "glass03.wav", "click01.wav"),
        7 => new SoundSet("bubble2.wav", "water05_1.wav", "water05_2.wav", "water05_3.wav", "click01.wav"),
        8 => new SoundSet("ceramic3.wav", "explod03.wav", "explod05.wav", "explod06.wav", "tapdrip.wav"),
        9 => new SoundSet("smash2.wav", "knock.wav", "bloop.wav", "click_1.wav", "pop1.wav"),
        _ => new SoundSet("pwap2.wav", "hitwood1.wav", "chop2.wav", "metal3.wav", "tapdrip.wav"),
    };

    /// <summary>A piece was rotated: <c>sons.drip</c>.</summary>
    public SoundCue Rotate(SoundSet set, int column) =>
        Cue(set.Rotate, -200 - Rnd(127), (column - 9) * 300, 10500 + Rnd(1023));

    /// <summary>A piece was stamped: one of three drop sounds, chosen at random.</summary>
    public SoundCue Stamp(SoundSet set, int column)
    {
        string file = _random.Next(3) switch { 0 => set.DropSoft, 1 => set.DropMiddle, _ => set.DropLoud };
        return Cue(file, -200 - Rnd(255), (column - 9) * 300, 10500 + Rnd(1023));
    }

    /// <summary>Rows flash: the pitch drops with every step of a combo.</summary>
    public SoundCue LineFlash(SoundSet set, int combo) =>
        Cue(set.Flash, -300 - Rnd(255), 0, 11000 + Rnd(127) - (combo << 8));

    /// <summary>Loose cells have fallen; more steps make it louder.</summary>
    public SoundCue Fall(int steps, int combo) =>
        Cue(FallFile, Math.Min(-500 + steps * 50, 0), 0, 11000 + Rnd(127) - (combo << 8));

    public SoundCue LevelUp() => Cue(LevelUpFile, -200 - Rnd(127), 0, 11000);

    public SoundCue Pause() => Cue(PauseFile, -300, 0, 11025);

    /// <summary>The pointer enters a menu item (<c>Zone_menu::entered</c>).</summary>
    public SoundCue MenuHover() => Cue("click_1.wav", -2000, 0, 22000 + Rnd(2047));

    /// <summary>A menu item was clicked (<c>Zone_menu::clicked</c>).</summary>
    public SoundCue MenuClick() => Cue("blip1.wav", -1000, 0, 14000 + Rnd(511));

    /// <summary>A value was changed, such as a Yes/No (<c>Zone_state_text2::clicked</c>).</summary>
    public SoundCue MenuValue() => Cue("tapdrip.wav", -800, 0, 26000 + Rnd(1023));

    /// <summary>Death: the flash sound again, much higher.</summary>
    public SoundCue Death(SoundSet set) => Cue(set.Flash, -600, 0, 22500 + Rnd(1023));

    private SoundCue Cue(string file, int volume, int pan, int rateHz)
    {
        float gain = Math.Clamp((volume + 4096) / 4096f, 0f, 1f);
        float panning = Math.Clamp(pan / 4096f, -1f, 1f);
        float pitch = Math.Clamp((float)Math.Log2(Math.Max(rateHz, 1) / (double)_sampleRate(file)), -1f, 1f);
        return new SoundCue(file, gain, panning, pitch);
    }

    /// <summary>A random number from 0 up to <paramref name="mask"/> (<c>Random::rnd(and)</c>).</summary>
    private int Rnd(int mask) => _random.Next(mask + 1);
}
