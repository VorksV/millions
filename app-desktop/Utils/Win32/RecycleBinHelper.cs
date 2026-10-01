using System;
using System.IO;
using System.Security.Principal;

namespace VoltrisOptimizer.Utils.Win32
{
    /// <summary>
    /// Esvazia a Lixeira sem exibir nenhuma interface do Shell.
    /// SHEmptyRecycleBin (mesmo com SHERB_NOPROGRESSUI) pode mostrar o diálogo
    /// "Tentar novamente / Cancelar / Ignorar" quando um arquivo está em uso,
    /// travando a otimização. A deleção feita aqui é nativa (File/Directory),
    /// nunca abre caixa e itens bloqueados são simplesmente ignorados.
    /// </summary>
    public static class RecycleBinHelper
    {
        public static bool EmptyWithoutUi()
        {
            bool anyDeleted = false;
            string currentSid = string.Empty;
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                currentSid = identity?.User?.Value ?? string.Empty;
            }
            catch
            {
            }

            try
            {
                foreach (var drive in Environment.GetLogicalDrives())
                {
                    try
                    {
                        string binRoot = Path.Combine(drive.TrimEnd('\\'), "$Recycle.Bin");
                        if (!Directory.Exists(binRoot)) continue;

                        if (!string.IsNullOrEmpty(currentSid))
                        {
                            string ownFolder = Path.Combine(binRoot, currentSid);
                            if (Directory.Exists(ownFolder))
                            {
                                try { anyDeleted |= ClearFolder(ownFolder); }
                                catch { }
                            }
                        }

                        foreach (var sidFolder in Directory.GetDirectories(binRoot))
                        {
                            try { anyDeleted |= ClearFolder(sidFolder); }
                            catch { }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return anyDeleted;
        }

        private static bool ClearFolder(string folder)
        {
            bool any = false;

            foreach (var file in Directory.GetFiles(folder))
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.IsReadOnly) fi.IsReadOnly = false;
                    fi.Delete();
                    any = true;
                }
                catch
                {
                }
            }

            foreach (var sub in Directory.GetDirectories(folder))
            {
                try
                {
                    Directory.Delete(sub, true);
                    any = true;
                }
                catch
                {
                    try
                    {
                        if (ClearFolder(sub)) any = true;
                        try { Directory.Delete(sub, false); any = true; } catch { }
                    }
                    catch
                    {
                    }
                }
            }

            return any;
        }
    }
}