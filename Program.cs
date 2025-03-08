using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "WifiDisconnectLog.txt");

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static NetworkInterface? _wirelessAdapter;
    private static bool _wasConnected = false;
    private static DateTime _startTime = DateTime.Now;

    static async Task Main(string[] args)
    {
        // Check if running on Windows
        if (!OperatingSystem.IsWindows())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: This application only runs on Windows.");
            Console.WriteLine("WiFi Disconnect Monitor requires Windows-specific features to function properly.");
            Console.ResetColor();
            return; // Exit immediately
        }

        // Get application version
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        var version = fileVersionInfo.ProductVersion ?? "1.0.0";

        Console.WriteLine($"=== WiFi Disconnect Monitor v{version} ===");
        Console.WriteLine($"Logs will be saved to: {LogFile}");

        // Initial log entry
        LogMessage("Monitor started");
        LogWirelessAdapters();

        // Find the wireless adapter
        UpdateWirelessAdapter();

        // Begin monitoring
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("Press Ctrl+C to stop monitoring");
        Console.WriteLine("Monitoring WiFi connection...");

        try
        {
            // Start the event log collection task
            var eventLogTask = CollectRelevantEventLogsAsync(cts.Token);

            // Start the network monitoring loop
            await MonitorNetworkAsync(cts.Token);

            // Wait for event log collection to complete
            await eventLogTask;
        }
        catch (OperationCanceledException)
        {
            LogMessage("Monitor stopped by user");
            Console.WriteLine("\nMonitor stopped. Log saved.");
        }
        catch (Exception ex)
        {
            LogMessage($"Error: {ex.Message}");
            Console.WriteLine($"Error: {ex.Message}");
        }

        // Final report
        await GenerateReportAsync();
    }

    private static void UpdateWirelessAdapter()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        _wirelessAdapter = interfaces.FirstOrDefault(nic =>
            nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
            nic.OperationalStatus == OperationalStatus.Up);

        if (_wirelessAdapter != null)
        {
            _wasConnected = true;
            LogMessage($"Found active wireless adapter: {_wirelessAdapter.Name}");
        }
        else
        {
            _wasConnected = false;
            var allWireless = interfaces.Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
            if (allWireless.Any())
            {
                LogMessage($"Found {allWireless.Count} wireless adapters, but none are connected");
            }
            else
            {
                LogMessage("No wireless adapters found");
            }
        }
    }

    private static void LogWirelessAdapters()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var wirelessAdapters = interfaces
            .Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ToList();

        LogMessage($"Found {wirelessAdapters.Count} wireless adapters:");
        foreach (var adapter in wirelessAdapters)
        {
            LogMessage($"  • {adapter.Name} - {adapter.Description} ({adapter.OperationalStatus})");
        }
    }

    private static async Task MonitorNetworkAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UpdateWirelessAdapter();

            bool isConnected = _wirelessAdapter != null;

            // Detect connection state changes
            if (isConnected != _wasConnected)
            {
                if (isConnected)
                {
                    LogMessage($"WiFi CONNECTED: {_wirelessAdapter!.Name}");
                    LogAdapterDetails(_wirelessAdapter);
                }
                else
                {
                    LogMessage("WiFi DISCONNECTED!");
                }
                _wasConnected = isConnected;
            }

            // Short pause between checks
            await Task.Delay(CheckInterval, token);
        }
    }

    private static void LogAdapterDetails(NetworkInterface adapter)
    {
        try
        {
            var properties = adapter.GetIPProperties();
            var sb = new StringBuilder();

            // DNS servers
            sb.AppendLine("  DNS Servers:");
            foreach (var dns in properties.DnsAddresses)
            {
                sb.AppendLine($"    {dns}");
            }

            // Gateway
            sb.AppendLine("  Gateways:");
            foreach (var gateway in properties.GatewayAddresses)
            {
                sb.AppendLine($"    {gateway.Address}");
            }

            // IP addresses
            sb.AppendLine("  IP Addresses:");
            foreach (var ip in properties.UnicastAddresses)
            {
                sb.AppendLine($"    {ip.Address} ({ip.PrefixLength})");
            }

            // Speed
            sb.AppendLine($"  Speed: {adapter.Speed / 1_000_000} Mbps");

            LogMessage(sb.ToString());
        }
        catch (Exception ex)
        {
            LogMessage($"Error getting adapter details: {ex.Message}");
        }
    }

    private static async Task CollectRelevantEventLogsAsync(CancellationToken token)
    {
        // Run this less frequently than the network checks
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Collect logs every 5 minutes
                await Task.Delay(TimeSpan.FromMinutes(5), token);

                if (!token.IsCancellationRequested)
                {
                    LogMessage("Collecting recent event logs...");

                    // Collect WiFi/networking related events
                    await CollectNetworkEventLogsAsync();

                    // Collect hardware related events
                    await CollectHardwareEventLogsAsync();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"Error collecting event logs: {ex.Message}");
            }
        }
    }

    private static async Task CollectNetworkEventLogsAsync()
    {
        try
        {
            // Look for Wlan (WiFi) related events in the last 15 minutes
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[(Provider[@Name='Wlansvc'] or Provider[@Name='NETwNs64'] or " +
                $"Provider[@Name='Microsoft-Windows-WLAN-AutoConfig'] or Provider[@Name='Tcpip'] or " +
                $"Provider[@Name='Microsoft-Windows-NetworkProfile'])" +
                $" and TimeCreated[timediff(@SystemTime) <= {(long)TimeSpan.FromMinutes(15).TotalMilliseconds}]]]");

            var reader = new EventLogReader(query);
            int count = 0;

            for (EventRecord entry = reader.ReadEvent(); entry != null; entry = reader.ReadEvent())
            {
                try
                {
                    LogMessage($"Network Event: [{entry.LevelDisplayName}] " +
                              $"{entry.TimeCreated:yyyy-MM-dd HH:mm:ss} " +
                              $"{entry.ProviderName} - {entry.Id}: {entry.FormatDescription()}");
                    count++;
                }
                catch (Exception ex)
                {
                    LogMessage($"Error reading network event: {ex.Message}");
                }
            }

            LogMessage($"Collected {count} network-related events");
        }
        catch (Exception ex)
        {
            LogMessage($"Error collecting network events: {ex.Message}");
        }
    }

    private static async Task CollectHardwareEventLogsAsync()
    {
        try
        {
            // Sources for hardware events
            var hardwareSources = new[] {
                // General hardware sources
                "Microsoft-Windows-Kernel-PnP",
                "Microsoft-Windows-DeviceSetupManager",
                "Microsoft-Windows-Kernel-Power",
                "Microsoft-Windows-Power-Troubleshooter",
                
                // WiFi adapter specific
                "Microsoft-Windows-WLAN-Driver",
                "Microsoft-Windows-NDIS",
                "NDIS",
                
                // USB related (in case of USB WiFi adapters)
                "Microsoft-Windows-USB-USBHUB",
                "Microsoft-Windows-USB-USBPORT"
            };

            var sourceClause = string.Join(" or ", hardwareSources.Select(s => $"Provider[@Name='{s}']"));
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[({sourceClause}) and (Level=1 or Level=2 or Level=3)" +
                $" and TimeCreated[timediff(@SystemTime) <= {(long)TimeSpan.FromMinutes(15).TotalMilliseconds}]]]");

            var reader = new EventLogReader(query);
            int count = 0;

            for (EventRecord entry = reader.ReadEvent(); entry != null; entry = reader.ReadEvent())
            {
                try
                {
                    if (IsRelevantHardwareEvent(entry))
                    {
                        LogMessage($"Hardware Event: [{entry.LevelDisplayName}] " +
                                  $"{entry.TimeCreated:yyyy-MM-dd HH:mm:ss} " +
                                  $"{entry.ProviderName} - {entry.Id}: {entry.FormatDescription()}");
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Error reading hardware event: {ex.Message}");
                }
            }

            LogMessage($"Collected {count} hardware-related events");
        }
        catch (Exception ex)
        {
            LogMessage($"Error collecting hardware events: {ex.Message}");
        }
    }

    private static bool IsRelevantHardwareEvent(EventRecord entry)
    {
        // Filter for relevant hardware events
        try
        {
            // Always include errors and warnings
            if (entry.Level <= 3) // 1=Critical, 2=Error, 3=Warning
                return true;

            // Consider the event description - look for keywords that might indicate WiFi issues
            string desc = entry.FormatDescription()?.ToLower() ?? "";
            string[] relevantKeywords = new[] {
                "wifi", "wireless", "wlan", "802.11", "network adapter",
                "disconnect", "connect", "power", "sleep", "restart",
                "driver", "hardware", "device", "failed", "error"
            };

            return relevantKeywords.Any(keyword => desc.Contains(keyword));
        }
        catch
        {
            // If we can't analyze it, include it to be safe
            return true;
        }
    }

    private static async Task GenerateReportAsync()
    {
        var reportFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"WifiDisconnectReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        try
        {
            LogMessage("Generating summary report...");

            using var writer = new StreamWriter(reportFile);

            // Get application version
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = fileVersionInfo.ProductVersion ?? "1.0.0";

            // Write header
            await writer.WriteLineAsync("=== WiFi Disconnect Monitor Report ===");
            await writer.WriteLineAsync($"Version: {version}");
            await writer.WriteLineAsync($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync($"Monitoring period: {_startTime:yyyy-MM-dd HH:mm:ss} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync("=================================");

            // Add system info
            await writer.WriteLineAsync("\n=== SYSTEM INFORMATION ===");
            await writer.WriteLineAsync($"Computer Name: {Environment.MachineName}");
            await writer.WriteLineAsync($"OS Version: {Environment.OSVersion}");
            await writer.WriteLineAsync($"Processor Count: {Environment.ProcessorCount}");
            await writer.WriteLineAsync($".NET Version: {Environment.Version}");

            // Add network adapters info
            await writer.WriteLineAsync("\n=== NETWORK ADAPTERS ===");
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                await writer.WriteLineAsync($"\nAdapter: {nic.Name}");
                await writer.WriteLineAsync($"  Description: {nic.Description}");
                await writer.WriteLineAsync($"  Type: {nic.NetworkInterfaceType}");
                await writer.WriteLineAsync($"  Status: {nic.OperationalStatus}");
                await writer.WriteLineAsync($"  Speed: {nic.Speed / 1_000_000} Mbps");

                var props = nic.GetIPProperties();
                await writer.WriteLineAsync("  IP Addresses:");
                foreach (var ip in props.UnicastAddresses)
                {
                    await writer.WriteLineAsync($"    {ip.Address}/{ip.PrefixLength}");
                }

                await writer.WriteLineAsync("  DNS Servers:");
                foreach (var dns in props.DnsAddresses)
                {
                    await writer.WriteLineAsync($"    {dns}");
                }
            }

            // Add command line tools output
            await writer.WriteLineAsync("\n=== NETWORK DIAGNOSTICS ===");
            await writer.WriteLineAsync("\n-- IPCONFIG --");
            await RunCommandAndLogOutput("ipconfig", "/all", writer);

            await writer.WriteLineAsync("\n-- NETSH WLAN SHOW INTERFACES --");
            await RunCommandAndLogOutput("netsh", "wlan show interfaces", writer);

            await writer.WriteLineAsync("\n-- NETSH WLAN SHOW NETWORKS --");
            await RunCommandAndLogOutput("netsh", "wlan show networks", writer);

            await writer.WriteLineAsync("\n-- PING TEST --");
            await RunCommandAndLogOutput("ping", "8.8.8.8 -n 4", writer);

            // Add hardware info
            await writer.WriteLineAsync("\n=== HARDWARE INFORMATION ===");

            // Device manager hardware info
            await writer.WriteLineAsync("\n-- NETWORK ADAPTER DETAILS --");
            await RunCommandAndLogOutput("powershell", "-Command \"Get-NetAdapter | Format-List *\"", writer);

            // Driver information
            await writer.WriteLineAsync("\n-- NETWORK DRIVER DETAILS --");
            await RunCommandAndLogOutput("powershell", "-Command \"Get-NetAdapter | Get-NetAdapterAdvancedProperty | Format-Table -AutoSize\"", writer);

            // Check for driver issues
            await writer.WriteLineAsync("\n-- PROBLEM DEVICES --");
            await RunCommandAndLogOutput("powershell", "-Command \"Get-WmiObject Win32_PnPEntity | Where-Object{$_.ConfigManagerErrorCode -ne 0} | Select-Object Name, DeviceID, ConfigManagerErrorCode | Format-List\"", writer);

            // Include the full log
            await writer.WriteLineAsync("\n=== DISCONNECT LOG ===");
            await writer.WriteLineAsync(File.ReadAllText(LogFile));

            Console.WriteLine($"Report generated: {reportFile}");
            LogMessage($"Report generated: {reportFile}");
        }
        catch (Exception ex)
        {
            LogMessage($"Error generating report: {ex.Message}");
            Console.WriteLine($"Error generating report: {ex.Message}");
        }
    }

    private static async Task RunCommandAndLogOutput(string command, string arguments, StreamWriter writer)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                await writer.WriteLineAsync($"Failed to start process: {command} {arguments}");
                return;
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            await writer.WriteLineAsync(output);
        }
        catch (Exception ex)
        {
            await writer.WriteLineAsync($"Error running command: {ex.Message}");
        }
    }

    private static void LogMessage(string message)
    {
        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        // Write to file
        try
        {
            File.AppendAllText(LogFile, logEntry + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write to log file: {ex.Message}");
        }

        // Also write to console
        Console.WriteLine(logEntry);
    }
}