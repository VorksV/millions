using System;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Services.License.Models
{
    public class LicenseCheckResult
    {
        private bool _isAllowed;
        private LicenseState _state = new LicenseState();
        private string _feature = string.Empty;
        private string _blockReason = string.Empty;
        private DateTime _checkedAt = DateTime.UtcNow;

        public bool IsAllowed
        {
            get => _isAllowed;
            set => _isAllowed = value;
        }

        public LicenseState State
        {
            get => _state;
            set => _state = value;
        }

        public string Feature
        {
            get => _feature;
            set => _feature = value;
        }

        public string BlockReason
        {
            get => _blockReason;
            set => _blockReason = value;
        }

        public DateTime CheckedAt
        {
            get => _checkedAt;
            set => _checkedAt = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LicenseCheckResult Allowed(LicenseState state, string feature)
        {
            return new LicenseCheckResult
            {
                IsAllowed = true,
                State = state,
                Feature = feature,
                BlockReason = string.Empty
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LicenseCheckResult Blocked(LicenseState state, string feature, string reason)
        {
            return new LicenseCheckResult
            {
                IsAllowed = false,
                State = state,
                Feature = feature,
                BlockReason = reason
            };
        }
    }
}
