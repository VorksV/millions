using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    public interface IPrivacyTuningService
    {
        Task<bool> ApplyTweakAsync(string tag, bool enable);
        Task<bool> GetTweakStateAsync(string tag);
    }
}
