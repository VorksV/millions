using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Modules
{
    public class RecycleBinCleanupModule : BaseCleanupModule
    {
        public override string Name => "Lixeira";

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        public override Task<long> AnalyzeAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int result = SHQueryRecycleBin(null, ref info);
                return result == 0 ? info.i64Size : 0;
            });
        }

        public override Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                long size = await AnalyzeAsync(ct);
                if (size == 0) return 0L;

                progress?.Report("Esvaziando a Lixeira...");
                bool emptied = VoltrisOptimizer.Utils.Win32.RecycleBinHelper.EmptyWithoutUi();

                return emptied ? size : 0;
            });
        }
    }
}
