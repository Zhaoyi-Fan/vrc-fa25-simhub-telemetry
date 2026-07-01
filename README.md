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

1. **In-game app:** copy `ingame-exporter/` into
   `…/assettocorsa/apps/lua/vrc_fa25_telemetry/`, then enable it in CSP and add its window
   on track. `Connected = 1` with an incrementing counter means it's feeding data.
2. **SimHub plugin:** build it and drop the DLL next to `SimHub.exe`. See
   [`simhub-plugin/README.md`](simhub-plugin/README.md) for the build/enable steps.

## Notes

This repository contains only my own original tooling. It does **not** include any VRC mod
assets, car data, or third-party content. It is not affiliated with or endorsed by the VRC
Modding Team.

## License

MIT — see [LICENSE](LICENSE).
