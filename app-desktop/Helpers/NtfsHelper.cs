using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// NTFS Helper - Acesso nativo ao sistema de arquivos Windows
    /// Resolve divergências entre tamanho lógico e físico
    /// </summary>
    public static class NtfsHelper
    {
        #region P/Invoke Declarations

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetCompressedFileSizeW(
            string lpFileName,
            out uint lpFileSizeHigh);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        #endregion

        /// <summary>
        /// OBTÉM O TAMANHO FÍSICO REAL ALOCADO NO DISCO (EM BYTES)
        /// Considera compressão NTFS e clusters alocados
        /// </summary>
        public static long GetPhysicalFileSize(string filePath)
        {
            try
            {
                // Tentar obter tamanho comprimido (tamanho físico real no disco)
                uint highOrder = 0;
                uint lowOrder = GetCompressedFileSizeW(filePath, out highOrder);

                if (lowOrder == 0xFFFFFFFF)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 0)
                    {
                        // Fallback para FileInfo.Length se falhar
                        return new FileInfo(filePath).Length;
                    }
                }

                long size = ((long)highOrder << 32) | lowOrder;
                return size;
            }
            catch
            {
                // Fallback seguro
                try
                {
                    return new FileInfo(filePath).Length;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// VERIFICA SE O ARQUIVO É UM HARDLINK (MÚLTIPLAS REFERÊNCIAS)
        /// Arquivos com NumberOfLinks > 1 NÃO devem ser contados múltiplas vezes
        /// </summary>
        public static bool IsHardLink(string filePath, out int numberOfLinks)
        {
            numberOfLinks = 1;

            try
            {
                using (var handle = CreateFile(
                    filePath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        return false;
                    }

                    BY_HANDLE_FILE_INFORMATION fileInfo;
                    if (GetFileInformationByHandle(handle, out fileInfo))
                    {
                        numberOfLinks = (int)fileInfo.NumberOfLinks;
                        return numberOfLinks > 1;
                    }
                }
            }
            catch
            {
                // Em caso de erro, assumir que não é hardlink
            }

            return false;
        }

        /// <summary>
        /// OBTÉM O FILE INDEX ÚNICO (PARA RASTREAR HARDLINKS JÁ CONTADOS)
        /// Retorna um identificador único do arquivo no volume
        /// </summary>
        public static ulong GetFileIndex(string filePath)
        {
            try
            {
                using (var handle = CreateFile(
                    filePath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        return 0;
                    }

                    BY_HANDLE_FILE_INFORMATION fileInfo;
                    if (GetFileInformationByHandle(handle, out fileInfo))
                    {
                        return ((ulong)fileInfo.FileIndexHigh << 32) | fileInfo.FileIndexLow;
                    }
                }
            }
            catch
            {
                // Em caso de erro, retornar 0
            }

            return 0;
        }

        /// <summary>
        /// INFORMAÇÕES COMPLETAS DO ARQUIVO PARA ANÁLISE PRECISA
        /// </summary>
        public class FileAnalysisInfo
        {
            public long PhysicalSize { get; set; }
            public long LogicalSize { get; set; }
            public bool IsHardLink { get; set; }
            public int NumberOfLinks { get; set; }
            public ulong FileIndex { get; set; }
            public bool IsCompressed { get; set; }
            public bool IsReclaimable { get; set; }
            public string NonReclaimableReason { get; set; } = "";
        }

        /// <summary>
        /// ANÁLISE COMPLETA DO ARQUIVO - RETORNA TODAS AS INFORMAÇÕES NECESSÁRIAS
        /// </summary>
        public static FileAnalysisInfo AnalyzeFile(string filePath)
        {
            var info = new FileAnalysisInfo();

            try
            {
                var fileInfo = new FileInfo(filePath);
                info.LogicalSize = fileInfo.Length;
                info.PhysicalSize = GetPhysicalFileSize(filePath);
                info.IsCompressed = info.PhysicalSize < info.LogicalSize;

                int numberOfLinks;
                info.IsHardLink = IsHardLink(filePath, out numberOfLinks);
                info.NumberOfLinks = numberOfLinks;
                info.FileIndex = GetFileIndex(filePath);

                // Determinar se é recuperável
                if (info.IsHardLink)
                {
                    info.IsReclaimable = false;
                    info.NonReclaimableReason = $"Hardlink ({numberOfLinks} referências)";
                }
                else
                {
                    info.IsReclaimable = true;
                }
            }
            catch (Exception ex)
            {
                info.IsReclaimable = false;
                info.NonReclaimableReason = $"Erro: {ex.Message}";
            }

            return info;
        }
    }
}
