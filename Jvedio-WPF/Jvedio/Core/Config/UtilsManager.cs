using static Jvedio.App;
namespace Jvedio.Core.Config
{
    public class UtilsManager
    {
        public static void OnUtilSettingChange()
        {
            SuperUtils.Config.SettingManager.IgnoreCertVal = ConfigManager.Settings.IgnoreCertVal;
            SuperUtils.Config.SettingManager.HttpTimeout = 30000; // Default timeout

            Logger?.Info("util setting changed");
        }
    }
}
