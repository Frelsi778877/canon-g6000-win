# Experimental Windows adapter for PIXMA G3100

This altered implementation reuses the SR5 codec and native Windows transport approach
from CanonG6000Read, which is based on [Jess Sullivan's canon-megatank-reset](https://github.com/Jesssullivan/canon-megatank-reset).
Original authors and zlib terms are preserved in [LICENSE](LICENSE).

**Validated on one G3100 with firmware V3.010**, after physical absorber service and
with the owner's authorization for an experimental repair. Read and reset were
confirmed by a counter read after a clean power-button cycle and normal printing.
This is evidence for that unit and firmware, not a guarantee for all G3000 variants.
See [the validation report](../../docs/g3100-validation.md).

## Requirements

- Windows with the native Canon/USB printer driver already installed.
- .NET SDK 8.0 or newer to build; .NET 8 runtime to run.
- USB access to the printer, usually from an administrator terminal.
- A physically serviced absorber and authorization to perform an experimental write.

No WIC executable, activation key, driver replacement or firmware flashing is needed.

## Build and offline tests

From the repository root:

```powershell
dotnet build adapters/windows-g3100/CanonG3100.csproj -c Release
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --selftest
```

The self-test never enumerates or opens a printer. It checks upstream cipher vectors,
G3100 wire fixtures, exact unit selection, status parsing, rejection paths and the
one-attempt write budget.

## Read the counter

Enter service mode with the physical panel: power off with the power button, hold
Stop, hold Power, release Stop, press Stop five times while holding Power, then
release Power. Wait for the steady green light. Enumeration must change from
`04A9:1794` (normal mode) to `04A9:12FE` (service mode).

```powershell
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --list
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --describe --unit YOUR_SERVICE_SERIAL
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --read --unit YOUR_SERVICE_SERIAL --experimental-g6000-profile --baseline before.json
```

Use the exact service-mode unit identifier from `--list`; substring matching and
ambiguous matches are rejected. The inherited profile flag is deliberately explicit.
The status must identify G3000/G3100 and contain a plausible `D=` field. `DL=`
alone is not accepted as the waste counter.

`before.json` is a **service status capture, not a complete EEPROM backup**. It is
created only after a successful read, bound to the exact unit and profile, with a
timestamp and status checksum. Existing captures are never overwritten.

## Dry-run and one experimental reset

```powershell
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --reset
```

That dry-run does not enumerate, open or send anything to a printer.

A real reset requires a matching capture less than 30 minutes old:

```powershell
dotnet adapters/windows-g3100/bin/Release/net8.0/CanonG3100.dll --reset --execute --unit YOUR_SERVICE_SERIAL --experimental-g6000-profile --baseline before.json
```

The adapter validates the capture before opening the unit, opens a fresh session,
drains leftover status blocks and requires the expected selector acknowledgment.
It then permanently reserves a one-write budget in `state/`, adjacent to the capture,
**before** sending the clear command exactly once. An uncertain reply leaves the
budget consumed. Do not delete the budget or lock records to repeat writes.

A decoded clear echo means accepted, not persisted. Power off cleanly with the
printer's Power button. Re-enter service mode and read to a **new** file to verify
that `D=` remains zero. Then power off and start normally to test printing.

Never assume that resetting the counter empties the absorber. The adapter has no
full EEPROM dump/restore operation, arbitrary-command interface, firmware update
or network/destination/serial-number reset.

## Implementation

- `Sr5Codec.cs`: inherited SR5 tables and upstream regression vectors.
- `NativeUsb.cs`: exact unit selection, three allowlisted usbprint IOCTLs,
  overlapped I/O, bounded replies, timeout/cancellation and deterministic disposal.
- `Program.cs`: CLI, status capture, preflight validation, one-shot clear and
  separation of command acceptance from post-power-cycle verification.
- `SelfChecks.cs`: offline safety tests and fixtures from the G3100 experiment.

Raw status and USB paths can contain serial numbers and factory/usage timestamps.
Keep local captures, baselines and logs private; the published validation excerpts
omit those identifiers.
