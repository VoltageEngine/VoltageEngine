---
title: Data Assets
---

# Data Assets

Data assets store shared settings in `.vasset` files. Define a public partial class derived from `Voltage.Data.DataAsset`, with public fields for the values you want to inspect, then compile the project scripts. For example:

```csharp
using Voltage.Data;

public partial class EnemySettings : DataAsset
{
    public int Health = 100;
    public float Speed = 80f;
}
```

In the [Asset Browser](asset-browser.md), right-click an indexed destination folder and choose **Create > Data Asset**, then choose the registered type. Use a folder under `Data/` for shared settings. Double-click a `.vasset` to open the **Data Asset** window; this is separate from the scene Inspector. Scenes and prefabs have [independent discovery anywhere in the project](scenes-and-prefabs.md); that does not make every other asset type discoverable in arbitrary folders.

Edit the fields, then choose **Save** or press **Ctrl+S** on Windows/Linux or **Cmd+S** on macOS while this window is focused. **Reveal** highlights the file in the Asset Browser. **Reload** rereads the saved file and discards unsaved edits.

The window edits a shared cached instance: objects using that asset see the same values. Saving the scene does not replace saving the data asset. Closing the window or opening another asset with unsaved edits offers **Save**, **Don't Save** and **Cancel**. If saving fails, the prompt stays open so the edits are not silently discarded.

Scripts can keep an `AssetReference` and load it through `DataAssets.Load<EnemySettings>(reference)`. See [content loading](../scripting/content-loading.md) for asset references and project paths.
