using System;
using System.Windows;
using static Jvedio.App;

namespace Jvedio.Core.Lang
{
    public static class LangManager
    {
        /// <summary>
        /// 固定设置为简体中文，忽略传入值
        /// </summary>
        public static bool SetLang(string lang)
        {
            string format = "pack://application:,,,/Jvedio;Component/Core/Lang/{0}.xaml";
            format = string.Format(format, "zh-CN");
            foreach (ResourceDictionary mergedDictionary in Application.Current.Resources.MergedDictionaries) {
                if (mergedDictionary.Source != null && mergedDictionary.Source.OriginalString.Contains("Jvedio;Component/Core/Lang")) {
                    try {
                        bool flag = Application.Current.Resources.MergedDictionaries.Remove(mergedDictionary);
                        mergedDictionary.Source = new Uri(format, UriKind.RelativeOrAbsolute);
                        Application.Current.Resources.MergedDictionaries.Add(mergedDictionary);
                        return true;
                    } catch (Exception ex) {
                        App.Logger.Error(ex);
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
