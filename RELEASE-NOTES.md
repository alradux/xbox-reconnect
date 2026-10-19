## Xbox Reconnect v2.2 - high-DPI layout fix

Fixes text clipping reported in [issue #1](https://github.com/alradux/xbox-reconnect/issues/1) at 175% display scaling.

- Text areas size themselves to the displayed text instead of using fixed pixel heights.
- Instructions and status messages wrap to the available space.
- Spacing and button sizes follow display DPI when the app opens.
- The window can be resized or maximized, fits within the working area on launch, and scrolls on smaller screens.
- Start/Stop service behavior and the reminder to restore GameInput before normal exit are unchanged.

**Download Xbox-Reconnect-v2.2.exe under Assets.** No installer or companion files are needed. If you previously enabled a System (Enhanced) DPI compatibility override for the app, turn it off when checking this version.

Validation: mock UI and service-logic checks passed. Automated layout checks passed at emulated 100%, 125%, 150%, 175%, and 200% metrics, including long status text and an 800 x 600 viewport. Real display settings and Windows services were not changed by these tests; confirmation on the reporter's physical 175% setup is still welcome.

The executable is unsigned and requires administrator access for service/device changes. This remains a workaround for the GameInput reconnect problem, not an official Microsoft fix. SHA256SUMS.txt identifies the release binary.
