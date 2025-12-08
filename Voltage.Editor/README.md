# Voltage Engine

Voltage Engine is a MonoGame-based 2D game ENGINE with a separate WYSIWYG editor, evolved from Nez. It provides a complete Entity-Component-System architecture with ImGui-powered visual editing tools for rapid game development.

Similar to popular engines like Unity and Godot, Voltage Engine combines a robust runtime with an intuitive editor, that allows you to create game projects with the help of the editor, and then build the game exectuable with its help for publishing,

> [!WARNING]

> Engine Status: Experimental. Using it for commercial release is not recommended yet.

> Voltage Engine is NOT yet production-ready for 2D game development. Many features are still being rewritten, as is the core of the Editor itself. Expect breaking changes in the upcoming time. Contributions and feedback are welcome!

This repository contains the complete engine and editor. Reference it from your game project to leverage both the powerful ECS runtime and the visual editing capabilities.

<img width="3840" height="2160" alt="Jolt - Editor Showoff" src="https://github.com/user-attachments/assets/c63658a0-0e4e-4695-91da-daceb4c3359c" />

---

## Core Features

### Runtime Foundation
- **Entity-Component-System (ECS)** - Flexible, composable game object architecture
- **Scene Management** - Hierarchical scene system with lifecycle hooks
- **Rendering Pipeline** - Layered rendering with custom materials and effects
- **Physics & Collisions** - AABB, circle, and polygon collision detection with spatial hashing
- **Input Handling** - Unified input system for keyboard, mouse, gamepad, and touch
- **Content Pipeline** - Asset loading and management via MonoGame Content Pipeline
- **Coroutines & Timers** - Built-in async patterns for time-based logic

### Visual Editor
- **Editor Executable** - Full-featured editor as a standalone app to host your game projects
- **Scene Graph Window** - Hierarchical entity browser with multi-select and drag-drop
- **Entity Inspector** - Real-time property editing with custom inspectors
- **Component System** - Add/remove/configure components at runtime
- **Edit/Play Mode** - Toggle between editing and testing (F1/F2)
- **Layout System** - Save and load custom editor layouts
- **Debug Console** - Built-in command console for development tools

### Data-Driven Workflow
- **SceneData Serialization** - JSON-based scene persistence
- **Entity Factory Registry** - Type-safe entity creation without reflection
- **Prefab System** - Reusable entity templates
- **EntityData Contracts** - Clean separation between editor and runtime data
- **Undo/Redo** - Full undo stack for editor operations

### Developer Experience
- **Tiled Map Editor Integration** - Import and render Tiled (.tmx) maps
- **Aseprite Support** - Load Aseprite sprite data and animations
- **Animation Event Editor** - Timeline-based event authoring for sprite animations
- **Sprite Atlas Support** - Texture packing and efficient rendering
- **Notification System** - User-facing feedback for editor actions
- **Content Browser** - File pickers rooted to your Content folder

---

## Project Structure

- **Voltage.Engine** (`Voltage.Portable`) - Core runtime (ECS, rendering, physics, utilities)
- **Voltage.Editor** (`Voltage.ImGui`) - Editor UI and tooling (scene graph, inspectors, gizmos)
- **Voltage.Persistence** - Serialization systems (JSON, binary)
- **Voltage.FarseerPhysics** - Optional full physics simulation (Box2D-based)

---

## Getting Started

### Setting Up the ImGui Manager

The editor is available via the `ImGuiManager` global manager. You can toggle ImGui rendering via the `toggle-imgui` command in the debug console or programmatically:

````````markdown
var imGuiManager = new ImGuiManager();
Core.RegisterGlobalManager( imGuiManager );

// toggle ImGui rendering on/off. It starts out enabled.
imGuiManager.SetEnabled( false );
````````