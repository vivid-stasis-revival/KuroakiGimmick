# Rust chart parsing and BPM · application v0.1.5 / 18.0

The current native crate is `0.3.0-alpha.0`. Build, switches, native-library staging
and release prerequisites are documented in [OPTIMIZATION_ENGINE.md](OPTIMIZATION_ENGINE.md).
This guide describes the parse/BPM boundary used by the existing C# application.

## Parsing

- Rust parses VSC, including CSM 3.4.0 fifth-column `modExtra`, binary VSB and VSM.
- Note times stay in seconds; VSM times remain beats. Stable note order, event
  source lines/order, proxy counts and the `_` sentinel are retained. Undefined
  modExtra is null; an explicitly present empty map remains distinct from null.
- VSB object-local mod names use the existing C# catalog and `id - 129` convention.
- Empty Note extras use the shared `Note.EmptyExtra` object on both paths.
- VSM trailing `//` comments are stripped before reading metadata, events and mpf
  functions. A warning records the first affected source line. C# and Rust emit
  the same warning; it does not fail strict diagnostics. The original game does
  not strip comments, so remove them before loading source files there.
- Standalone comment rows, bare `mods`, malformed rows and unsupported GML numeric
  syntax retain the C# diagnostic path. In required mode a native failure throws.

Rust returns a bounded little-endian packet. C# copies it and always frees native
storage via `kg_core_free_packet`; no parse pointers survive the call. `KGS2`
contains chart data and source diagnostics; older `KGS1` packets remain accepted.
VSC/VSB input is checked against 128 MiB before allocation; VSM files are limited
to 64 MiB and native packets to 512 MiB. Note counts are validated against packet
contents rather than a fixed two-million limit; VSM events remain limited to one million.

## BPM

Rust constructs the same segments as C# for same-time overrides, negative-time
changes, offsets and fixed-BPM mode. The `KGB1` packet reconstructs ordinary C#
`BpmMap.Segment` objects. There is no fixed BPM-change/segment count limit; count
validation uses packet bytes and managed collection bounds before allocating.

## Modes and validation

`KG_RUST_ENGINE=auto|off|verify|required` controls parsing, BPM and Timeline.
The initial default is `auto`; the Compatibility/C# preference selects `off` when
no environment override is set. `verify` compares values and diagnostics with the
original C# implementation and falls back on mismatch. `KG_RUST_BRIDGE` controls
Note motion separately; without an override it follows calculation on/off.

```sh
bash scripts/build-rustbridge.sh
KG_RUST_ENGINE=verify dotnet run -c Release --no-build -- --rust-engine-test
```

The engine self-test checks actual native execution, VSC/VSM/BPM parity, trailing
comment warnings and informational VSC v/s anchors. The full CPU regression also
checks VSB/VSC empty extras and rejection before reading oversized files. See
[tests/Regression/README.md](tests/Regression/README.md) for native and optional
high-memory BPM boundary checks. Native decode failures and unsupported input
retain the original C# implementation in auto/verify modes.
