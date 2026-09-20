# Kuroaki UI Sans artwork

UI bitmap glyphs and metrics rasterized from IBM Plex Sans SC Medium / SemiBold.
Source package: @ibm/plex-sans-sc 1.1.0, published by IBM.
Upstream: https://github.com/IBM/plex
Copyright © 2017 IBM Corp. with Reserved Font Name "Plex".

These generated bitmap font resources are distributed under SIL OFL 1.1.
The full copyright and license are in ../../ThirdParty/IBM-Plex-OFL.txt.
The generated family is named **Kuroaki UI Sans**; SourceFamily in the metadata
identifies the upstream typeface for attribution, not the derivative font name.
The application code remains under its own license.

No original font binaries or game assets are included here. Both atlases are
embedded in the executable. Regenerate with scripts/dev/build-ui-font-atlas.py;
see docs/I18N.md. In the Chinese interface, printable ASCII and Chinese use Medium with a shared
baseline. The English interface keeps the original sans / mono / CJK fonts.
SemiBold remains available as a fallback for the separate document renderer;
main UI headings do not select it automatically.
