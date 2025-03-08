# WirelessDisconnectMonitor

Quick and dirty log maker (saved to working dir) for troubleshooting a sketchy wifi.

## Overview

- Windows 10 or later
- .NET 8.0 Runtime

### Installation

1. Clone the repository:
   ```
   git clone https://github.com/yourusername/WirelessDisconnectMonitor.git
   ```

2. Build the application:
   ```
   cd WirelessDisconnectMonitor
   dotnet build
   ```

3. Run the application:
   ```
   dotnet run --project WirelessDisconnectMonitor
   ```

4. Publish the application:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true /p:IncludeNativeLibrariesForSelfExtract=true
   ```
