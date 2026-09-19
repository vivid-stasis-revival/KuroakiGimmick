KuroakiGimmick v0.1.2 / 16.2 compile hotfix 2

Fixes the remaining references to the old local variable names `from` / `to`
in Viewer.Editor.ImageCanvas.cs after compile-hotfix.1 renamed those pose variables
to `startPose` / `endPose`.

Apply at the project root, after compile-hotfix.1 if it was already installed:
  unzip -o KuroakiGimmick-v0.1.2-16.2-compile-hotfix.2.zip
  ./scripts/build-mac.sh

This hotfix only replaces:
  src/UI/Editor/Viewer.Editor.ImageCanvas.cs
