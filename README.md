# NSDK Unity Samples

A Unity project demonstrating the Niantic Spatial SDK (NSDK) — depth, occlusion, meshing,
semantic segmentation, device mapping, scanning, and VPS2 localization.

## Quick Links
* [Documentation](https://www.nianticspatial.com/docs/nsdk/?platform=unity)
* [NSDK API Reference](https://www.nianticspatial.com/docs/api/unity/?platform=unity)
* [Sample project walkthroughs](https://www.nianticspatial.com/docs/nsdk/sample_projects/?platform=unity)
* [NSDK Unity package](https://github.com/nianticspatial/nsdk-library-upm)

## Requirements

* Unity 6000.3.14f1
* [Git LFS](https://git-lfs.com) — required to check out any LFS-tracked assets
* An ARKit-capable iOS device or an ARCore-capable Android device — the samples use live
  camera and sensor data and do not run meaningfully in the Editor
* A Niantic Spatial access token, for the samples that use VPS2 or Sites

## Getting Started

1. Clone this repository.
2. Fetch any LFS-tracked assets:

   ```sh
   git lfs install
   git lfs pull
   ```

3. Open `NsdkSamples/` in Unity Hub using the version above. Unity Package Manager
   resolves the NSDK package on first open, which may take a few minutes.
4. Open `Assets/Samples/Home/Scenes/Home.unity` — this is the entry point that links to
   every other sample.
5. Set your access token in **Project Settings → XR Plug-in Management → Niantic Spatial
   Development Kit → Credentials → Niantic Spatial Access Token** (the **NSDK → XR Plug-in
   Management** menu opens the same page).

   To create a token, sign in to [Scaniverse Web](https://scaniverse.nianticspatial.com),
   open **Developer tokens** for your organization, and select **New developer token**.
   Copy it before closing the dialog — it is shown only once — and note that it expires on
   the date you choose. See the
   [setup guide](https://www.nianticspatial.com/docs/nsdk/setup/?platform=unity) for more.
6. Build and run on a device.

## What's included

| Sample | Scene |
|---|---|
| Home — entry point linking the others | `Samples/Home/Scenes/Home.unity` |
| Depth display | `Samples/Depth/Scenes/DepthDisplay.unity` |
| Occlusion | `Samples/Depth/Scenes/Occlusion.unity` |
| Meshing | `Samples/Meshing/Scenes/NormalMeshes.unity` |
| Scene segmentation | `Samples/SceneSegmentation/Scenes/SceneSegmentationDisplay.unity` |
| Device mapping | `Samples/DeviceMapping/Scenes/DeviceMapping.unity` |
| VPS2 localization | `Samples/VPS2/Scenes/VPS2Localization.unity` |
| Sites | `Samples/Sites/Scenes/SitesClient.unity` |
| Scanning / recording | `Samples/Scanning/Scenes/Recording.unity` |
| Preloading | `Samples/Preloading/Scenes/Preloading.unity` |

## License

See [LICENSE](LICENSE).
