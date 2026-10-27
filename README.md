# Nyvora

A lightweight Windows desktop companion with an animated dynamic-notch pill. It rests as a small pill at the top-center of the primary display and expands on click.

**Status:** initial development (first milestone: the animated pill). Not ready for general distribution.

## Requirements

- Windows 10 (1809+) or Windows 11
- .NET 8 SDK
- To build the app locally: Visual Studio 2022 17.8+ with the ".NET desktop development" workload (or the .NET SDK alone; WinUI 3 is restored from NuGet)

## Build, test, run

```powershell
dotnet restore Nyvora.sln -p:Platform=x64
dotnet build Nyvora.sln -c Release -p:Platform=x64
dotnet test tests/Nyvora.Tests/Nyvora.Tests.csproj -c Release
dotnet run --project src/Nyvora.App -c Release -p:Platform=x64
```

`Nyvora.Core` (state machine, animation math) and its tests are plain `net8.0` and also build and test on Linux/macOS: `dotnet test tests/Nyvora.Tests`.

## Using it

- Click the pill to expand. Collapse with **Esc**, **Collapse**, or by clicking elsewhere.
- Right-click the pill (or use **Quit**) to exit. There is no taskbar entry by design.
- Logs: `%LOCALAPPDATA%\Nyvora\nyvora.log` (events and exceptions only; no network, no telemetry).

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and manual verification](docs/ROADMAP.md)

## Known limitations

See [docs/ROADMAP.md](docs/ROADMAP.md#known-limitations). In short: the visual and native-window behavior was written without being run on Windows yet and needs the manual checklist; edges are clipped with a window region (not anti-aliased); only the primary display is used.
