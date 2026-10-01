using System;

namespace VoltrisUninstaller.Core
{
    public class UninstallResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UninstallProgress
    {
        public string Step { get; set; } = string.Empty;
        public int Percent { get; set; }
    }
}
