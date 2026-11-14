---
title: Data Assets
---

# Data Assets

A data asset is a `.vasset` file holding shared settings, such as enemy stats or a difficulty profile, with no entity or update cost. Define a public partial class derived from `Voltage.Data.DataAsset` with public fields for the values to edit, then compile the scripts:

```csharp
using Voltage.Data;

public partial class EnemySettings : DataAsset
{
    public int Health = 100;
    public float Speed = 80f;
}
```

The class must be concrete, `partial` (so the source generator can add its reflection-free reader) and have a public parameterless constructor. To start from a template, right-click a folder under `Scripts` in the Asset Browser and choose **Create > New Script > Data Asset**. Enums, lists, colours, asset slots and nested `ISerializableData` types all edit with the standard inspectors. For stable type ids, schema versions and variants, see [Assets: data assets](../assets/README.md).

## Creating and editing

In the [Asset Browser](asset-browser.md), right-click a folder and choose **Create > Data Asset**, then the type. Create the file inside `Data/` (the usual place) or another indexed folder (`Content`, `Scripts` or `Effects`); a `.vasset` elsewhere is not indexed.

Double-click a `.vasset` to open it in the **Data Asset** window, which is separate from the entity inspector. The header shows the file name and type; hover it for the type id, GUID and path. The menu bar has:

- **Save** (`Ctrl+S`, or `Cmd+S` on macOS, while the window is focused).
- **Reveal**, which highlights the file in the Asset Browser.
- **Reload**, which rereads the file and discards unsaved edits.

The window edits the shared cached instance, so everything using the asset sees edits immediately, before they are saved. Saving the scene does not save the data asset. Unsaved data assets are listed in the editor's exit prompt like unsaved scenes. Closing the window or opening another asset with unsaved edits offers **Save**, **Don't Save** and **Cancel**; if saving fails, the prompt stays open so nothing is silently lost.

If the file changes on disk, the window rereads it. If scripts recompile while you have unsaved edits, the window keeps your values.

## Using a data asset from code

Keep an `AssetReference` field and load it with `DataAssets.Load<T>`:

```csharp
[AssetType(typeof(EnemySettings))]
public AssetReference Settings;

var settings = DataAssets.Load<EnemySettings>(Settings);
```

`Load` returns the shared instance, or `null` when the reference is empty, cannot be resolved or has the wrong type. Changing its fields at runtime changes them for every user of the asset. Mark the class `[CloneOnLoad]` to give each `Load` call its own copy. See [content loading](../scripting/content-loading.md) for asset references and project paths.
