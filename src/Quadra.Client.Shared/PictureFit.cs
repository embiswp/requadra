using Microsoft.Xna.Framework;
using Quadra.Core.Rendering;

namespace Quadra.Client;

/// <summary>How the original's 640 x 480 picture sits in the window: as large as fits, centred, in proportion.</summary>
internal readonly record struct PictureFit(Rectangle Area)
{
    public static PictureFit In(int windowWidth, int windowHeight)
    {
        float scale = Math.Min(windowWidth / (float)SinglePlayerView.ScreenWidth, windowHeight / (float)SinglePlayerView.ScreenHeight);
        int width = (int)(SinglePlayerView.ScreenWidth * scale), height = (int)(SinglePlayerView.ScreenHeight * scale);
        return new PictureFit(new Rectangle((windowWidth - width) / 2, (windowHeight - height) / 2, width, height));
    }

    /// <summary>A point of the window, in the picture's coordinates.</summary>
    public Point ToPicture(int windowX, int windowY) =>
        new((windowX - Area.X) * SinglePlayerView.ScreenWidth / Math.Max(Area.Width, 1),
            (windowY - Area.Y) * SinglePlayerView.ScreenHeight / Math.Max(Area.Height, 1));

    /// <summary>A point of the picture, in window pixels.</summary>
    public Point ToWindow(int pictureX, int pictureY) =>
        new(Area.X + pictureX * Area.Width / SinglePlayerView.ScreenWidth, Area.Y + pictureY * Area.Height / SinglePlayerView.ScreenHeight);
}
