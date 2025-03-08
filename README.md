# WirelessDisconnectMonitor

A Windows utility application that monitors and logs wireless network disconnections, helping to diagnose connectivity issues.

## Overview

WirelessDisconnectMonitor is a .NET Windows application designed to track and analyze wireless network disconnection events. It helps users identify patterns and potential causes of wireless connectivity problems by monitoring network status and recording details about disconnection events.

## System Requirements

- Windows 10 or later
- .NET 8.0 Runtime

## Features

- Real-time monitoring of wireless network connections
- Logging of disconnection events with timestamps
- Detection of reconnection attempts
- Statistical analysis of disconnection patterns
- Notification system for extended outages

## Getting Started

### Prerequisites

- Windows operating system
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

## Usage

Once launched, the application runs in the background, monitoring your wireless connections. The system tray icon provides access to:

- View current connection status
- Access disconnection history
- Configure notification settings
- Export logs for analysis

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
