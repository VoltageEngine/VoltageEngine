---
title: SVG Paths and Shapes
sidebar_position: 5
description: Read rectangles, circles, polygons and Bezier paths from SVG files for level layout and movement paths.
---

# SVG Paths and Shapes

`Voltage.Svg` reads basic SVG documents as data. It is meant for layout, such as patrol routes, camera rails, trigger areas and spawn shapes drawn in a vector editor, rather than for displaying vector art.

```csharp
using System.IO;
using Voltage.Svg;

using var stream = File.OpenRead(Path.Combine(VoltageContentManager.ContentRoot, "Content/Paths/Level1.svg"));
var doc = SvgDocument.Open(stream);

foreach (var path in doc.Paths ?? System.Array.Empty<SvgPath>())
{
    Vector2[] points = path.GetTransformedDrawingPoints(new SvgPathBuilder());
}
```

A document is a group: `Paths`, `Rectangles`, `Lines`, `Circles`, `Ellipses`, `Polygons`, `Polylines` and `Images` are arrays (null when absent), and `Groups` holds nested groups with the same members. Supported elements are groups, paths, rectangles, lines, circles, ellipses, polygons, polylines and images (embedded, linked or from the content folder), with their transforms applied. A path made of cubic Bezier segments converts to a `BezierSpline` with `GetBezierSplineForPath()`, and `GetOptimizedDrawingPoints(tolerance)` simplifies dense paths.

To parse a single path without a document, set the `d` attribute on an `SvgPath` and call `GetTransformedDrawingPoints`:

```csharp
var path = new SvgPath { D = "M 0 0 C 40 -60, 120 -60, 160 0" };
Vector2[] curve = path.GetTransformedDrawingPoints(new SvgPathBuilder());
```

`SvgDebugComponent` draws a whole SVG file over the scene, which is a quick way to check alignment and a working example of reading every element type.
