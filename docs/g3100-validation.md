# PIXMA G3100 V3.010: Windows read/reset validation

Validation date: **2026-10-29**. Hardware: one Canon PIXMA G3100, reported as
G3000 by the service status, firmware **V3.010**. The owner confirmed prior physical
absorber maintenance and explicitly authorized the experimental repair.

The live experiment used the .NET 9.0.6 runtime. The published project targets
.NET 8; both that build and its offline self-tests were separately verified using
the installed .NET 8.0.17 runtime. No second physical reset was performed to test
a different runtime.

## Observed result

| Check | Observation |
|---|---|
| Normal USB identity | `04A9:1794` |
| Service USB identity | `04A9:12FE` |
| Status before reset | `D=063.6`, `DL=00000` |
| Selector acknowledgment | Decoded prefix `00 12 01 10 00 00` |
| Clear acknowledgment | Decoded prefix `00 12 01 0D 00 00` |
| After physical power-button cycle | Independent read: `D=000.0`, `DL=01000` |
| Normal startup | No 5B00; Windows reported no detected error |
| Physical output | Owner confirmed successful printing |

Only **one** clear command was sent. The meaning of the observed `DL=` change is
not established here; it is recorded without treating it as the `D=` counter.

The `--read` path sent the inherited session, selector and status-query commands,
and successfully decoded a G3000 banner and waste-counter field using the SR5 tables.
The reset then used the same session/selector transport and one clear operand.
A positive clear echo was followed by an independent read after a clean shutdown;
the claim is therefore stronger than an acknowledgment alone.

The button-only reset sequence did not work on this unit: after entering service
mode, Stop five times followed by Power simply turned it off. The successful path
used native Windows usbprint vendor IOCTLs and the adapted software.

## Reproducible evidence

`adapters/windows-g3100/SelfChecks.cs` contains short encrypted selector/clear
fixtures and per-session keyword bytes from this experiment, plus a byte-exact
outbound clear assertion. Those bytes are protocol fixtures, not account
credentials. The tests exercise the control flow through fake transport and cannot
touch hardware.

Personal serial numbers, USB instance identifiers, factory/printing timestamps,
local paths and complete raw status dumps were deliberately omitted. The excerpts
above preserve the model, firmware, counters and command-response facts needed
to assess the compatibility claim.

## Scope and limitations

The preflight record is a recent service status capture with a checksum, not a
complete EEPROM backup. The owner accepted that limitation for this experimental
repair. There is no EEPROM restore implementation.

This validates one physically serviced G3100 with V3.010. Other units, regional
variants and firmware revisions remain unvalidated. No paid tool was executed,
no activation key was purchased, no driver was replaced, and no firmware was flashed.

## Sources and attribution

- Protocol/cipher research: [Jesssullivan/canon-megatank-reset](https://github.com/Jesssullivan/canon-megatank-reset).
- Windows source: `Program.cs` blob `9a39b98e111abca822409277c20feb7134d32bca`
  in [Frelsi778877/canon-g6000-win](https://github.com/Frelsi778877/canon-g6000-win).
- The altered code retains the original zlib license and authors' notices.
