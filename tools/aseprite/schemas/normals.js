export function registerNormalsTools(server) {
  const frames = {
    frames: z.array(z.number()).optional().describe('1-based frames; defaults to the tag, then every frame'),
    tag: z.string().optional().describe('Tag whose frames to use'),
  };
  const layers = z.array(z.string()).optional().describe('Diffuse layer names; defaults to every visible art layer');
  const layer = z.string().optional().describe('Diffuse or Normals- layer; defaults to the active layer');
  server.tool('normals_generate', 'Generate flat faceted normals into hidden Normals-<layer> layers. An existing Normals layer is never replaced without overwrite=true; a new -gen layer is written instead.', {
    layers, ...frames,
    preset: z.enum(['Character', 'Prop', 'Wall-tile']).optional().describe('Starting values for the options below'),
    width: z.number().min(1).max(4).optional().describe('Silhouette bevel width in pixels'),
    merge_outlines: z.boolean().optional().describe('Dark outline pixels take the facet of the surface they wrap'),
    split: z.boolean().optional().describe('Add side facets to large colour clusters'),
    split_min: z.number().min(0).optional().describe('Smallest cluster to split, in pixels'),
    diagonals: z.boolean().optional().describe('Use the four diagonal facets at corners'),
    overwrite: z.boolean().optional().describe('Replace the existing Normals layer'),
  });
  server.tool('normals_apply', 'Set the selected pixels of a Normals layer to one palette facet, clipped to the diffuse alpha.', {
    layer,
    facet: z.enum(['front', 'left', 'right', 'top', 'bottom', 'top_left', 'top_right', 'bottom_left', 'bottom_right']).describe('Palette facet'),
    frame: z.number().optional().describe('1-based frame; defaults to the active frame'),
  });
  server.tool('normals_average', 'Replace the selected normals with the palette facet nearest to their average.', {
    layer, frame: z.number().optional().describe('1-based frame; defaults to the active frame'),
    diagonals: z.boolean().optional().describe('Allow diagonal facets as the result'),
  });
  server.tool('normals_select_cluster', 'Select the connected same-colour diffuse cluster under a canvas point.', {
    x: z.number().describe('Canvas x'), y: z.number().describe('Canvas y'), layer,
    mode: z.enum(['replace', 'add', 'remove']).optional().describe('How it combines with the current selection'),
    frame: z.number().optional().describe('1-based frame; defaults to the active frame'),
  });
  server.tool('normals_validate', 'Check every Normals-/Glow- layer against its diffuse: alpha, bounds, frames, palette colours; unbevelled silhouette pixels are warnings.', {
    layers: z.array(z.string()).optional().describe('Normals- or Glow- layer names; defaults to all of them'),
    diagonals: z.boolean().optional().describe('Accept the diagonal facets'),
  });
  server.tool('normals_glow', 'Glow brush: action=edit shows the Glow-<layer> and targets it, strength sets the foreground colour for the pencil, fill and clear work on the selection.', {
    action: z.enum(['edit', 'strength', 'fill', 'clear']).describe('What to do'), layer,
    on: z.boolean().optional().describe('action=edit: false ends editing and hides the layer again'),
    strength: z.number().min(0).max(1).optional().describe('Self-illumination 0..1'),
    frame: z.number().optional().describe('1-based frame; defaults to the active frame'),
  });
  server.tool('normals_rim', 'Compute the Jolt-style pixel rim. bake=true writes it into Glow-<layer> on the chosen frames; otherwise returns an image of it.', {
    layers, ...frames,
    width: z.number().min(1).max(4).optional().describe('Rim depth from the silhouette'),
    side_strength: z.number().min(0).max(1).optional().describe('Left and right edges relative to the top'),
    facing: z.number().min(0).max(1).optional().describe('How far a normal must face the edge'),
    skip_layers: z.array(z.string()).optional().describe('Layers whose pixels get no rim'),
    bake: z.boolean().optional().describe('Write into the Glow layers'),
    replace: z.boolean().optional().describe('Discard the existing glow on those frames'),
    use_generated: z.boolean().optional().describe('Read normals from -gen layers'),
  });
  server.tool('normals_carry', 'Record cluster facets and glow on one frame and carry them to matching clusters on other frames. Without apply=true it only reports the diff and returns a strip with changed pixels in magenta.', {
    layer, ...frames, source_frame: z.number().optional().describe('Frame to copy from; defaults to the active frame'),
    threshold: z.number().min(0).max(2).optional().describe('Largest match score for a cluster pair'),
    apply: z.boolean().optional().describe('Write the result'),
  });
  server.tool('normals_preview', 'Render a frame in Aseprite with a Voltage-exact CPU copy of the lighting. Returns a PNG.', {
    frame: z.number().optional().describe('1-based frame; defaults to the active frame'),
    mode: z.enum(['final', 'normals', 'glow']).optional().describe('View'),
    preset: z.enum(['Fluorescent bar above', 'Warm room fill', 'Cable spark']).optional().describe('Light preset when lights is omitted'),
    lights: z.array(z.object({
      kind: z.enum(['point', 'bar', 'fill']).optional(), preset: z.string().optional(), x: z.number().optional(), y: z.number().optional(),
      z: z.number().optional(), radius: z.number().optional(), intensity: z.number().optional(), color: z.string().optional(),
      length: z.number().optional(), range: z.number().optional(), spread: z.number().optional(), softness: z.number().optional(), falloff: z.number().optional(),
    })).optional().describe('Lights in canvas pixels'),
    ambient: z.string().optional().describe('Ambient colour as #RRGGBB'),
    normal_layers: z.array(z.string()).optional().describe('Normals layers; defaults to all of them'),
    use_generated: z.boolean().optional().describe('Prefer -gen layers'),
    scale: z.number().min(1).max(16).optional().describe('Integer upscale'),
  });
}
