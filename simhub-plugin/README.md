# VRC FA25 Telemetry — SimHub plugin

Reads the shared memory-mapped file `Local\AcTools.CSP.VRC_FA25.v1` published by the in-game
CSP exporter app (`apps/lua/vrc_fa25_telemetry`) and exposes the VRC FA25 private channels as
SimHub properties (Strat, PuMode, DiffEntry/Mid/Exit, BrakeBias…, ERS deploy/regen, MGU-K/H
power, maps, wear, etc.). Standard data (speed/rpm/tyres/fuel/ERS%/gaps) stays SimHub-native.

## Two halves of the pipe
1. **In game:** the CSP exporter app must be active on track (writes the MMF). See its window.
2. **SimHub:** this plugin reads the MMF and publishes `VRC FA25 Telemetry` properties.

## Install (prebuilt)
Download `VrcFa25Telemetry.dll` from the [latest release](../../../releases/latest), unblock it
(right-click → Properties → **Unblock**), copy it next to `SimHub.exe`, then start SimHub and
enable the plugin when prompted. Full steps in the [root README](../README.md#install).

## Build (from source)
Requires .NET SDK (or MSBuild/Visual Studio) and a SimHub install (for the reference DLLs).

1. Edit `VrcFa25Telemetry.csproj` → set `<SimHubPath>` to your SimHub folder if it isn't
   `C:\Program Files (x86)\SimHub`. Optionally set `<CopyToSimHub>true</CopyToSimHub>`.
2. Build:
   ```
   dotnet build -c Release VrcFa25Telemetry.csproj
   ```
   Output: `bin\Release\VrcFa25Telemetry.dll`.
3. Copy `VrcFa25Telemetry.dll` into the SimHub folder (next to `SimHub.exe`) if not auto-copied.
4. Start SimHub → it will ask to enable the new plugin → enable it → restart SimHub.

## Use
- Properties appear in the SimHub property picker under **VRC FA25 Telemetry** (search e.g. "Strat").
- Put them on any dashboard for your 7" screen. Format there (×100 / decimals / colours) —
  the plugin sends raw values, plus a few `*Pct` convenience properties.
- `Connected` = 1 and `Counter` incrementing means the in-game exporter is feeding data.

## Notes
- Struct layout in `VrcFa25TelemetryPlugin.cs` (`VrcData`) **must** match the `LAYOUT` string in
  `vrc_fa25_telemetry.lua`. If you add/remove channels, update both in the same order.
- Plugin target framework: net48 (SimHub runs on .NET Framework).
- No admin needed; MMF is opened read-only.
