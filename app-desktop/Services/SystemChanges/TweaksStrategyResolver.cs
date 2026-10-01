namespace VoltrisOptimizer.Services.SystemChanges
{
    public interface ITweaksStrategyResolver
    {
        ISystemTweaksStrategy Resolve();
    }

    public class TweaksStrategyResolver : ITweaksStrategyResolver
    {
        private readonly ICapabilityGuard _guard;
        public TweaksStrategyResolver(ICapabilityGuard guard) { _guard = guard; }
        public ISystemTweaksStrategy Resolve()
        {
            return _guard.IsWindows11() ? new Windows11TweaksStrategy() : new Windows10TweaksStrategy();
        }
    }
}
