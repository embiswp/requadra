using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;
using Quadra.Client;

[assembly: Application(Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round")]

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
    private QuadraGame? _game;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _game = new QuadraGame(new StartupOptions());
        SetContentView((View)_game.Services.GetService(typeof(View))!);
        _game.Run();
    }
}
