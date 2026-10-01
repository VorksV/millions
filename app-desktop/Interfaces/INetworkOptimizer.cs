using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Interface para serviço de otimização de rede
    /// </summary>
    public interface INetworkOptimizer
    {
        Task<bool> FlushDnsAsync(Action<int>? progressCallback = null);
        Task<bool> ResetWinsockAsync(Action<int>? progressCallback = null);
        Task<bool> ResetIPStackAsync(Action<int>? progressCallback = null);
        Task<bool> RenewDhcpAsync(Action<int>? progressCallback = null);
        Task<bool> OptimizeTcpSettingsAsync(Action<int>? progressCallback = null);
        Task<bool> SetDnsAsync(string interfaceName, string[] dnsv4, string[] dnsv6);
        Task<bool> ResetDnsAsync(string interfaceName);
    }
}
