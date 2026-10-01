using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    public interface IHealthReportService
    {
        Task LogHealthEventAsync(string component, string status, string details, Dictionary<string, object>? metrics = null);
        Task<string> GenerateSystemReportAsync();
    }
}
