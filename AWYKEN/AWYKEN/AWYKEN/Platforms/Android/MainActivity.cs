using Android.App;
using Android.Content.PM;
using Android.OS;

namespace AWYKEN
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestStoragePermissions();
        }

        private void RequestStoragePermissions()
        {
            if ((int)Build.VERSION.SdkInt >= 33) // Android 13+
            {
                if (CheckSelfPermission("android.permission.READ_MEDIA_IMAGES") != Permission.Granted)
                {
                    RequestPermissions(new[] { "android.permission.READ_MEDIA_IMAGES" }, 1);
                }
            }
            else if ((int)Build.VERSION.SdkInt >= 30) // Android 11+
            {
                if (CheckSelfPermission("android.permission.READ_EXTERNAL_STORAGE") != Permission.Granted)
                {
                    RequestPermissions(new[] { "android.permission.READ_EXTERNAL_STORAGE" }, 1);
                }
            }
        }
    }
}
