KuroakiGimmick v0.1.2 / 16.2 compile hotfix 1

Fixes the C# parser errors caused by using the contextual keyword `from`
as the source-pose local directly before record `with` expressions.

Changed files:
  src/UI/Editor/Viewer.Editor.ImageCanvas.cs
  src/UI/Editor/Viewer.Editor.ImageObjects.cs
  tests/SelfTests/ImageObjectSelfTest.cs

Apply at the project root, then rebuild:
  unzip -o KuroakiGimmick-v0.1.2-16.2-compile-hotfix.1.zip
  ./scripts/build-mac.sh

The fix renames only the affected locals to startPose/endPose. It does not
change VSM/VSP semantics, themes, track-help Z-order, export, or image resource paths.
