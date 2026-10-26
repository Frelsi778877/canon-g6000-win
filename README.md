# CanonG6000Read

A small, native **Windows C#** tool to **read** (and optionally **reset**) the
ink‑absorber / "waste ink" counter on **Canon PIXMA G6000‑series** MegaTank
printers (e.g. **G6020 / G6040 / G6050**) — the counter behind the
**`5B00` / "ink absorber is full"** lockout.

- **No WicReset, no per‑printer key, no vendor cloud.**
- Talks directly to the printer's `usbprint` **service‑mode** interface over the
  documented `VENDOR_SET`/`VENDOR_GET` IOCTLs.
- **Read is the proven path.** Reset is included but **experimental** (see Status).

> ⚠️ **Right‑to‑repair, for hardware you own.** This resets a counter the printer
> uses to refuse printing after it decides the absorber is "full". Resetting the
> counter clears the *software* lock — it does **not** empty the physical absorber
> pad. For heavy use, service/replace the pad, or ink can eventually overflow.

## Status

| Feature | State |
|---|---|
| Read waste counter (`D=` / `DL=`) | ✅ Working, validated byte‑exact on a real G6040 |
| Cipher (functor‑3 + functor‑2) | ✅ Self‑test proves it byte‑exact (`--selftest`, no hardware) |
| Reset (`--reset --execute`) | 🧪 Experimental — frames are byte‑exact and the printer is asked to confirm (`0D 00 00` echo), but the on‑EEPROM commit is still being verified on G6040. Validated on the sibling **G6020** by the upstream project. |

If you test the reset on your own G6000‑series printer, please open an issue with
the result (model, firmware, whether `D=` dropped after a clean power‑cycle).

## Requirements

- Windows, **.NET SDK 8.0+**
- The printer connected by **USB** and in **service mode** (it then enumerates as
  USB `04A9:12FE`). Service‑mode entry is a button combo on the printer; for the
  G6000 series it is typically: power off → hold **Stop** → hold **Power** →
  release **Stop** → press **Stop ×5** → release **Power**.
- Run the tool **as Administrator** (opening the usbprint device needs it).

## Build

```sh
dotnet build -c Release
```

## Use

Cipher self‑test only (no printer, no admin needed):
```sh
dotnet run -c Release -- --selftest
```

Read the counter (admin, service mode):
```sh
CanonG6000Read.exe
# -> WASTE ABSORBER (D): 13.0%   (DL raw: 00870)
```

Reset — **dry‑run first** (prints the exact frames, sends nothing):
```sh
CanonG6000Read.exe --reset
```

Reset for real (writes the printer; experimental):
```sh
CanonG6000Read.exe --reset --execute
```
Then **power‑cycle the printer with its power button** (clean shutdown) to commit
the write and leave service mode. An abrupt USB unplug does **not** commit.

## How it works

The G6000 service protocol runs over the `usbprint` vendor control channel:

| step | wire | meaning |
|---|---|---|
| `set_session` | SEND `81 00 00 03` | open the service session |
| `get_keyword` | RECV `82 00 00` | printer returns a fresh 3‑byte session keyword |
| selector | SEND `85 00 00` + enc(`10 07 7C`) | address the waste/status rows |
| read / status | SEND `85 00 00` + enc(`15`) → RECV `86 00 00`×N | enciphered 20‑byte blocks → decrypt → ASCII |
| clear (reset) | SEND `85 00 00` + enc(`0D 00 00`) | zero the 'common' absorber counter |

Each `set_command` payload is enciphered with Canon's G6000‑family **functor‑3**
scheme (a fixed‑LCG envelope + a keyword‑seeded XOR keystream with a permutation),
keyed per session by the live `get_keyword` value. The cipher and its tables are
embedded and self‑tested against known vectors.

## Credits & license

The service‑mode protocol and the cipher tables were reverse‑engineered and
published by **Jess Sullivan** in
[**canon‑megatank‑reset**](https://github.com/Jesssullivan/canon-megatank-reset)
(zlib License) — a native Linux, key‑free, cloud‑free reset for the Canon
MegaTank 5B00 error. **This project is an independent Windows C# port of the
read/reset path**, built around that recovered protocol and validated byte‑exact
against a real G6040's own captured traffic.

Licensed under the **zlib License** — see [`LICENSE`](LICENSE). This is an altered
work (a Windows port), plainly marked as such, and is not the original
`canon‑megatank‑reset`.

## Disclaimer

For lawful repair and interoperability on printers you own or are authorized to
service. No warranty. A counter reset does not empty the physical absorber pad —
service it. Writing printer EEPROM carries inherent risk; the reset is
experimental on the G6040. Use the dry‑run first.
