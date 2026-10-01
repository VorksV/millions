using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.SystemChanges
{
    public interface ISystemChangeTransactionService
    {
        ISystemTransaction Begin(string name);
    }

    public interface ISystemTransaction : IDisposable
    {
        void RegisterRegistryChange(string rootPath, string valueName, object? oldValue, object? newValue, bool currentUser = false);
        void RegisterServiceChange(string serviceName, int? oldStartType, int? newStartType);
        void RegisterTaskChange(string taskPath, bool enabledBefore, bool enabledAfter);
        void Commit();
        void Rollback();
    }
}
