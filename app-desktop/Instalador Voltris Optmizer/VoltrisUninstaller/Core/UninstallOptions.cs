namespace VoltrisUninstaller.Core
{
    /// <summary>
    /// Opções de desinstalação
    /// </summary>
    public class UninstallOptions
    {
        public bool Silent { get; set; }
        public bool KeepUserData { get; set; }
        public bool DryRun { get; set; }
        public bool Force { get; set; }
        public bool Relocated { get; set; } // Indica que o processo já roda a partir de %TEMP%
        public string? LogPath { get; set; }
        public string? UninstallationReason { get; set; }
    }
}
