namespace Quadra.Core.Menus;

/// <summary>Turns a key's stored name (such as <c>RightShift</c>) into something to show (<c>Right Shift</c>).</summary>
public static class KeyNames
{
    public static string Friendly(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "?";
        // Digit row keys are called D0..D9; numeric keypad keys NumPad0..9.
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return name[1].ToString();
        if (name.StartsWith("Oem", StringComparison.Ordinal))
            name = name[3..];
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                text.Append(' ');
            else if (i > 0 && char.IsDigit(name[i]) && !char.IsDigit(name[i - 1]))
                text.Append(' ');
            text.Append(name[i]);
        }
        return text.ToString();
    }
}
