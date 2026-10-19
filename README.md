# Xbox Reconnect

![v2.2 EXE downloads](https://img.shields.io/github/downloads/alradux/xbox-reconnect/v2.2/Xbox-Reconnect-v2.2.exe?label=v2.2%20EXE%20downloads&displayAssetName=false&cacheSeconds=300)
![v2.1 EXE downloads](https://img.shields.io/github/downloads/alradux/xbox-reconnect/v2.1/Xbox-Reconnect-v2.1.exe?label=v2.1%20EXE%20downloads)

A small, portable Windows app for an Xbox controller that connects once after a PC restart, then shuts off shortly after reconnecting through an Xbox Wireless Adapter.

This is an independent community troubleshooting utility, not a Microsoft product or an official fix.

## What it does

- Shows live Windows XInput controller connection status, battery information when available, and input activity.
- Shows the installed GameInput Redist version and service status.
- **Stop** sets GameInput Redist Service to Disabled and stops it, preventing it from starting itself again through normal service-start requests.
- **Start** sets that service to Automatic and starts it again.
- After Stop, normal app closing is blocked until Start succeeds. The same reminder applies if the app detects that the service is already disabled or stopped.
- Can restart a specifically identified official Xbox Wireless Adapter.
- Can save a local text diagnostic report. Nothing is uploaded automatically.

## Why this workaround exists

On the author's tested Windows 11 PC, reconnecting worked after reboot with **GameInputRedistService 4.0.300.0 disabled**. Re-enabling the service reproduced a disconnect after approximately 1–2 seconds. Firmware updates, USB power settings, adapter restarts, and service restarts had not resolved the problem.

This is evidence of a service-related compatibility problem on that setup, not proof that every controller disconnect has the same cause. A game did not need to be running for the fault to occur.

## Display scaling

Version 2.2 fixes text clipping reported at 175% scaling in issue #1. Text areas now size to their contents, instructions wrap, and the window can be resized or maximized. Layout spacing follows the display DPI when the app opens; scrollbars keep content reachable on small work areas. If you previously applied a System (Enhanced) DPI compatibility override, turn it off when testing v2.2.

## Use

1. Download `Xbox-Reconnect-v2.2.exe` from this repository's **Releases** page.
2. Open it and accept Windows' administrator prompt. System service and device changes require administrator access.
3. If the controller will not reconnect, click **Stop**, then turn the controller on again.
4. Before closing, click **Start** to restore GameInput Redist to Automatic and running.

Some games depend on GameInput. Starting the service again may also bring the reconnect problem back. The exit reminder covers normal app closing; forced termination, a crash, or shutdown can bypass it. If needed, use Windows Services to set **GameInput Redist Service** to Automatic and start it.

Start intentionally selects **Automatic**; it does not restore an arbitrary previous startup setting. The separate Windows **GameInput Service** is not modified by the Start/Stop buttons.

## Requirements and scope

Windows 11 x64, with .NET Framework 4.8 (included with Windows 11). No installer or companion files are needed to run the release executable. This binary is unsigned.

Controller monitoring uses Microsoft's documented XInput APIs. XInput does not provide model or serial information here and can include virtual controllers. Adapter restart supports recognized Microsoft USB IDs `045E:02E6` and `045E:02FE`; it excludes Bluetooth, Wi-Fi, and USB hubs. All devices using the selected Xbox dongle disconnect briefly when it is restarted.

## Build from source

On Windows x64, open PowerShell in this folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script uses the Windows .NET Framework C# compiler. The executable is written to `dist/Xbox-Reconnect-v2.2.exe`. No NuGet downloads are required. The execution-policy option applies only to that process.

To also run the existing logic and UI checks without changing real services:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Logic checks include startup-mode handling, simulated partial-write rollback, state verification, and the exit guard. UI checks use mock service state. DPI checks emulate 100%, 125%, 150%, 175%, and 200% font/layout metrics and inspect label bounds, long status messages, and scrolling at 800 x 600; they do not change Windows display settings. This does not replace confirmation on the reporter's physical 175% display. Actual service mutations require testing on the target PC; they are not performed by these tests.

## Reporting a problem

For this app, open a GitHub issue with the app version, Windows version from `winver`, symptoms, and relevant report excerpts. Remove personal paths and unique device identifiers before publishing reports.

For GameInput itself, report through Windows Feedback Hub and the [GameInput issue tracker](https://github.com/microsoftconnect/GameInput/issues).

## Source files

- `Core.cs`: controller/adapter discovery and application entry point.
- `ReconnectForm.cs`: interface, monitoring, Start/Stop workflow, and close reminder.
- `ServiceSettings.cs`: Windows Service Control Manager configuration and control calls.
- `SettingsTests.cs`: mock logic checks.
- `DpiLayoutTests.cs`: scale, text-fit, resize, and scrolling regression checks.
- `XboxReconnect.manifest`: administrator elevation and Windows compatibility declarations.
- `XboxReconnect.ico`: application icon.

No license has been selected for this repository.
