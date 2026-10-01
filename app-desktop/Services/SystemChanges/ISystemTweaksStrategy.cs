namespace VoltrisOptimizer.Services.SystemChanges
{
    public interface ISystemTweaksStrategy
    {
        string Name { get; }
        bool AllowOptimization(string category, string optimizationName);
    }
}
