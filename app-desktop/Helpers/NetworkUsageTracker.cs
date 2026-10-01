using System;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace VoltrisOptimizer.Helpers
{
    public readonly struct NetworkRateSample
    {
        public double DownloadBytesPerSec { get; init; }
        public double UploadBytesPerSec { get; init; }
    }

    /// <summary>
    /// Mede a velocidade real de download/upload (bytes/s) calculando deltas de
    /// IPv4InterfaceStatistics entre duas amostras. Nenhum valor fictício.
    /// </summary>
    public static class NetworkUsageTracker
    {
        private static readonly object _lock = new object();
        private static long _lastDown;
        private static long _lastUp;
        private static long _lastTicks;
        private static bool _initialized;

        public static NetworkRateSample Sample()
        {
            long down = 0;
            long up = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (nic.Speed <= 0) continue;

                        var stats = nic.GetIPv4Statistics();
                        down += stats.BytesReceived;
                        up += stats.BytesSent;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
                return new NetworkRateSample();
            }

            long nowTicks = Stopwatch.GetTimestamp();
            double rateDown = 0;
            double rateUp = 0;

            lock (_lock)
            {
                if (_initialized)
                {
                    double seconds = (double)(nowTicks - _lastTicks) / Stopwatch.Frequency;
                    if (seconds > 0.05 && seconds < 60)
                    {
                        rateDown = Math.Max(0, (double)(down - _lastDown) / seconds);
                        rateUp = Math.Max(0, (double)(up - _lastUp) / seconds);
                    }
                }

                _initialized = true;
                _lastTicks = nowTicks;
                _lastDown = down;
                _lastUp = up;
            }

            return new NetworkRateSample
            {
                DownloadBytesPerSec = rateDown,
                UploadBytesPerSec = rateUp
            };
        }
    }
}