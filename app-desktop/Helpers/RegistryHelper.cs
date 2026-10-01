using Microsoft.Win32;

namespace VoltrisOptimizer.Helpers
{
    public static class RegistryHelper
    {
        public static RegistryKey? OpenKeySafe(RegistryHive hive, RegistryView view, string subKey, bool writable)
        {
            try
            {
                var baseKey = RegistryKey.OpenBaseKey(hive, view);
                return baseKey.OpenSubKey(subKey, writable);
            }
            catch { return null; }
        }

        public static RegistryKey? CreateKeyPath(RegistryHive hive, RegistryView view, string subKey)
        {
            try
            {
                var parts = subKey.Split('\\');
                var baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey? current = baseKey;
                foreach (var part in parts)
                {
                    current = current!.CreateSubKey(part, true);
                }
                return current;
            }
            catch { return null; }
        }

        public static void CopyRecursive(RegistryKey? src, RegistryKey? dst)
        {
            if (src == null || dst == null) return;
            try
            {
                foreach (var valueName in src.GetValueNames())
                {
                    if (string.IsNullOrEmpty(valueName)) continue;
                    var val = src.GetValue(valueName);
                    if (val == null) continue;
                    var kind = src.GetValueKind(valueName);
                    dst.SetValue(valueName, val, kind);
                }
                foreach (var subName in src.GetSubKeyNames())
                {
                    using var srcChild = src.OpenSubKey(subName);
                    using var dstChild = dst.CreateSubKey(subName);
                    CopyRecursive(srcChild, dstChild);
                }
            }
            catch { }
        }

        public static string? GetValueString(RegistryKey? key, string name)
        {
            try { return key?.GetValue(name)?.ToString(); } catch { return null; }
        }

        public static bool SetValueSafe(RegistryKey? key, string name, object? value, RegistryValueKind kind)
        {
            try
            {
                if (key == null) return false;
                if (string.IsNullOrEmpty(name) || value == null) return false;
                key.SetValue(name, value, kind);
                return true;
            }
            catch { return false; }
        }
    }
}
