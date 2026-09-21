# krocket-document-scanner

A desktop app for scanning documents on Linux.

Put a page on your scanner, press **Preview**, choose the area you want, press **Scan**, and save the pages
as a PDF, PNG or JPEG. It is a small utility and is meant to look and feel like a normal part of your desktop.

> The project is under development. Some parts have not been tested on real hardware yet (see
> [Testing](#testing)).

## What it does

- Finds scanners connected by USB and scanners on your local network, or lets you add a network scanner by
  its address.
- Shows a preview of the page before you scan.
- Lets you choose the paper size (A4, Letter, Legal, A5 or your own size) and drag the crop area.
- Scans in colour, grayscale or black and white, at the resolution you choose.
- Can straighten pages automatically and skip blank pages.
- Supports a flatbed and a document feeder. Feeder scanning has not been tested on a real scanner yet.
- Keeps all scanned pages in a list, where you can move or remove them before saving.
- Saves the pages as one PDF file, or as PNG or JPEG images.

## Requirements

- Linux with a desktop. It uses the X11 display system, and on Wayland it runs through XWayland.
- [SANE](http://www.sane-project.org/) and the `sane-airscan` backend, for scanner access.
- Avahi with mDNS name lookup, to find network scanners and to use names such as `printer.local`.
- fontconfig and the basic X11 libraries.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download), to build and run it from source.


## To Build and run

1. Checkout project

2. install dependencies with:

```console
sudo ./scripts/install-dependencies.sh
```

3. Build & Run

```console
dotnet build
dotnet run --project src/KRocketDocumentScanner.App
```

### Options

| Option | What it does |
|---|---|
| `--manage` | Opens only the scanner list (add, remove and check scanners), with no scan window |
| `--lang <code>` | Uses that language, for example `--lang fr`. Without it the system language is used |
| `--theme <dark or light>` | Forces the dark or light look. Without it the system setting is used |
| `--no-system-titlebar` | Uses the app's own title bar instead of the one from your desktop |

Only one copy of the app runs at a time for each user.

## Your data and settings

Everything stays on your computer, in `~/.config/krocketdocumentscanner/` (or in `$XDG_CONFIG_HOME/krocketdocumentscanner/`):

- `scanners.json`: the list of scanners you added.
- `settings.toml`: settings you can edit by hand. It is created the first time the app starts. For now it holds
  one setting, `network_timeout_seconds`, the longest the app waits for a scanner to answer. The default is 45.
- `network-activity.log`: a plain-text record of every time the app contacted a scanner.

## Privacy

The app has no telemetry or analytics. The only network
traffic is between the app and your own scanners. 

## Testing

```console
# tests that need no scanner
dotnet test --project tests/KRocketDocumentScanner.Tests --filter-not-trait "Category=Hardware"

# tests that use a real, switched-on scanner
dotnet test --project tests/KRocketDocumentScanner.Tests --filter-trait "Category=Hardware"
```

## License

This program is free software, licensed under the GNU General Public License, version 3. See [LICENSE](LICENSE).
The libraries it uses and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
