# Microsoft Store submission: WinWidgetPerf

Copy-paste material for Partner Center (https://partner.microsoft.com/dashboard). Fill in the
sections in the order Partner Center shows them.

## Product identity (Product management > Product identity)

Copy these three values into the repo Actions **Variables** (Settings > Secrets and variables >
Actions > Variables) so the release workflow builds a matching Store package:

| Partner Center field | Repo variable |
| --- | --- |
| Package/Identity/Name | `MSIX_IDENTITY_NAME` |
| Package/Identity/Publisher (`CN=...`) | `MSIX_PUBLISHER` |
| Package/Properties/PublisherDisplayName | `MSIX_PUBLISHER_DISPLAY_NAME` |

Then push a `v*` tag. The workflow's build artifact contains `dist-store/WinWidgetPerf-<ver>-x64-store.msix`
and `-arm64-store.msix`. They are unsigned, which is what the Store expects. Upload both under
**Packages**.

## Properties

| Field | Value |
| --- | --- |
| Category | Utilities + tools (subcategory: none) |
| Privacy policy URL | https://landenlabs.github.io/win-widget-perf/privacy.html |
| Website | https://landenlabs.github.io/win-widget-perf/ |
| Support contact | https://github.com/landenlabs/win-widget-perf/issues |
| Collects personal information? | No |
| Product declarations | No, it does not access, collect or transmit personal information |
| System requirements | Windows 10 version 1809 (build 17763) or later; x64 or ARM64 |

## Age ratings (IARC questionnaire)

Category: **Utility / productivity**. Answer **No** to everything: no violence, no user
interaction or chat, no sharing of location or personal info, no purchases, no unrestricted web
access. Expected rating: 3+ / Everyone.

## Pricing and availability

Free. No in-app purchases. All markets.

## Restricted capability justification

Partner Center asks why the package declares `runFullTrust`. Suggested text:

> WinWidgetPerf is a WPF desktop app packaged with the Desktop Bridge. It needs runFullTrust to run
> as a normal Win32 process: it reads Windows performance counters (CPU, disk, memory), per-process
> CPU times, network adapter counters and thermal-zone and GPU temperature data, and it draws a
> transparent, always-on-desktop widget window. It makes no internet connections and collects no
> user data.

## Store listing (English, United States)

**Product name:** WinWidgetPerf

**Short description** (up to 270 characters):

> A lightweight, transparent desktop widget that charts your PC's CPU, disk, network, memory and
> temperature in real time, and names the process using the most CPU. No ads, no account, no
> telemetry.

**Description:**

> WinWidgetPerf puts a small, transparent performance chart right on your desktop, so you can see
> what your PC is doing at a glance without opening Task Manager.
>
> A scrolling stripchart shows CPU load and disk activity together, each in its own color, over a
> time span you choose. The title bar names the process using the most CPU, averaged over the
> chart window. Hover it to see the top three. Gauges show how busy your network connection is,
> how much memory is in use, and how warm your CPU and GPU are.
>
> Make it yours: pick the colors, opacity, size and font scale, choose which drive to watch, and
> turn the title, legend and grid lines on or off. Run several widgets, place them on any monitor,
> and WinWidgetPerf remembers where each one goes for each monitor layout. It can start
> automatically when you sign in.
>
> Private by design: WinWidgetPerf has no account, ads, analytics or telemetry, and it never
> connects to the internet. Everything it measures stays on your PC.
>
> Free and open source under the Apache 2.0 license.

**What's new in this version:** (fill in per release)

**Product features** (one per line, up to 20):

```
Real-time CPU and disk stripchart on your desktop
Shows the busiest process, averaged over the chart window
Network, memory, CPU and GPU temperature gauges
Transparent widget that sits on your wallpaper
Custom colors, opacity, size and font scale
Multiple widgets, multi-monitor aware
Optional start at sign-in
No ads, no account, no telemetry, no internet connections
```

**Search terms** (up to 7):

```
performance monitor
cpu monitor
desktop widget
system monitor
disk activity
temperature monitor
network monitor
```

**Copyright and trademark info:** © 2026 LanDen Labs

**Additional license terms:** Apache License 2.0,
https://github.com/landenlabs/win-widget-perf/blob/main/LICENSE

## Images

| Partner Center slot | File |
| --- | --- |
| Store logo, 1:1 (300 x 300) | `store-assets/tile-300x300.png` |
| Screenshots (Desktop; at least 1, recommended 1366 x 768 or larger) | See note below |

The Store requires desktop screenshots of at least 1366 x 768. The images in `screens/` are cropped
to single windows and are too small. Capture full-screen shots with the widget on the desktop, for
example one with the widget on a wallpaper and one with Settings open.
