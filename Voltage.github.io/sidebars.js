// @ts-check

/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: ['getting-started/installation', 'getting-started/first-game'],
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
        'scripting/components',
        'scripting/scene-components',
        'scripting/content-loading',
        'scripting/audio',
        'scripting/rules-and-gotchas',
      ],
    },
    {
      type: 'category',
      label: 'Lighting',
      items: ['graphics/room-light', 'graphics/projected-light', 'graphics/animated-normal-maps'],
    },
    {
      type: 'category',
      label: 'Automation',
      items: ['gateway/README'],
    },
    {
      type: 'category',
      label: 'Extending',
      items: ['plugins/README', 'assets/README'],
    },
    {
      type: 'category',
      label: 'Engine Internals',
      items: ['engine/serialization-and-aot', 'engine/engineer-notes'],
    },
  ],
};

module.exports = sidebars;
