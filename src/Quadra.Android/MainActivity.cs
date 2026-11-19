using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;

namespace Quadra.Android;

/// <summary>The one screen of the app: it hosts the game, in landscape, full screen.</summary>
[Activity(
    Label = "Quadra",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    LaunchMode = LaunchMode.SingleInstance)]
public class MainActivity : AndroidGameActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // The game itself comes later; for now an empty window shows that the head builds and starts.
        var game = new Game();
        SetContentView((View)game.Services.GetService(typeof(View))!);
        game.Run();
    }
}
