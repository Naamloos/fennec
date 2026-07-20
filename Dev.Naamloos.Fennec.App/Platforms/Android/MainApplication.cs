using Android.App;
using Android.Runtime;
using Google.Android.Material.Color;

namespace Dev.Naamloos.Fennec.App
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override void OnCreate()
        {
            base.OnCreate();
            DynamicColors.ApplyToActivitiesIfAvailable(this);
        }
    }
}
