using Quadra.Core.Assets;
using Quadra.Core.Game;

namespace Quadra.Client;

/// <summary>Plays the sound cues for what happens in the menus and in a game.</summary>
internal sealed class GameSounds(AssetLibrary assets, SoundCues cues)
{
    private const float MasterVolume = 0.6f;

    public void MenuHover() => Play(cues.MenuHover());
    public void MenuClick() => Play(cues.MenuClick());
    public void MenuValue() => Play(cues.MenuValue());
    public void Pause() => Play(cues.Pause());
    public void NewHighScore() => Play(cues.LevelUp());      // the original celebrates a new high score like a level-up

    /// <summary>Plays a sound for each event the game reported, then forgets them.</summary>
    public void PlayEvents(List<GameEvent> events, int level)
    {
        var set = SoundCues.ForLevel(level);
        foreach (var e in events)
        {
            SoundCue? cue = e.Kind switch
            {
                GameEventKind.Rotated => cues.Rotate(set, e.Column),
                GameEventKind.Stamped => cues.Stamp(set, e.Column),
                GameEventKind.LineFlash => cues.LineFlash(set, e.Combo),
                GameEventKind.BlocksFell => cues.Fall(e.Value, e.Combo),
                GameEventKind.LevelUp => cues.LevelUp(),
                GameEventKind.GameOver => cues.Death(set),
                _ => null,
            };
            if (cue is { } c)
                Play(c);
        }
        events.Clear();
    }

    private void Play(SoundCue cue) => assets.Play(cue, MasterVolume);
}
