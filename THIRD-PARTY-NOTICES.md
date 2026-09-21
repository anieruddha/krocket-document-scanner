# Third-party notices

This application is licensed under the GNU General Public License, version 3. See `LICENSE`.

It is built on the following libraries. Each keeps its own license and copyright. Thank you to their authors.

| Library | Used for | License |
|---|---|---|
| [NAPS2.Sdk](https://github.com/cyanfish/naps2) | Finding scanners and scanning | GNU LGPL 2.1 or later |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (with Avalonia.Desktop and Avalonia.Themes.Fluent) | Windows and controls | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Drawing images and writing PDF files | MIT |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Image handling | Six Labors Split License 1.0 (Apache 2.0 terms apply because this application is open source) |
| [Tomlyn](https://github.com/xoofx/Tomlyn) | Reading the settings file | BSD-2-Clause |

The exact versions are listed in the project files (`*.csproj`).

## NAPS2 (LGPL)
The scanning code is provided by the NAPS2 SDK, which is licensed under the GNU Lesser General Public
License, version 2.1 or later. The SDK is used unmodified, as a separate package that this application loads.
Its source code is available at https://github.com/cyanfish/naps2. 

## ImageSharp
SixLabors.ImageSharp is used under the Apache License 2.0 terms of the Six Labors Split License, which apply to
software distributed under an open source license. Full text: https://github.com/SixLabors/ImageSharp/blob/main/LICENSE

## System components
Scanners are reached through SANE and the sane-airscan backend, which the user installs on their system. They
are not part of this source code.
