using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace AetherShell.Client.Utils
{
    public class SystemInfoDto
    {
        public string IpAddress { get; set; } = "";
        public string CpuName { get; set; } = "";
        public int RamTotalMb { get; set; }
        public int RamUsedMb { get; set; }
        public string GpuName { get; set; } = "";
        public string DiskInfo { get; set; } = "";
        public string OsVersion { get; set; } = "";
    }

    public static class SystemInfoCollector
    {
        public static SystemInfoDto Collect()
        {
            var info = new SystemInfoDto();

            try
            {
                // IP адрес
                info.IpAddress = GetLocalIpAddress();

                // Процессор
                info.CpuName = GetCpuName();

                // Оперативная память
                var ram = GetRamInfo();
                info.RamTotalMb = ram.total;
                info.RamUsedMb = ram.used;

                // Видеокарта
                info.GpuName = GetGpuName();

                // Диски
                info.DiskInfo = GetDiskInfo();

                // ОС
                info.OsVersion = Environment.OSVersion.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SystemInfo] Ошибка сбора: {ex.Message}");
            }

            return info;
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return ip?.ToString() ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        private static string GetCpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    return item["Name"]?.ToString() ?? "N/A";
                }
            }
            catch { }
            return "N/A";
        }

        private static (int total, int used) GetRamInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select TotalVisibleMemorySize, FreePhysicalMemory from Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var totalKb = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                    var freeKb = Convert.ToInt64(obj["FreePhysicalMemory"]);
                    var totalMb = (int)(totalKb / 1024);
                    var usedMb = (int)((totalKb - freeKb) / 1024);
                    return (totalMb, usedMb);
                }
            }
            catch { }
            return (0, 0);
        }

        private static string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController");
                foreach (var item in searcher.Get())
                {
                    var name = item["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            catch { }
            return "N/A";
        }

        private static string GetDiskInfo()
        {
            try
            {
                var disks = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => new
                    {
                        Name = d.Name.TrimEnd('\\'),
                        TotalGb = (int)(d.TotalSize / 1024 / 1024 / 1024),
                        FreeGb = (int)(d.AvailableFreeSpace / 1024 / 1024 / 1024),
                        UsedPercent = (int)(100 - (d.AvailableFreeSpace * 100.0 / d.TotalSize))
                    })
                    .ToList();

                return JsonSerializer.Serialize(disks);
            }
            catch
            {
                return "[]";
            }
        }
    }
}
