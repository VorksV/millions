namespace VoltrisOptimizer.Services.SystemChanges
{
    public class Windows11TweaksStrategy : ISystemTweaksStrategy
    {
        public string Name => "Windows 11";
        public bool AllowOptimization(string category, string optimizationName) => true;
    }
}
