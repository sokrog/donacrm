using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using AndroidX.Core.View;

namespace Dona.Crm.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static readonly Android.Graphics.Color AppBackground = Android.Graphics.Color.ParseColor("#09070F");

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

        window.SetBackgroundDrawable(new ColorDrawable(AppBackground));

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            window.NavigationBarContrastEnforced = false;

#pragma warning disable CA1422 // Android < 15 still requires the compatibility color API.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            window.SetNavigationBarColor(AppBackground);
#pragma warning restore CA1422

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
