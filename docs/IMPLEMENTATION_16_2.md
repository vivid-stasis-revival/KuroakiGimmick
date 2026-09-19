# Image object editing implementation

`Core/Editing/Images` defines reversible image-channel views, fixed-value grouping, local matrix projection, and a lightweight source-value sampler using the production VSM reader and `Timeline.Evaluate`.

`EditorDocument.ImageObjects.cs` writes actual source events through the existing transactional undo system. It does not create a second persistent pose store. Compound move/scale edits preserve event identities, source order, and untouched values. Overlaps and unsuitable dynamic/repeated source remain raw.

`UI/Editor/Viewer.Editor.Image*` handles the object inspector, source list, canvas, group timeline, capture/cancel, endpoint editing and route visualization. Preview gestures remain transient until mouse release. The UI canvas uses local 320x180 coordinates; it is not a proxy/post-processing inverse renderer.

`Graphics/Scene/SceneRenderer.Authoring.cs` exposes the pre-existing bounded texture cache to the authoring UI. No pre-existing Graphics file is changed. `CustomImages.At` adds an optional override map used only by the editing canvas; default callers retain the existing path.

`VspDocument.ReplaceSource` preserves logical dimensions and other instances. Resources use existing staging, SaveCopy, and Chart Folder pipelines. Existing VSM/export/renderer capability limitations remain unchanged.

The optional reference maintenance scripts now locate original Markdown by a verified SHA-256 when an old ZIP has a misdecoded filename. They neither rewrite the original bytes nor bypass hash validation. Runtime F1 uses the same unchanged generated/embedded index. The typography check now evaluates actual theme colors rather than a pre-theme hardcoded literal.

`ImageObjectSelfTest` is the production C# CPU/IO regression entry. It is included but was not executed in the delivery environment because the .NET SDK was unavailable. `check-image-objects.py` is explicitly a source contract and independent Python math test, not a compiler or a C# execution emulator.
