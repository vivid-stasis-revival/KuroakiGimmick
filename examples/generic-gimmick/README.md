# Configuration-driven example

Open `example.sgv.json` in the viewer. No C# object registration is required.
The shader starts enabled, turns off at beat 4 (two seconds), and turns back on at
beat 8 (four seconds). Move those VSM events to change the effect lifetime.
This example uses an original grayscale shader, so it needs no game-specific
sprite files beyond the viewer's normal installed resources.

`sceneComposite: "filter-chain"` selects the reusable ordered filter-chain component.
The filter chain itself is supplied by `compositePasses`; it can contain CRT,
grayscale or another validated shader. `compositeLifecycle` controls existence.
The names `enable_effect`, `disable_effect`, `effect.on` and `effect.off` are all
configuration names, not reserved runtime object names.

The full v2 component contract is documented in
[`scripts/assets/GENERIC_COMPONENTS.md`](../../scripts/assets/GENERIC_COMPONENTS.md).
