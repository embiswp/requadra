using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Quadra.Core.Assets;
using Quadra.Core.Rendering;

namespace Quadra.Client;

/// <summary>A level's background picture together with its palette (which holds the block colours).</summary>
internal sealed record Background(Texture2D Picture, Rgb[] Palette);

/// <summary>
/// Loads the original art, sounds and fonts from the Assets folder, through <see cref="TitleContainer"/>: that is
/// the folder next to the program on a desktop and the packaged assets on Android. Backgrounds and sounds are
/// loaded when first needed.
/// </summary>
internal sealed class AssetLibrary : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<int, Background> _backgrounds = [];
    private readonly Dictionary<string, Background> _pictures = [];
    private readonly Dictionary<string, PixelBuffer?> _sprites = [];
    private readonly Dictionary<string, Texture2D> _spriteTextures = [];
    private readonly Dictionary<int, LegacyText> _texts = [];
    private readonly Dictionary<string, SoundEffect?> _sounds = [];
    private readonly Dictionary<string, int> _sampleRates = [];
    private bool _audioFailed;

    public AssetLibrary(GraphicsDevice device)
    {
        _device = device;
        TextFont = new LegacyFont(Read("fonts/font.fnt"), 2);
        NumberFont = new LegacyFont(Read("fonts/courrier.fnt"), 1);
        LevelUpBanner = LoadSprite("gamelvup.png");
        PauseBanner = LoadSprite("gamepaus.png");
    }

    // Desktop copies content next to the executable under Assets/; Android's AndroidAsset packaging
    // strips that leading folder name, so assets there sit directly under the package's asset root.
    private static readonly string AssetFolder = OperatingSystem.IsAndroid() ? "" : "Assets/";

    private static byte[] Read(string file)
    {
        using var stream = TitleContainer.OpenStream(AssetFolder + file);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static bool Exists(string file)
    {
        try
        {
            using var stream = TitleContainer.OpenStream(AssetFolder + file);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public LegacyFont TextFont { get; }
    public LegacyFont NumberFont { get; }

    /// <summary>The game's strings in a language (0 English, 1 French).</summary>
    public LegacyText TextFor(int language)
    {
        if (!_texts.TryGetValue(language, out var text))
        {
            string file = language == 1 ? "francais.txt" : "anglais.txt";
            text = new LegacyText(Read("textes/" + file));
            _texts[language] = text;
        }
        return text;
    }

    /// <summary>Size of a picture in the images folder, read from its header.</summary>
    public (int Width, int Height) ImageSize(string file) => PngInfo.Size(Read("images/" + file));

    /// <summary>A full-screen picture (menu background) with its palette; cached.</summary>
    public Background Picture(string file)
    {
        if (!_pictures.TryGetValue(file, out var picture))
        {
            byte[] bytes = Read("images/" + file);
            using var stream = new MemoryStream(bytes);
            picture = new Background(Texture2D.FromStream(_device, stream), PngPalette.Read(bytes));
            _pictures[file] = picture;
        }
        return picture;
    }

    /// <summary>A picture whose palette entry 0 is see-through (hover pictures, overlays); cached.</summary>
    public PixelBuffer? Sprite(string file)
    {
        if (!_sprites.TryGetValue(file, out var sprite))
        {
            sprite = Exists("images/" + file) ? LoadSprite(file) : null;
            _sprites[file] = sprite;
        }
        return sprite;
    }

    /// <summary>The same as <see cref="Sprite"/>, as a texture for drawing with a sprite batch.</summary>
    public Texture2D? SpriteTexture(string file)
    {
        if (_spriteTextures.TryGetValue(file, out var texture))
            return texture;
        if (Sprite(file) is not { } sprite)
            return null;
        texture = ToTexture(sprite);
        _spriteTextures[file] = texture;
        return texture;
    }

    /// <summary>The rising "level up" banner (palette entry 0 is see-through).</summary>
    public PixelBuffer LevelUpBanner { get; }

    /// <summary>The "pause" sign shown in the middle of the screen.</summary>
    public PixelBuffer PauseBanner { get; }

    /// <summary>The background of a level; the originals cycle through ten pictures.</summary>
    public Background BackgroundFor(int level)
    {
        int index = ((level - 1) % 10 + 10) % 10;
        if (!_backgrounds.TryGetValue(index, out var background))
        {
            byte[] bytes = Read($"images/fond{index}.png");
            using var stream = new MemoryStream(bytes);
            background = new Background(Texture2D.FromStream(_device, stream), PngPalette.Read(bytes));
            _backgrounds[index] = background;
        }
        return background;
    }

    /// <summary>Sample rate of a sound file (the original plays samples at a rate relative to it).</summary>
    public int SampleRate(string file)
    {
        if (!_sampleRates.TryGetValue(file, out int rate))
        {
            rate = WavInfo.SampleRate(Read("sons/" + file));
            _sampleRates[file] = rate;
        }
        return rate;
    }

    /// <summary>Plays a cue. Sound is optional: if the machine has no audio device the game stays silent.</summary>
    public void Play(SoundCue cue, float masterVolume)
    {
        if (_audioFailed)
            return;
        try
        {
            if (!_sounds.TryGetValue(cue.File, out var effect))
            {
                using var stream = TitleContainer.OpenStream(AssetFolder + "sons/" + cue.File);
                effect = SoundEffect.FromStream(stream);
                _sounds[cue.File] = effect;
            }
            effect?.Play(Math.Clamp(cue.Volume * masterVolume, 0f, 1f), cue.Pitch, cue.Pan);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or NotSupportedException or DllNotFoundException)
        {
            _audioFailed = true;
            Console.Error.WriteLine($"Sound disabled: {e.Message}");
        }
    }

    /// <summary>Turns a texture-less sprite into a texture, for drawing with a sprite batch.</summary>
    public Texture2D ToTexture(PixelBuffer buffer)
    {
        var texture = new Texture2D(_device, buffer.Width, buffer.Height);
        texture.SetData(buffer.Pixels);
        return texture;
    }

    private PixelBuffer LoadSprite(string name)
    {
        byte[] bytes = Read("images/" + name);
        using var stream = new MemoryStream(bytes);
        using var texture = Texture2D.FromStream(_device, stream);
        var rgba = new byte[texture.Width * texture.Height * 4];
        texture.GetData(rgba);
        var sprite = PixelBuffer.FromRgba(texture.Width, texture.Height, rgba);
        sprite.MakeTransparent(PngPalette.Read(bytes)[0]);
        return sprite;
    }

    public void Dispose()
    {
        foreach (var background in _backgrounds.Values.Concat(_pictures.Values))
            background.Picture.Dispose();
        foreach (var texture in _spriteTextures.Values)
            texture.Dispose();
        foreach (var effect in _sounds.Values)
            effect?.Dispose();
    }
}
