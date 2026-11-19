using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Quadra.Desktop;

/// <summary>
/// Input from a script instead of a person, for checking the program end to end. A script is steps separated
/// by semicolons, one per window update: <c>move x y</c> (coordinates of the 640 x 480 picture), <c>click</c>,
/// <c>key Name</c>, <c>type text</c> and <c>wait n</c>. A key or click lasts one update, so each shows as a press.
/// </summary>
internal sealed class ScriptedInput(string script, Func<int, int, Point> pictureToWindow, Action<char> typeCharacter)
{
    /// <summary>Updates with nothing left to do before the script counts as finished, so the last step has time to show.</summary>
    private const int SettleUpdates = 5;

    private readonly Queue<string> _steps = new(script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private Point _mouse;
    private int _waitsLeft;
    private int _idleUpdates;

    /// <summary>Every step has run and the program has had a moment to show the result.</summary>
    public bool IsDone => _steps.Count == 0 && _waitsLeft == 0 && _idleUpdates > SettleUpdates;

    /// <summary>The keyboard and mouse as the next step makes them.</summary>
    public (KeyboardState Keyboard, MouseState Mouse) Next()
    {
        var keys = new List<Keys>();
        bool click = false;
        if (_waitsLeft > 0)
        {
            _waitsLeft--;
        }
        else if (_steps.Count > 0)
        {
            string[] step = _steps.Dequeue().Split(' ', 2, StringSplitOptions.TrimEntries);
            switch (step[0])
            {
                case "move":
                    var coordinates = step[1].Split(' ');
                    _mouse = pictureToWindow(int.Parse(coordinates[0]), int.Parse(coordinates[1]));
                    break;
                case "click":
                    click = true;
                    break;
                case "key":
                    keys.Add(Enum.Parse<Keys>(step[1]));
                    break;
                case "type":
                    foreach (char c in step[1])
                        typeCharacter(c);
                    break;
                case "wait":
                    _waitsLeft = int.Parse(step[1]) - 1;
                    break;
            }
        }
        else
        {
            _idleUpdates++;
        }

        return (new KeyboardState(keys.ToArray()),
            new MouseState(_mouse.X, _mouse.Y, 0, click ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }
}
