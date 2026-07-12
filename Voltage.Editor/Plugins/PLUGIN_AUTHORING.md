# Authoring a Voltage plugin

A plugin is a **folder** whose root contains one file the editor reads: **`plugin.json`**. That manifest
declares the plugin's identity and points at everything it ships (managed DLLs, C# sources, native
libraries, content). When a plugin is added to a project, the editor reads `plugin.json`, discovers the
`Id` and `Kinds`, and loads exactly what it lists.

There are two kinds (a plugin can be both):

- **gameplay** — engine `Component`s and assemblies that become part of the built game (e.g. an FMOD
  integration). Loaded in the editor so its components show up in *Add Component*, and statically
  compiled into the NativeAOT game at build time.
- **editor** — ImGui tools/windows that extend the editor itself (implement `IEditorPlugin`). Never
  shipped in the game.

---

## How the editor reads it

1. You add a plugin (Plugins → Plugin Manager → **Add Plugin**, or by editing `plugins.json`).
2. The editor **resolves** the source (folder / git / zip / bundled) into a per-user cache and reads the
   package's `plugin.json`.
3. It validates the manifest and records the plugin in `plugins.lock.json` (pinned by content hash).
4. It syncs the payload into the gitignored `PluginLibs/<id>/` folder in your project.
5. **gameplay**: it `Assembly.LoadFrom`s the managed DLLs (so their `[ComponentId]` components register)
   and generates `PluginLibs/Plugins.g.props` so the game build statically references them.
   **editor**: it instantiates each `IEditorPlugin` and calls `Initialize`.

Only `plugins.json` (what the project wants) and `plugins.lock.json` (exact pins) are committed to your
game repo — `PluginLibs/` is regenerated and gitignored.

---

## Tutorial 1 — a gameplay plugin (a new Component)

### 1. Create the folder + a `.csproj`

```
MyPlugin/
  plugin.json
  src/                     (source-form: compiled with the game — simplest)
    HealthComponent.cs
```

Two ways to ship gameplay code:

- **Source-form** (recommended for simple community plugins): put `.cs` files under `src/`. They compile
  together with the game's scripts — no build step, and they hot-reload in `Dev` mode.
- **Prebuilt DLL** (required for NDA/binary SDKs like FMOD): build a `net8.0` class library that
  references `Voltage.dll` + the `Voltage.SourceGenerators` analyzer, and ship the DLL under `lib/`.

### 2. Write the component

```csharp
using Voltage;
using Voltage.Serialization;

namespace MyPlugin
{
    // The [ComponentId] is the STABLE on-disk identity. Scenes reference the component by this id,
    // so you can rename the class later without breaking saved scenes. REQUIRED for published plugins.
    [ComponentId("myplugin.health")]
    public partial class HealthComponent : Component   // must be `partial`
    {
        public float Max = 100f;
        public float Current = 100f;
    }
}
```

Two rules that make serialization work automatically:
- the class is **`partial`** (the `ComponentData` source generator emits AOT-safe read/write for it), and
- it carries a **`[ComponentId("…")]`** (published plugins must declare one; the editor stamps missing
  ids only for your own project scripts and `Dev` plugins).

### 3. Write `plugin.json`

```json
{
  "SchemaVersion": 1,
  "Id": "com.you.myplugin",
  "Name": "My Plugin",
  "Version": "1.0.0",
  "Description": "Adds a Health component.",
  "Kinds": ["gameplay"],
  "EngineVersion": ">=0.1.0",
  "Gameplay": {
    "SourceRoots": ["src"]
  }
}
```

For a **prebuilt DLL** plugin instead, drop `SourceRoots` and use:

```json
  "Gameplay": {
    "ManagedAssemblies": ["lib/MyPlugin.dll"],
    "RootTypes": ["MyPlugin.HealthComponent"],
    "TrimmerRootAssemblies": ["MyPlugin"]
  }
```

`RootTypes` (one public type per assembly) lets the game build root the assembly for NativeAOT;
`TrimmerRootAssemblies` preserves it from trimming. Both are auto-detected if omitted, but declaring
them is safest.

### 4. Add it

Plugin Manager → **Add Plugin** → *Local folder* → Browse to `MyPlugin` → **Add Plugin**. Your
`HealthComponent` now appears in *Add Component*, serializes into scenes, and ships in the game build.

---

## Tutorial 2 — an editor plugin (a custom window)

### 1. Build a class library that references `Voltage.Editor.dll` + `ImGui.NET` (1.89.5)

```csharp
using Voltage.Editor.Plugins;
using ImGuiNET;

namespace MyTools
{
    public class DialogueTool : IEditorPlugin
    {
        private class DialogueWindow : EditorPluginWindow
        {
            public override void Draw()
            {
                if (ImGui.Begin(Title, ref IsOpen))
                    ImGui.Text("Hello from a plugin window!");
                ImGui.End();
            }
        }

        public void Initialize(IEditorPluginContext ctx)
        {
            var window = new DialogueWindow { Title = "Dialogue Tool" };
            ctx.RegisterWindow(window);
            ctx.AddMenuItem("MyTools/Dialogue Editor", () => window.IsOpen = true);
        }

        public void Shutdown() { }
    }
}
```

### 2. Package + manifest

```
MyTools/
  plugin.json
  editor/
    MyTools.dll
```

```json
{
  "SchemaVersion": 1,
  "Id": "com.you.mytools",
  "Name": "My Editor Tools",
  "Version": "1.0.0",
  "Kinds": ["editor"],
  "EditorPluginApiVersion": 1,
  "Editor": { "Assemblies": ["editor/MyTools.dll"] }
}
```

`EditorPluginApiVersion` must match the editor's `EditorPluginApi.Version` (currently `1`) or the plugin
is refused with a clear message. Your menu item appears under the **Plugins** menu; your window draws
while open. (Assemblies can't hot-unload — updating an editor plugin needs an editor restart.)

---

## Tutorial 3 — an NDA SDK (FMOD-style)

For SDKs whose binaries you can't redistribute, declare an `ExternalSdks` block. The user configures the
SDK's local install path once (Plugin Manager → *External SDKs*), and the editor **pulls** the needed
files from there into `PluginLibs/` at sync time — they never enter your package, the cache, or git.

```json
{
  "SchemaVersion": 1,
  "Id": "com.you.fmod",
  "Name": "FMOD Audio",
  "Version": "1.0.0",
  "Kinds": ["gameplay"],
  "Gameplay": {
    "ManagedAssemblies": ["lib/fmod_csharp.dll"],
    "Natives": [
      { "Rid": "win-x64",   "Files": ["native/win-x64/*.dll"] },
      { "Rid": "osx-arm64", "Files": ["native/osx-arm64/*.dylib"] }
    ]
  },
  "ExternalSdks": [{
    "Id": "fmod-engine",
    "DisplayName": "FMOD Engine 2.02+",
    "EnvVar": "FMOD_SDK_ROOT",
    "Required": true,
    "Pulls": [
      { "From": "api/core/lib/x64/fmod.dll",           "To": "native/win-x64/" },
      { "From": "api/core/lib/csharp/fmod_csharp.dll",  "To": "lib/" }
    ]
  }]
}
```

Per-RID natives are copied next to the built game automatically. If the SDK path isn't set, the plugin
is marked *Unavailable* (scenes still load; unknown-component data is preserved) until it's configured.

---

## Distributing your plugin

The manifest/package is source-agnostic, so the same folder can be installed as:

- a **local folder** (development, or an internal file share),
- a **git URL + ref** — `{"Git": "https://…", "Ref": "v1.0.0"}`; **private repos work via your git
  credentials**, which is how NDA plugins stay off public infrastructure, or
- an **https zip URL**.

Pin a stable git tag (or a versioned zip) so `plugins.lock.json` records an exact commit/hash and every
teammate restores identical bytes.

---

## Manifest field reference (quick)

| Field | Meaning |
|---|---|
| `Id` | Stable reverse-domain id — never change it |
| `Version` | Plugin semver |
| `Kinds` | `["gameplay"]`, `["editor"]`, or both |
| `EngineVersion` | Supported engine range (e.g. `">=0.1.0"`) |
| `EditorPluginApiVersion` | Required for editor plugins; must match the editor |
| `Gameplay.ManagedAssemblies` | Prebuilt DLLs shipped into the game |
| `Gameplay.SourceRoots` | Folders of `.cs` compiled with the game |
| `Gameplay.RootTypes` / `TrimmerRootAssemblies` | AOT rooting / trim preservation |
| `Gameplay.Natives` | Per-RID native libraries |
| `Gameplay.Content` | Runtime content copied into the game's `Content/` |
| `Editor.Assemblies` | DLLs containing `IEditorPlugin` implementations |
| `ExternalSdks` | NDA SDK files pulled from the user's local install |
