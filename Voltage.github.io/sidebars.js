// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: ['getting-started/installation', 'getting-started/first-game', 'getting-started/platforms'],
    },
    {
      type: 'category',
      label: 'Editor',
      items: [
        'editor/projects',
        'editor/windows',
        'editor/layouts',
        'editor/play-mode',
        'editor/scenes-and-prefabs',
        'editor/asset-browser',
        'editor/hot-reload',
        'editor/asset-build',
        'editor/aseprite',
        'editor/voltage-normals',
        'editor/light-preview',
        'editor/tilesets',
        'editor/tile-palette',
        'editor/tile-selection',
        'editor/game-build',
        'editor/timelines',
        'editor/animation-events',
        'editor/core-settings',
        'editor/data-assets',
        'editor/plugin-windows',
      ],
    },
    {
      type: 'category',
      label: 'Scripting',
      items: [
        'scripting/core',
        'scripting/components',
        'scripting/scenes-and-entities',
        'scripting/scene-components',
        'scripting/time-and-scheduling',
        'scripting/input',
        'scripting/debugging',
        'scripting/inspector-attributes',
        'scripting/content-loading',
        'scripting/tilemaps',
        'scripting/timelines',
        'scripting/audio',
        'scripting/rules-and-gotchas',
      ],
    },
    {
      type: 'category',
      label: 'Rendering',
      items: ['rendering/sprites', 'rendering/rendering', 'rendering/scene-transitions', 'rendering/ui', 'rendering/svg', 'effects'],
    },
    {
      type: 'category',
      label: 'Lighting',
      items: [
        'graphics/lighting',
        'graphics/animated-normal-maps',
        'graphics/room-light',
        'graphics/projected-light',
        'graphics/fluorescent-light',
      ],
    },
    {
      type: 'category',
      label: 'Physics',
      items: ['physics/collisions', 'physics/verlet'],
    },
    {
      type: 'category',
      label: 'Gameplay Utilities',
      items: ['utilities/tweening', 'utilities/collections-and-math', 'ai/behavior', 'ai/pathfinding'],
    },
    {
      type: 'category',
      label: 'Automation',
      items: ['gateway/README'],
    },
    {
      type: 'category',
      label: 'Plugins and Integrations',
      items: ['plugins/README', 'integrations/dialoguemaker', 'integrations/veltius', 'assets/README'],
    },
    {
      type: 'category',
      label: 'Engine Internals',
      items: ['engine/serialization-and-aot', 'engine/engineer-notes'],
    },
  ],
};

module.exports = sidebars;
