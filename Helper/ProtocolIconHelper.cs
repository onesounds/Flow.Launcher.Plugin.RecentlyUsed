
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.RecentlyUsed.Helper
{
    public static class ProtocolIconHelper
    {
        public static string GetProtocolIconPath(string protocol)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(protocol + "\\DefaultIcon"))
                {
                    if (key == null)
                        return null;

                    var icon = key.GetValue("") as string;
                    return icon;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
