using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.Services.License.Exceptions
{
    /// <summary>
    /// Exceção lançada quando uma funcionalidade é bloqueada por licença
    /// </summary>
    public class LicenseBlockedException : System.Exception
    {
        public LicenseState State { get; }
        public string Feature { get; }
        public string BlockReason { get; }

        public LicenseBlockedException(LicenseState state, string feature, string blockReason) 
            : base($"Feature '{feature}' blocked: {blockReason}")
        {
            State = state;
            Feature = feature;
            BlockReason = blockReason;
        }

        public LicenseBlockedException(LicenseCheckResult checkResult) 
            : this(checkResult.State, checkResult.Feature, checkResult.BlockReason)
        {
        }
    }
}
