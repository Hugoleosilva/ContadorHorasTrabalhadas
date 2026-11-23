using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RelogioAcompanhamento
{
    public static class StartupManager
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RelogioAcompanhamento";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(ValueName);
                    return val != null && !string.IsNullOrEmpty(val.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        string exePath = Application.ExecutablePath;
                        key.SetValue(ValueName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null)
                        {
                            key.DeleteValue(ValueName, false);
                        }
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
