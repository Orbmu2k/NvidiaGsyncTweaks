# NVIDIA display driver: hidden VRR registry values (analysis of nvlddmkm.sys 621.12)

Everything here was derived by reverse engineering `nvlddmkm.sys` **32.0.16.2112 (621.12)**, `nvapi64.dll` and `nvapi64_impl.dll` of the same
driver package. Addresses are RVAs. Names / offsets / defaults are exact (read from the driver's own table); the *effect* of each value is
rated high / medium / low confidence. NVIDIA can change or remove every value in any driver update.

## 1. How the values are read

* The display driver ("DD") builds a table of **248 settings** in function `16A2A90`. Every entry is 0x18 bytes:
  `{ wchar_t* name; DWORD* dest; DWORD default; }` where `dest` is a field of the settings object (`[[adapter]+0x49B0]`).
* A loop at `16A4D93` calls `195C7C0(name, dest)` for all entries. That reads a **REG_DWORD** (a 4-byte REG_BINARY is also accepted)
  from the **adapter's driver registry key** (`ZwOpenKey` on the path stored at `adapter+0x4568`, `ZwQueryValueKey`). If the value does not
  exist, the default from the table is stored.
* The key is `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000N`.
  Windows hands the miniport `...\Control\Video\{adapter GUID}\0000`, which is a registry symbolic link (REG_LINK) to that class key.
* The settings are read **once when the adapter starts** -> restart the display driver (or reboot) after changing them.

## 2. Finding the correct key (NVAPI)

`\0000` is *not* guaranteed (several adapters, iGPU, re-installs). The app asks NVAPI:

| NVAPI function | interface id | used for |
|---|---|---|
| `NvAPI_Initialize` / `NvAPI_Unload` | `0150E828` / `D22BDD7E` | |
| `NvAPI_EnumPhysicalGPUs` | `E5AC921F` | GPU handles |
| `NvAPI_GPU_GetFullName` / `GetBusId` / `GetBusSlotId` / `GetPCIIdentifiers` | `CEEE8E9F` / `1BE0B8E5` / `2A0A350F` / `2DDFB66E` | GPU name, PCI location + ids |
| `NvAPI_SYS_GetDriverAndBranchVersion` | `2926AAAD` | e.g. 62112 = 621.12 |
| `NvAPI_EnumNvidiaDisplayHandle` / `NvAPI_GetPhysicalGPUsFromDisplay` | `9ABDD40D` / `34EF9506` | display -> GPU |
| **`NvAPI_GetDisplayDriverRegistryPath`** | **`0E24CEEE`** | `(NvDisplayHandle, char[256])` -> `System\CurrentControlSet\Control\Video\{GUID}\0000` |

The reported path is resolved as a registry link (`RegOpenKeyEx(REG_OPTION_OPEN_LINK)` + `SymbolicLinkValue`) to the real class key, and
cross-checked with the PnP display adapter that matches NVAPI's PCI bus/slot (`CM_Get_DevNode_Registry_Property`: `BUSNUMBER`, `ADDRESS`, `DRIVER`).
The driver version of the key (`DriverVersion` = `32.0.16.2112`) is compared with NVAPI's version (`16.2112` -> `62112`).
GPUs without an attached display have no display handle; they fall back to the PCI match.

All ids were verified against the interface table of `nvapi64_impl.dll` (`nvapi_QueryInterface` -> 16-byte entries `{ptr, id}` at RVA `504000`, 2099 entries).
`NvAPI_GPU_GetPerGpuRegistryPath` could not be found: no public id table lists it, and NVAPI ids are not hashes of the names.

## 3. `vrrFrameSplitBehavior` (default `0x80000055`) - LFC multiplier selection

Consumers: `D928E0` (per-display chooser) and an inlined copy in `D93097-D9375D` (flip-metering scheduler).

For a frame time `T`, multipliers `n = 1, 2, 3 ...` are tried; `T/n` must lie in `[minFT, maxFT]` (`1/maxRefresh .. 1/VRRmin`).

```
mid = (minFT + maxFT) / 2
dist(n) = |T/n - mid|;   if n == multiplier currently in use and bit17 clear:  dist *= (value & 0xFF) / 100
default (bit16 clear): n with the smallest dist wins
bit16 set           : the first (lowest) n whose T/n fits wins
```

| bits | meaning |
|---|---|
| 0-7 | stickiness: `% x distance` of the multiplier in use. 85 (0x55) = 15 % bonus, 100 = neutral, 0 = never leave while it still fits, >100 = penalty |
| 16 (`0x10000`) | lowest-fit mode (what the community fix `0x80010055` sets) |
| 17 (`0x20000`) | disable stickiness |
| 31, 8-15, 18-30 | never read |

The model in `Model/LfcModel.cs` reproduces the numbers of the original Reddit analysis exactly
(120 Hz: 50.0/52.8, 144 Hz: 52.5/55.5, 240 Hz: 58.4/61.6, 360 Hz: 61.8/65.2 FPS; `0x80000032` @360 Hz: 56.5/70.6;
30 FPS @360 Hz: 3x default vs 2x lowest-fit). Engage threshold with stickiness `s`: `T > mid*(1+s)/(s+0.5)`, release `T < mid*(1+s)/(1+s/2)`.

## 4. All VRR-related values (settings-object offset, default)

"UI" = page of this app that exposes it. Confidence is about the *effect*.

| value | off | default | effect | conf. | UI |
|---|---|---|---|---|---|
| `vrrFrameSplitBehavior` | 2018 | 0x80000055 | see section 3 | high | LFC |
| `enableVrrFrameSmoothing` | 1FBC | 3 | **not** a bitmask: `== 3` enables the flip-delay smoothing (`DC6C85`), anything else disables it | high / med | Smoothing |
| `vrrDeflickerMaxUs` | 200C | 3000 | upper bound (us) for the delay added to one flip; 0 = smoothing off | high / med | Smoothing |
| `vrrMonAdj` | 2024 | 50 | % of the measured even/odd frame imbalance corrected per flip | med | Smoothing |
| `vrrDeflickerMaxPulsar` | 2010 | 60 | on panels flagged "Pulsar" in NVIDIA's panel DB: cap = % of the minimum frame time | high / med | Smoothing |
| `vrrDecayCoefficient` | 2020 | 30 | EMA weight (%) of the newest frame in the frame-time estimate; 1..100 | high | Smoothing |
| `anomalousFrametimeDetectionCooldown` | 2034 | 10 | a frame > 5x the estimate is ignored once, then the filter pauses N frames; 0 is dangerous | high | Smoothing |
| `enableVrrCursorRelease` | 1FC4 | 1 | arms a cursor-only-flip timer while VRR is idle | med | Cursor |
| `minVrrCursorFPS` | 1FC8 | 30 | period of that timer = 1000/value ms; 0 = never | high / med | Cursor |
| `vrrCursorMarginUs` | 2028 | 1500 | cursor flip only if the next game frame is expected later than now + margin | med-high | Cursor |
| `vrrSmartDetection` | 1FFC | 0 | bit0 slideshow, bit1 entropy, bit2 head mask (**required** for any effect) | med-high | Detection |
| `vrrSlideShowModeThresholdMs` / `...PeriodMs` | 1FE0 / 1FE4 | 1000 / 2000 | frame time that counts as slideshow / how long the mode holds | high | Detection |
| `vrrEntropyThreshold` / `Decrease` / `Decay` / `Max` / `DelayPeriodMs` | 1FE8-1FF8 | 100 / 30 / 1000 / 6000 / 500 | jitter score: `S = clamp((S + max(0, T/Tref - 1)) * Decay/1000 - Decrease/100, 0, Max/100)`; above Threshold/100 the mode holds for DelayPeriodMs | high | Detection |
| `vrrHysteresisParam` | 201C | 500000 | ns below maxFT before the "out of VRR range" state is released; dormant unless smart-detection bit2 | med | Detection |
| `vrrDeflickerMarginUs` | 2014 | 1000 | **no consumer** (a hard-coded 1 ms constant is used) | med | - |
| `enableVrrLowFreqFrameTimeEstimation` | 1FC0 | 0 | only copied into a caps struct; overwritten by the driver | high | - |
| `vrrNBTestMode` | 1FD4 | 0 | notebook test hook that force-enables VRR on a panel id | high | - |
| `dumpVrrInfoIntervalMs` | 202C | 0 | debug logging | med-high | - |
| `enableVrrFlipMetering` | 2298 | 3 | bit0/bit1: flip-metering (frame-count pacing) for flips that carry / do not carry a count | med | - |
| `VrrNBFMinOverride` | (string, not in table) | absent | REG_SZ `"Hz*us;Hz*us"` (<=4 pairs): overrides the VRR floor (max frame time in us) per refresh rate - notebook panels only | med-high | - |

### Mode, detection and timing values

| value | off | default | effect | conf. | UI |
|---|---|---|---|---|---|
| `VRRAlwaysOn` | 2048 | 0 | OR-ed with the per-display "application requested VRR" flag in the VRR state machine (`DD8F40`, `1544230`): VRR stays engaged without a request (desktop / windowed) | med-high | VRR |
| `forceVrrTearingMode` | 2000 | 0 | per-flip "may tear" override (`D937C0`): 0 = per-layer flag, 1 = force tearing, 2 = never tear | medium | VRR |
| `enableAdaptiveSync` | 2008 | 0x17 | bitmask: 0x01 and 0x02 = DP Adaptive-Sync detection (identical in code), 0x04 = allow VRR validation with >1 display, 0x10 = HDMI VSDB path; 0x08/0x20/0x40... unused | high (bits) / med (naming) | VRR |
| `disableOSVRR` | 221C | 0 | no "OS VRR" capability bit (0x800) in the monitor caps (`16D92FF`), the OS-VRR state sweep is skipped; NVIDIA's own G-SYNC path is unaffected | med-high | VRR |
| `vrrMaxRRx1k` | 1FA0 | 0 | VRR max-refresh **cap in mHz** (240000 = 240 Hz): the driver pads vtotal to `pclk_kHz*1e6/(htotal*cap)` lines; 0 = no cap | high | VRR |
| `vrrVblankExtension` | 1F9C | 0x3FFF | extra vblank lines for the slowest VRR frame; **only** used when the monitor's minimum refresh is unknown / 1 Hz (G-SYNC-module style), otherwise computed from the panel's min refresh | med-high | VRR |
| `GsyncTimingOptimizationPolicy` | 1F90 | 0 | bit0 = 1 prevents monitor flag 0x400 ("G-SYNC timing optimisation": modeset picks a pre-optimised timing from the monitor table) | med-low | VRR |
| `bIgnoreBlankingReqForDRR` | 2030 | 0 | allows VRR / virtual refresh on modes with vblank < ~25 lines + 280 us (`16799E0`) | medium | VRR |
| `vblCooldownTimeMs` | 2274 | 10000 | rate limit / retry delay of the "VBL driver policy" RM control (`0x731386`) and VRR re-update | medium | VRR |
| `enableVrrDisplayValidation` | 1FB8 | 0 | stricter `ValidateVrr` (`1543420`): fails when the display group is not already in the VRR set | low-med | VRR |
| `minimumFramePeriod100Ns_FRL21` | 20B4 | 0 | fallback minimum frame period (100 ns) for flips without one - a driver FPS limiter that only acts when VRR is **not** active | medium | - |
| `EnableEdpMinRefreshOverride` | 1FA4 | 0 | only value 1: per-panel quirk table replaces min refresh on internal / eDP panels (laptops) | medium | - |
| `enableVrrExtendedScan`, `forceVrrExtendedActiveLines`, `enableVrrModeFilter`, `enableVrrMethodMode`, `allowVRROverVideoBridge`, `SetPanelRefreshRate` | 1FAC 1FB0 1FB4 20D0 20B8 1F7C | 1,0,1,1,0,0 | **no consumer found** (legacy / unused) | med-high | - |

### Resource-manager (RM) keys

The RM reads its ASCII-named values with helper `1708F0`: first from the **same per-adapter driver key** (`IoOpenDeviceRegistryKey(PLUGPLAY_REGKEY_DRIVER)`, first DWORD of the value),
then from `...\Services\nvlddmkm`, then from an RM-internal store. (Not `...\nvlddmkm\Global`.) They are read once when the display RM is constructed (`149350`).

| value | effect | conf. | UI |
|---|---|---|---|
| `RmVrrMaxFrameDeltaOverride` | ms (x1e6 -> ns), 0 = 100 ms: clamp on the look-ahead of the per-head vblank / VRR wake-up estimator (`666A03`) | med (mechanism) / low (effect) | VRR (experimental) |
| `RmInternalVrrMclkOn2h1or` | 0 disables (default on for display version >= 0x27): keeps MCLK on for VRR with two heads on one OR (`7438D0`) | med / low-med | VRR (experimental) |
| `RmCLFCDisable` | 1 disables the "CLFC" RM sub-engine; no link to VRR visible | high (semantic) / low (purpose) | - |
| `RMDisableVESADisplayID20`, `RMEnableMSCGConfig`, `RmDisableVblankMscg`, `RmMscgLowFpsWarConfig`, `RmForceEnableDisplayMscg`, `RMOnDemandVBlank` ... | display power-gating / DisplayID 2.0 switches that can interact with low-FPS VRR flicker | low | - |

How the smoothing works (`D92A90`, called from the present path `DC5120`): the driver keeps, per display, a running sum of
`(even-flip interval - odd-flip interval)` in two slots. Before a flip it returns
`ext = min(maxFT - minFT, vrrDeflickerMaxUs, vrrMonAdj% x imbalance)` (cut so the flip stays >= 1 ms before the panel's own timeout) and pushes the flip later by `ext`.
An alternating A/B pattern is therefore stretched until both are equally long, as long as the difference is below the cap.

Smart detection (`D93A00`): per flip, a 32-entry interval ring feeds the slideshow and entropy detectors. When either triggers (and bit2 is set) the driver
sends event 2, which writes `{max frame duration := min frame duration}` instead of `{maxFT - 250 us}` - i.e. constant maximum refresh instead of VRR
(medium-low confidence: the HAL callee was not resolved). A hard-coded list of panels (HPN 0x3380, ACR 0x0579, AOC 0x3525 -> 27 ms; ACI 0x27B1, LGD 0x049B/0x0570/0x05B5, AUO 0x60ED/0x20EB -> 100 ms)
forces detection on and overrides the slideshow threshold.

## 5. Restarting instead of rebooting

`SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, DICS_PROPCHANGE)` on the display adapter (what `pnputil /restart-device` does). The kernel driver is unloaded and loaded again,
so it re-reads the registry. Side effects: screens go black for a few seconds, windows are rearranged, running games / GPU compute / capture software can lose the device,
HDR / resolution / G-SYNC are renegotiated. Needs administrator rights.
