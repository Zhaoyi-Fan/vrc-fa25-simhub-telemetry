# VRC FA25 SimHub Telemetry

Bridge a formula car's **private CAN/ECU channels** from Assetto Corsa (Custom Shaders Patch)
into [SimHub](https://www.simhubdash.com/), so data that normally only exists on the in-car
dashboard — ERS strategy, deployment/regen, differential maps, brake bias & migration, PU
temperatures, MGU-K/H power — can be shown on an external dashboard or secondary screen.

Built for the VRC Formula Alpha 2025 car used in a private league. **League-safe by design:**
the in-game side is read-only and never edits a car file, so it has no checksum impact.

## How it works

Two halves connected by a shared **memory-mapped file** (`Local\AcTools.CSP.VRC_FA25.v1`):

```
Assetto Corsa (CSP Lua app)                         SimHub (C# plugin)
  reads car CAN/ECU channels    ──►  MMF  ──►   reads MMF, exposes as
  writes a fixed C-struct                        SimHub properties → dashboard
```

- **`ingame-exporter/`** — a standalone CSP Lua app. It reads the car's `..._CAN` data and
  script controller inputs and packs them into a fixed-layout struct in the MMF every frame.
  No car-file edits.
- **`simhub-plugin/`** — a SimHub `IDataPlugin` (net48) that maps the MMF struct to
  `VRC FA25 Telemetry` properties. Standard data (speed/rpm/tyres/fuel/gaps) stays SimHub-native.

The struct field **order and types must match** between the Lua `LAYOUT` string and the C#
`VrcData` struct — add or remove a channel and you update both.

## Install

### Option A — download (recommended, no build needed)

Grab both assets from the [latest release](../../releases/latest):

**1. In-game exporter** — `vrc_fa25_telemetry_ingame_app.zip`
   1. Extract the zip into `…\assettocorsa\apps\lua\`. You should end up with
      `…\assettocorsa\apps\lua\vrc_fa25_telemetry\manifest.ini` (and the `.lua` next to it).
   2. In game (with CSP installed and Lua apps enabled), open the apps sidebar and add the
      **VRC FA25 Telemetry** window on track.
   3. The window shows `Connected = 1` with an incrementing counter when it's feeding data.

**2. SimHub plugin** — `VrcFa25Telemetry.dll`
   1. Close SimHub if it's running.
   2. Right-click the downloaded DLL → **Properties** → tick **Unblock** (if shown) → OK.
      Windows marks downloaded DLLs as untrusted and SimHub may silently refuse to load them.
   3. Copy the DLL next to `SimHub.exe` (default: `C:\Program Files (x86)\SimHub`).
   4. Start SimHub → it asks about the new plugin → enable it → restart SimHub.
   5. Properties appear in the property picker under **VRC FA25 Telemetry** (search "Strat").

### Option B — build from source

See [`simhub-plugin/README.md`](simhub-plugin/README.md) for the build steps; the in-game
half is plain Lua (`ingame-exporter/` → copy as `apps/lua/vrc_fa25_telemetry/`), nothing to build.

## Notes

This repository contains only my own original tooling. It does **not** include any VRC mod
assets, car data, or third-party content. It is not affiliated with or endorsed by the VRC
Modding Team.

## License

MIT — see [LICENSE](LICENSE).
