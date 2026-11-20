---
title: Privacy Policy
---

# WinWidgetPerf Privacy Policy

**Effective date:** November 20, 2026
**Publisher:** LanDen Labs

WinWidgetPerf is a free Windows desktop widget that charts your PC's CPU, disk, network, memory and
temperature activity. This policy explains what the app reads, what leaves your computer, and what
is stored.

## Summary

- WinWidgetPerf has **no accounts, no ads, no analytics, no telemetry, no tracking and no crash
  reporting**.
- It is **free**. There are no fees, subscriptions or in-app purchases.
- LanDen Labs **does not collect, receive or store any of your data**. There is no WinWidgetPerf
  server.
- The app **makes no internet connections**. Everything it measures is read on your PC and shown
  only to you.

## Information the app reads on your PC

To draw its charts, WinWidgetPerf reads, locally and without administrator rights:

- total CPU load and disk activity, from Windows performance counters,
- the CPU time used by each running process, grouped by process name. It uses this to show the
  busiest process in the title bar. It reads only process **names and CPU time**, never what a
  process is doing, its window contents or its files,
- memory use and the free space on the drive you choose,
- the byte counters, link speed and default gateway (router) of your active network adapter, used
  to measure how busy the connection is. It **does not look at the content of your network
  traffic**,
- CPU temperature from Windows "thermal zone" counters and GPU temperature from the graphics driver
  (the same source Task Manager uses),
- the size and layout of your monitors, to place the widget.

None of this is sent to LanDen Labs or anyone else.

## Network activity

WinWidgetPerf doesn't connect to the internet, and it doesn't check for updates. If you installed
it from the MSIX package, Windows handles updates when you install a newer release.

The network gauge reads byte counts that Windows already keeps. To tell your networks apart, the app
asks Windows for your router's hardware (MAC) address, which may send a standard local ARP request
to the router on your own network. That request never leaves your local network.

## Actions you choose to take

Some features open another program only when you click them:

- **Resource Monitor** (right-click menu) opens the Windows Resource Monitor.
- The **open folder** button in Settings opens the app's data folder in File Explorer.
- The **GitHub** link in the **About** dialog opens the project page in your web browser.

## Information stored on your PC

WinWidgetPerf keeps a small amount of data in `%AppData%\WinWidgetPerf`:

- `settings.json`: your preferences, such as each widget's position on each monitor layout, size,
  colors, opacity, font scale, sample interval, chart duration, chosen disk drive, temperature
  thresholds, which chart elements are shown, and whether *Auto-start on login* is on.
- `network-max.json`: the fastest speed the app has measured on each network connection, so the
  network gauge can show "percent busy" for that connection. Each entry is labeled with the
  connection type (Wi-Fi or wired), your router's local IP address, and your router's hardware (MAC)
  address, so the app can tell your networks apart. These identify your router, not you, and they
  never leave your PC. To forget them, exit the widget and delete this file. The **open folder**
  button in Settings shows you where it is.

The busiest-process history is kept only in memory for the chart window and is never saved.

If you turn on *Auto-start on login* in the portable (zip) build, the app adds a `WinWidgetPerf`
entry to your user's Windows startup (Run) registry key. In the MSIX build it uses the Windows
*Startup apps* setting instead.

When the app is installed from the MSIX package, Windows keeps these files inside the app's private
package folder (under `%LocalAppData%\Packages\`) instead.

The app keeps no log files. This data never leaves your PC. To delete it, uninstall the app
(uninstalling the MSIX removes its package folder), or delete the `%AppData%\WinWidgetPerf` folder.

## Children

WinWidgetPerf is a general-purpose utility. It is not directed at children and collects no personal
information from anyone.

## License

WinWidgetPerf is open source under the
[Apache License 2.0](https://github.com/landenlabs/win-widget-perf/blob/main/LICENSE). You can read
all of its source code on [GitHub](https://github.com/landenlabs/win-widget-perf) and confirm what
this policy describes.

## Changes to this policy

If the app's behavior changes in a way that affects privacy, this page will be updated and the
effective date above will change. The history of this page is public on
[GitHub](https://github.com/landenlabs/win-widget-perf/commits/main/docs/privacy.md).

## Contact

For questions about this policy, open an issue at
[github.com/landenlabs/win-widget-perf/issues](https://github.com/landenlabs/win-widget-perf/issues).
