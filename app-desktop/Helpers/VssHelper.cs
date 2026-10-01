using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Volume Shadow Copy Service Helper
    /// Detecta snapshots ativos que podem impedir liberação imediata de espaço
    /// </summary>
    public static class VssHelper
    {
        /// <summary>
        /// VERIFICA SE HÁ SNAPSHOTS ATIVOS NO VOLUME
        /// Snapshots impedem que espaço seja liberado imediatamente após deleção
        /// </summary>
        public static bool HasActiveSnapshots(string driveLetter = "C:")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = $"list shadows /for={driveLetter}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);

                    // Se encontrar "Shadow Copy Volume:", há snapshots ativos
                    return output.Contains("Shadow Copy Volume:") || 
                           output.Contains("Volume de Cópia de Sombra:");
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// OBTÉM A LISTA DE SNAPSHOTS ATIVOS
        /// </summary>
        public static List<SnapshotInfo> GetActiveSnapshots(string driveLetter = "C:")
        {
            var snapshots = new List<SnapshotInfo>();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = $"list shadows /for={driveLetter}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return snapshots;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);

                    // Parse simples - contar quantos snapshots existem
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("Shadow Copy Volume:") || 
                            line.Contains("Volume de Cópia de Sombra:"))
                        {
                            snapshots.Add(new SnapshotInfo
                            {
                                Volume = driveLetter,
                                CreationTime = DateTime.Now // Simplificado
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignorar erros
            }

            return snapshots;
        }

        /// <summary>
        /// CALCULA O ESPAÇO USADO POR SNAPSHOTS
        /// </summary>
        public static long GetSnapshotUsedSpace(string driveLetter = "C:")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = $"list shadowstorage /for={driveLetter}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return 0;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);

                    // Parse simples - procurar "Used Shadow Copy Storage space:"
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("Used Shadow Copy Storage space:") ||
                            line.Contains("Espaço de Armazenamento de Cópia de Sombra Usado:"))
                        {
                            // Extrair valor (formato: "X.XX GB" ou "X.XX MB")
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                            {
                                var sizeStr = parts[1].Trim();
                                return ParseSizeString(sizeStr);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignorar erros
            }

            return 0;
        }

        private static long ParseSizeString(string sizeStr)
        {
            try
            {
                sizeStr = sizeStr.Replace(",", ".").ToUpper();
                
                if (sizeStr.Contains("GB"))
                {
                    var value = double.Parse(sizeStr.Replace("GB", "").Trim());
                    return (long)(value * 1024 * 1024 * 1024);
                }
                else if (sizeStr.Contains("MB"))
                {
                    var value = double.Parse(sizeStr.Replace("MB", "").Trim());
                    return (long)(value * 1024 * 1024);
                }
                else if (sizeStr.Contains("KB"))
                {
                    var value = double.Parse(sizeStr.Replace("KB", "").Trim());
                    return (long)(value * 1024);
                }
            }
            catch
            {
                // Ignorar erros de parse
            }

            return 0;
        }

        public class SnapshotInfo
        {
            public string Volume { get; set; } = "";
            public DateTime CreationTime { get; set; }
        }
    }
}
