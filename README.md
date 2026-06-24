# Remote Resource Transfer (RRT)

Wireless resource transfer between unconnected vessels within 1 km — landed, splashed, flying, or orbiting. No pipes, no docking ports, no physical connection needed.

**KSP 1.12.x** · **No dependencies** · **MIT**

## Features

- **Wireless transfer** — select source/destination vessels and transfer resources with sliders
- **1 km range** — configurable (`maxTransferRange` in source)
- **Works everywhere** — launchpad, Mun surface, low Kerbin orbit — anywhere in physics range
- **Toolbar GUI** — toggle via the stock Application Launcher (green arrow icon, generated at runtime)
- **Per-resource sliders** — transfer exactly the amount you want
- **Smart limits** — slider max is the lesser of `source available` and `destination spare capacity`
- **EC cost** — small ElectricCharge drain from the active vessel (`0.5 + 0.001/unit`, configurable)
- **Bypasses crossfeed rules** — intentionally transfers between any parts containing the resource

## Installation

1. Download `RemoteResourceTransfer-*.zip` from the [latest release](https://github.com/jacobjuneau6/KSPRemoteResource/releases/latest)
2. Extract into your KSP `GameData/` folder
3. Launch KSP → enter Flight scene → click the green arrow toolbar button

```
KSP_install/
  GameData/
    RemoteResourceTransfer/
      Plugins/
        RemoteResourceTransfer.dll
      Localization/
        en-us.cfg
      RemoteResourceTransfer.version
```

## Usage

1. In Flight scene, click the green toolbar button on the right
2. Choose a **Source** vessel and a **Destination** vessel from the within-range list
3. Adjust sliders for each resource — slider range is `0 → min(avail, spare_capacity)`
4. Click **TRANSFER**
5. Status message confirms success, reports errors, or tells you nothing was moved

## Building from source

**Requirements:** KSP 1.12.x installed, plus one of: `dotnet` (≥ 6), `msbuild`, or `xbuild`.

```bash
# Linux / WSL
./build.sh Release

# Or manually:
export KSP_DIR="/path/to/Kerbal Space Program"
dotnet build Source/RemoteResourceTransfer.csproj -c Release /p:KSP_DIR="$KSP_DIR"
```

The DLL lands at `GameData/RemoteResourceTransfer/Plugins/RemoteResourceTransfer.dll`.

> **Note:** You need KSP installed because the mod references `Assembly-CSharp.dll` from `KSP_Data/Managed/`. This DLL is not open-source and can't be bundled here.



## Configuration

Edit fields at the top of `Source/RemoteResourceTransfer.cs`:

| Field | Default | Description |
|-------|---------|-------------|
| `maxTransferRange` | `1000f` | Transfer radius in metres (max ~2500 for physics range) |
| `electricCostPerUnit` | `0.001` | EC drained per unit of resource transferred |
| `electricCostBase` | `0.5` | Flat EC cost per transfer operation |

## Licence

MIT — do whatever you want with it.
