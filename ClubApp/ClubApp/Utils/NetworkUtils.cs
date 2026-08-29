using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace AetherShell.Client.Utils
{
    public static class NetworkUtils
    {
        public static string GetMacAddress()
        {
            try
            {
                var mac = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && 
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.GetPhysicalAddress().ToString())
                    .FirstOrDefault();
                
                return string.IsNullOrEmpty(mac) ? "UNKNOWN-ID" : mac;
            }
            catch
            {
                return "ERR-" + Guid.NewGuid().ToString().Substring(0, 8);
            }
        }
    }
}
