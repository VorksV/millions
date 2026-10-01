using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace VoltrisOptimizerInstaller
{
    public interface IReversibleAction
    {
        void Apply();
        void Rollback();
    }

    public class InstallerTransaction
    {
        private readonly List<IReversibleAction> _applied = new();

        public void Add(IReversibleAction action) => _applied.Add(action);

        public void ExecuteAll()
        {
            for (int i = 0; i < _applied.Count; i++)
            {
                var a = _applied[i];
                a.Apply();
            }
        }

        public void Rollback()
        {
            for (int i = _applied.Count - 1; i >= 0; i--)
            {
                try
                {
                    _applied[i].Rollback();
                }
                catch { }
            }
        }
    }

    public class CreateDirectoryAction : IReversibleAction
    {
        private readonly string _path;
        private bool _created;

        public CreateDirectoryAction(string path)
        {
            _path = path;
        }

        public void Apply()
        {
            if (!Directory.Exists(_path))
            {
                Directory.CreateDirectory(_path);
                _created = true;
            }
        }

        public void Rollback()
        {
            if (_created)
            {
                try
                {
                    Directory.Delete(_path, true);
                }
                catch { }
            }
        }
    }

    public class CopyDirectoryAction : IReversibleAction
    {
        private readonly string _source;
        private readonly string _dest;
        private readonly List<string> _createdFiles = new();
        private readonly List<string> _createdDirs = new();

        public CopyDirectoryAction(string source, string dest)
        {
            _source = source;
            _dest = dest;
        }

        public void Apply()
        {
            foreach (var dir in Directory.GetDirectories(_source, "*", SearchOption.AllDirectories))
            {
                var targetSubDir = dir.Replace(_source, _dest);
                if (!Directory.Exists(targetSubDir))
                {
                    Directory.CreateDirectory(targetSubDir);
                    _createdDirs.Add(targetSubDir);
                }
            }

            foreach (var file in Directory.GetFiles(_source, "*", SearchOption.AllDirectories))
            {
                var targetFile = file.Replace(_source, _dest);
                var tfDir = Path.GetDirectoryName(targetFile);

                if (!string.IsNullOrEmpty(tfDir) && !Directory.Exists(tfDir))
                {
                    Directory.CreateDirectory(tfDir);
                    _createdDirs.Add(tfDir);
                }

                File.Copy(file, targetFile, true);
                _createdFiles.Add(targetFile);
            }
        }

        public void Rollback()
        {
            foreach (var f in _createdFiles)
            {
                try
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                catch { }
            }

            for (int i = _createdDirs.Count - 1; i >= 0; i--)
            {
                var d = _createdDirs[i];
                try
                {
                    if (Directory.Exists(d)) Directory.Delete(d, true);
                }
                catch { }
            }
        }
    }

    public class EnsureUninstallerAction : IReversibleAction
    {
        private readonly string _sourceExe;
        private readonly string _destExe;
        private bool _created;

        public EnsureUninstallerAction(string sourceExe, string destExe)
        {
            _sourceExe = sourceExe;
            _destExe = destExe;
        }

        public void Apply()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_destExe)!);
            File.Copy(_sourceExe, _destExe, true);
            _created = true;
        }

        public void Rollback()
        {
            if (_created)
            {
                try
                {
                    if (File.Exists(_destExe)) File.Delete(_destExe);
                }
                catch { }
            }
        }
    }

    public class CreateShortcutAction : IReversibleAction
    {
        private readonly string _shortcut;
        private readonly Action _creator;

        public CreateShortcutAction(string shortcut, Action creator)
        {
            _shortcut = shortcut;
            _creator = creator;
        }

        public void Apply()
        {
            _creator();
        }

        public void Rollback()
        {
            try
            {
                if (File.Exists(_shortcut)) File.Delete(_shortcut);
            }
            catch { }
        }
    }

    public class UninstallKeyAction : IReversibleAction
    {
        private readonly string _installPath;
        private readonly string _displayName;
        private readonly string _publisher;
        private readonly string _displayVersion;
        private readonly string _displayIcon;
        private readonly int _estimatedKb;
        private Dictionary<string, object>? _backupValues;
        private bool _createdNew;

        public UninstallKeyAction(string installPath, string displayName, string publisher, string displayVersion, string displayIcon, int estimatedKb)
        {
            _installPath = installPath;
            _displayName = displayName;
            _publisher = publisher;
            _displayVersion = displayVersion;
            _displayIcon = displayIcon;
            _estimatedKb = estimatedKb;
        }

        public void Apply()
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer");

            if (key == null) throw new InvalidOperationException("Falha ao criar chave de desinstalação");

            var names = key.GetValueNames();
            _backupValues = new Dictionary<string, object>();

            foreach (var n in names)
            {
                try
                {
                    _backupValues[n] = key.GetValue(n)!;
                }
                catch { }
            }

            if (names.Length == 0) _createdNew = true;

            key.SetValue("DisplayName", _displayName, RegistryValueKind.String);
            if (!string.IsNullOrWhiteSpace(_displayVersion)) key.SetValue("DisplayVersion", _displayVersion, RegistryValueKind.String);
            key.SetValue("Publisher", _publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", _installPath, RegistryValueKind.String);

            var uninstallExe = Path.Combine(_installPath, "uninstall.exe");
            var uninstallCmd = $"\"{uninstallExe}\"";
            var quietUninstallCmd = $"\"{uninstallExe}\" /silent";

            key.SetValue("UninstallString", uninstallCmd, RegistryValueKind.String);
            key.SetValue("QuietUninstallString", quietUninstallCmd, RegistryValueKind.String);
            key.SetValue("DisplayIcon", _displayIcon, RegistryValueKind.String);
            key.SetValue("EstimatedSize", _estimatedKb, RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.UtcNow.ToString("yyyyMMdd"), RegistryValueKind.String);
        }

        public void Rollback()
        {
            try
            {
                try
                {
                    using var baseKey32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                    if (_backupValues == null)
                    {
                        baseKey32.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer", false);
                        return;
                    }

                    using var key32 = baseKey32.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer");
                    if (key32 == null) return;

                    if (_createdNew)
                    {
                        baseKey32.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer", false);
                        return;
                    }

                    foreach (var n in key32.GetValueNames())
                    {
                        try
                        {
                            key32.DeleteValue(n, false);
                        }
                        catch { }
                    }

                    foreach (var kv in _backupValues)
                    {
                        try
                        {
                            key32.SetValue(kv.Key, kv.Value);
                        }
                        catch { }
                    }
                }
                catch { }

                try
                {
                    using var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    if (_backupValues == null)
                    {
                        baseKey64.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer", false);
                        return;
                    }

                    using var key64 = baseKey64.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer");
                    if (key64 == null) return;

                    if (_createdNew)
                    {
                        baseKey64.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoltrisOptimizer", false);
                        return;
                    }

                    foreach (var n in key64.GetValueNames())
                    {
                        try
                        {
                            key64.DeleteValue(n, false);
                        }
                        catch { }
                    }

                    foreach (var kv in _backupValues)
                    {
                        try
                        {
                            key64.SetValue(kv.Key, kv.Value);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
        }
    }
}
