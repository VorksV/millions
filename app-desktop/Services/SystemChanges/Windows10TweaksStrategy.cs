namespace VoltrisOptimizer.Services.SystemChanges
{
    public class Windows10TweaksStrategy : ISystemTweaksStrategy
    {
        public string Name => "Windows 10";
        public bool AllowOptimization(string category, string optimizationName) => true;
    }
}
