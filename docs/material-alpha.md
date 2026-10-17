All future character ports must resolve source alpha before passing colours to
Unity. A material with zero alpha can still be visible when its texture replaces
or blends into alpha. Multiplying every texture by that zero erases effects.

`MaterialAlpha.Tint` implements the shared texture-mask-preserving tint policy.
`MaterialAlphaBuffer` adapts native RGBA float arrays for `IViewSource.Colors`.
Alongside each colour, provide three floats: raw material alpha, current animated
texture blend, and normalized mode (`Multiply=0`, `Blend=1`, `Replace=2`). The
native reader owns decoding format-specific HSD/TEV flags. The host owns applying
this shared policy. Input metadata and colour counts must match. Dispose the
buffer with the view. Sources whose final alpha was already baked can omit the
metadata and keep their existing colours; do not apply the operation twice.

Texture alpha remains in the shared shader, preserving clear borders. Blend
weights follow source animation, so effects can fade in and out. This is the
existing simplified textured-tint policy, not a complete GX TEV interpreter;
complex combiners must be baked or implemented from their source semantics.
Do not replace legitimate zero alpha with one or force all effects opaque.

Run `dotnet run --project tests/MaterialAlphaCheck -c Release`. Every new port
must also compare native metadata with its rendered alpha through the actual
C# boundary and inspect an active effect plus its transparent/faded frames.
Samus's zero-material-alpha Screw Attack is the concrete regression case.
