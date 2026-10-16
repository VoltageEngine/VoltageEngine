# Fluorescent light

`Voltage.DeferredLighting.FluorescentLight` emits from a finite horizontal bar
toward local positive Y. Rotate the entity to aim it; parent transforms and
mirrored/nonuniform scale also transform its source and receiving area. Add it
on the scene's lighting render layer. It shades the existing normal buffer.

| Setting | Purpose |
| --- | --- |
| SourceLength | Length of the emitting bar in local pixels |
| Range | Downward reach in local pixels |
| Spread | Extra horizontal radius per pixel of downward reach |
| EdgeSoftness | Feather width at the rounded perimeter, in local pixels |
| Falloff | Strength of attenuation toward the perimeter |
| ZPosition | Virtual height above receiving surfaces for normal shading |
| Color / Intensity | Hue and brightness |
| LocalOffset | Source position relative to the entity |
| FlickerDepth | Maximum fractional dimming; zero disables flicker, capped at 0.5 |
| FlickerDuration | Burst length in seconds, capped at half the interval |
| FlickerInterval | Seconds of enabled play time between burst starts |
| FlickerPhase | Initial offset into the flicker cycle, in seconds |

The receiving footprint is a feathered half ellipse, with horizontal radius
`SourceLength / 2 + Range * Spread + EdgeSoftness` and vertical radius `Range`.
Attenuation fades continuously toward its curved perimeter. The source emits
on one side, with no upward halo. The nearest position along
the bar supplies the surface-to-light direction; this is an art-directed 2D
approximation. Shadows and volumetric beams are not simulated. RoomLight can
continue providing independent bounded ambient fill.

Flicker applies three brief, uneven dimming dips separated by steady output.
Their timing, width and depth vary deterministically between bursts; the
interval leaves a steady gap outside each burst window. It never increases
authored intensity or switches the lamp completely off. `FlickerMultiplier`
can also drive a visible emitter material from the same clock. Its clock advances
only while enabled in play mode, freezes during pause and variant hiding, and
resets when removed. No global lighting or screen flash is changed. New lights
have flicker disabled until a nonzero depth is authored.

Fields are inspector-editable and use generated serialization and NativeAOT
registration. Resize Bounds changes the bar length and reach while keeping the
source origin and entity fixed; undo restores both values. For rotated lights,
handles project the pointer into the light's local axes. Debug rendering shows
the bar and receiving envelope. Zero-scale or invalid transforms emit nothing.
