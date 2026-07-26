using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Dona.Crm.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    internal static event Action<int, Result, Intent?>? ActivityResultReceived;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarAppearance();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplySystemBarAppearance();
    }

    private void ApplySystemBarAppearance()
    {
        var window = Window;
        var decorView = window?.DecorView;
        if (window is null || decorView is null)
            return;

        var controller = WindowCompat.GetInsetsController(window, decorView);
        if (controller is null)
            return;

        controller.AppearanceLightStatusBars = false;
        controller.AppearanceLightNavigationBars = false;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        ActivityResultReceived?.Invoke(requestCode, resultCode, data);
    }
}
