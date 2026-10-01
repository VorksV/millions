using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Interfaces
{
    public interface INetworkDevice
    {
        string IPAddress { get; set; }
        string MacAddress { get; set; }
        string Hostname { get; set; }
        string Vendor { get; set; }
        string DeviceType { get; set; }
        string DeviceCategory { get; set; }
        string DeviceModel { get; set; }
        string OperatingSystem { get; set; }
        string FriendlyName { get; set; }
        string Icon { get; set; }
        bool IsGateway { get; set; }
        bool IsOnline { get; set; }
        bool IsPortable { get; set; }
        List<int> OpenPorts { get; set; }
        DateTime FirstSeen { get; set; }
        DateTime LastSeen { get; set; }
        bool IsNew { get; set; }
        int ConfidenceLevel { get; set; }
    }
}
