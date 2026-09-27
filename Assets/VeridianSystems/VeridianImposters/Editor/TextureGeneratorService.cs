using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using UnityEngine.Rendering;

// These preprocessor directives are essential for multi-pipeline support.
// They ensure that the 'using' statements and any pipeline-specific code
// are only compiled if the corresponding render pipeline package (URP or HDRP)
// is actually installed in the user's project. This prevents compilation errors.
#if UNITY_URP
using UnityEngine.Rendering.Universal;
#endif
#if UNITY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Veridian.Imposters
{
    /// <summary>
    /// This service is responsible for the first major step of billboard generation:
    /// creating all the necessary textures. It sets up a temporary, controlled scene,
    /// places a camera at various angles around the source object, and captures images
    /// (snapshots) of it. It captures both albedo (color) and, optionally, normal maps.
    /// These individual snapshots are then processed (e.g., edge padding, alpha clipping)
    /// and stitched together into a single large texture called an "atlas" for efficiency.
    /// The final output is a GeneratedTextureData object containing the atlases and metadata.
    /// </summary>
    public class TextureGeneratorService
    {
        // A private instance of the material used for capturing world-space normals.
        // It's created from a special shader that encodes normals into RGB colors.
        private Material normalCaptureMaterialInstance;

        // A dictionary to store the original materials of every renderer on the source object.
        // This is crucial for cleanup, allowing us to restore the object to its original state
        // after we temporarily override its materials to capture the normal map.
        private Dictionary<Renderer, Material[]> originalSharedMaterials = new Dictionary<Renderer, Material[]>();

        // A debug flag. If set to true, the service will save the raw top-down orthographic
        // snapshot to a PNG file in the project's root directory for inspection.
        private const bool DEBUG_SAVE_ORTHO_SNAPSHOTS = false;

        /// <summary>
        /// Creates and caches the material used for capturing normal maps.
        /// This avoids creating a new material for every snapshot.
        /// </summary>
        /// <param name="normalShader">The shader used to render world-space normals as colors.</param>
        private void SetupNormalCaptureMaterial(Shader normalShader)
        {
            if (normalShader == null) { Debug.LogError("Normal Capture Shader not provided."); return; }
            // If an old material instance exists, destroy it to prevent memory leaks.
            if (normalCaptureMaterialInstance != null) UnityEngine.Object.DestroyImmediate(normalCaptureMaterialInstance);
            normalCaptureMaterialInstance = new Material(normalShader);
        }

        /// <summary>
        /// Temporarily replaces the materials on all renderers of a GameObject with the normal capture material.
        /// It backs up the original materials first so they can be restored later.
        /// </summary>
        /// <param name="rootObject">The GameObject whose materials will be overridden.</param>
        private void ApplyNormalCaptureMaterials(GameObject rootObject)
        {
            if (normalCaptureMaterialInstance == null) { Debug.LogError("Normal Capture Material not initialized."); return; }
            originalSharedMaterials.Clear();
            Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer rend in renderers)
            {
                // Store the original materials before overwriting them.
                originalSharedMaterials[rend] = rend.sharedMaterials;
                // Create an array of the normal capture material to apply to all material slots.
                Material[] overrideMaterials = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < overrideMaterials.Length; i++) overrideMaterials[i] = normalCaptureMaterialInstance;
                rend.sharedMaterials = overrideMaterials;
            }
        }

        /// <summary>
        /// Restores the original materials to all renderers that were modified by ApplyNormalCaptureMaterials.
        /// This is a critical cleanup step.
        /// </summary>
        private void RestoreOriginalMaterials()
        {
            foreach (var pair in originalSharedMaterials)
            {
                // Check if the renderer still exists before trying to restore its materials.
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            }
            originalSharedMaterials.Clear();
        }

        /// <summary>
        /// Recursively sets the layer for a GameObject and all of its children.
        /// It stores the original layers in a dictionary so they can be restored.
        /// This is used to isolate the target object for rendering.
        /// </summary>
        /// <param name="obj">The root GameObject to start from.</param>
        /// <param name="newLayer">The new layer to assign.</param>
        /// <param name="originalLayers">A dictionary to store the original layers for restoration.</param>
        private static void SetLayerRecursively(GameObject obj, int newLayer, Dictionary<GameObject, int> originalLayers)
        {
            if (obj == null) return;
            originalLayers[obj] = obj.layer;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child != null && child.gameObject != null)
                {
                    SetLayerRecursively(child.gameObject, newLayer, originalLayers);
                }
            }
        }

        /// <summary>
        /// Restores the original layers to all GameObjects in the provided dictionary.
        /// </summary>
        /// <param name="originalLayers">The dictionary containing the GameObjects and their original layer indices.</param>
        private static void RestoreOriginalLayers(Dictionary<GameObject, int> originalLayers)
        {
            if (originalLayers == null) return;
            foreach (var pair in originalLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.layer = pair.Value;
                }
            }
        }

        /// <summary>
        /// The main public method of the service. It orchestrates the entire texture generation process.
        /// It takes settings from the UI, the source object's LOD index, and its pre-calculated bounds.
        /// It returns a data container with all the generated textures and metadata.
        /// </summary>
        /// <param name="settings">A data transfer object containing all settings from the UI.</param>
        /// <param name="sourceLODIndex">The specific LOD of the source object to use for the capture.</param>
        /// <param name="sourceObjectBounds">The pre-calculated bounds of the object to be captured.</param>
        /// <returns>A GeneratedTextureData object, or null on failure.</returns>
        public GeneratedTextureData GenerateAtlasAndSnapshots(TextureGeneratorSettings settings, int sourceLODIndex, Bounds sourceObjectBounds, RenderPipelineType pipelineType)
        {
            // --- 1. Initial Validation ---
            if (settings.TargetObject == null) { Debug.LogError("Target Object not set."); return null; }
            if (settings.GenerateNormalMap && settings.NormalCaptureShader == null) { Debug.LogError("Normal Capture Shader missing, cannot generate normal map."); return null; }

            int selectedSnapshotLayer = settings.SnapshotLayerIndex;
            if (settings.GenerateNormalMap) SetupNormalCaptureMaterial(settings.NormalCaptureShader);

            // --- 2. Setup and State Backup ---
            // This section prepares for the capture process and saves the current scene state
            // so it can be perfectly restored afterward, ensuring the tool doesn't permanently
            // alter the user's scene.

            GeneratedTextureData outputData = new GeneratedTextureData();

            // Declare variables for all the temporary objects we will create.
            GameObject instance = null;
            GameObject cameraGO = null;
            Camera snapshotCamera = null;
            GameObject tempKeyLightGO = null;
            GameObject tempFillLightGO = null;

            // Backup current scene lighting settings.
            AmbientMode originalAmbientMode = RenderSettings.ambientMode;
            Color originalAmbientLight = RenderSettings.ambientLight;
            float originalAmbientIntensityVal = RenderSettings.ambientIntensity;
            SphericalHarmonicsL2 originalAmbientProbe = RenderSettings.ambientProbe;
            DefaultReflectionMode originalReflectionMode = RenderSettings.defaultReflectionMode;
            Cubemap originalCustomReflection = null;
            bool customReflectionModeWasActive = (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom);
            if(customReflectionModeWasActive) { try { originalCustomReflection = RenderSettings.customReflectionTexture as Cubemap; } catch (System.ArgumentException e) { Debug.LogWarning($"Could not retrieve customReflection: {e.Message}"); originalCustomReflection = null; } }

            // Find and disable all active lights in the scene to create a controlled environment.
            var activeLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled && l.gameObject.scene.isLoaded).ToList();
            List<bool> lightStates = activeLights.Select(l => l.enabled).ToList();

            // Dictionaries to hold original state for restoration.
            Dictionary<GameObject, int> originalInstanceLayers = new Dictionary<GameObject, int>();
            int originalCameraCullingMask = 0;

            // --- 3. Main Generation Process (within a try...finally block) ---
            // The entire process is wrapped in a try...finally block. This is extremely important.
            // It guarantees that the 'finally' block will execute, cleaning up all temporary objects
            // and restoring the original scene settings, even if an error occurs during generation.
            try
            {
                // Instantiate a temporary copy of the source object for capturing.
                // The bounds are passed in, so we don't calculate them here.
                instance = BillboardSnapshotWindow.PrepareInstanceForSnapshot(settings.TargetObject, sourceLODIndex, true);

                if (instance == null) { Debug.LogError("Failed to instantiate and prepare source prefab."); return null; }
                instance.name = settings.TargetObject.name + "_SnapshotInstance";

                // Move the instance to the dedicated snapshot layer to isolate it.
                SetLayerRecursively(instance, selectedSnapshotLayer, originalInstanceLayers);
                instance.SetActive(true);

                // Wait a moment to ensure editor changes are applied before rendering.
                EditorApplication.QueuePlayerLoopUpdate();
                System.Threading.Thread.Sleep(20);

                // Validate the bounds that were passed into the method.
                if (sourceObjectBounds.size.sqrMagnitude < 0.0001f)
                {
                    Debug.LogError($"Provided prefab bounds are zero: {sourceObjectBounds.size}. Check bounds calculation in the window.");
                    UnityEngine.Object.DestroyImmediate(instance);
                    return null;
                }

                // --- 4. Camera and Lighting Setup ---
                // Create and configure a temporary camera for taking the snapshots.
                cameraGO = new GameObject("SnapshotServiceCamera");
                snapshotCamera = cameraGO.AddComponent<Camera>();

                // Set the camera to only see the snapshot layer.
                originalCameraCullingMask = snapshotCamera.cullingMask;
                snapshotCamera.cullingMask = (1 << selectedSnapshotLayer);

// --- NEW: PIPELINE-AWARE COMPONENT LOGIC ---
                // This section adds the required additional data components to the temporary camera
                // based on the active render pipeline. This is crucial for the camera to render correctly.
                #if UNITY_URP
                if (pipelineType == RenderPipelineType.URP)
                {
                    var urpCamData = cameraGO.AddComponent<UniversalAdditionalCameraData>();
                    urpCamData.renderShadows = false;
                    urpCamData.requiresColorOption = CameraOverrideOption.Off;
                    urpCamData.requiresDepthOption = CameraOverrideOption.Off;
                }
                #endif
                #if UNITY_HDRP
                if (pipelineType == RenderPipelineType.HDRP)
                {
                    var hdCamData = cameraGO.AddComponent<HDAdditionalCameraData>();
                    hdCamData.backgroundColorHDR = new Color(0, 0, 0, 0);
                    hdCamData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                    hdCamData.volumeLayerMask = 0; // Ignore post-processing volumes
                }
                #endif

                // Basic camera setup: clear to transparent, orthographic projection.
                snapshotCamera.clearFlags = CameraClearFlags.SolidColor;
                snapshotCamera.backgroundColor = new Color(0, 0, 0, 0);
                snapshotCamera.orthographic = true;
                snapshotCamera.enabled = false; // The camera is only rendered manually.

                // Disable all scene lights and set a temporary, flat ambient light.
                activeLights.ForEach(l => { if (l != null) l.enabled = false; });
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * settings.Lighting.AmbientLightMultiplier;
                RenderSettings.ambientIntensity = settings.Lighting.AmbientIntensity;

                // Create temporary key and fill lights for the snapshot scene.
                tempKeyLightGO = new GameObject("SnapshotTempKeyLight_Service");
                Light tempKeyLight = tempKeyLightGO.AddComponent<Light>();
                tempKeyLight.type = LightType.Directional;
                tempKeyLight.intensity = settings.Lighting.KeyLightIntensity;
                tempKeyLight.shadows = LightShadows.None;

                tempFillLightGO = new GameObject("SnapshotTempFillLight_Service");
                Light tempFillLight = tempFillLightGO.AddComponent<Light>();
                tempFillLight.type = LightType.Directional;
                tempFillLight.intensity = settings.Lighting.KeyLightIntensity * 0.45f; // Fill light is less intense.
                tempFillLight.shadows = LightShadows.None;
                tempFillLight.renderMode = LightRenderMode.ForcePixel; // Ensures consistent lighting.

                if (tempKeyLightGO != null) tempKeyLightGO.layer = selectedSnapshotLayer;
                if (tempFillLightGO != null) tempFillLightGO.layer = selectedSnapshotLayer;



                // --- NEW: HDRP LIGHT COMPONENT LOGIC ---
                // HDRP requires all lights to have an HDAdditionalLightData component to function.
#if UNITY_HDRP
                if (pipelineType == RenderPipelineType.HDRP)
                {
                    tempKeyLightGO.AddComponent<HDAdditionalLightData>();
                    tempFillLightGO.AddComponent<HDAdditionalLightData>();
                }
#endif
                // Set initial light rotations based on user settings. These will be adjusted for each view.

                Quaternion keyLightUserRotation = Quaternion.Euler(settings.Lighting.KeyLightRotation);
                Quaternion fillLightRelativeRotation = Quaternion.Euler(settings.Lighting.KeyLightRotation.x + 30f, settings.Lighting.KeyLightRotation.y + 150f, settings.Lighting.KeyLightRotation.z + 20f);

                // Initialize lists and variables for the capture loop.
                int totalAtlasPixelWidth = 0;
                List<Texture2D> tempAlbedoSnapshots = new List<Texture2D>();
                List<Texture2D> tempNormalSnapshots = new List<Texture2D>();

                // Determine how many radial views to actually capture.
                int numRadialViewsToCapture = settings.Views;
                if (settings.OptimizeTexturesForFrontFace && settings.Views > 1)
                {
                    // For efficient mode, we only need to capture the front-facing views.
                    // The back will be handled by a double-sided shader.
                    numRadialViewsToCapture = settings.Views / 2;
                    if (numRadialViewsToCapture == 0 && settings.Views > 0) numRadialViewsToCapture = 1;
                }
                outputData.Views = numRadialViewsToCapture;

                // Setup for the progress bar displayed in the Unity Editor.
                int totalViewsToProcessForProgressBar = numRadialViewsToCapture + (settings.EnableHorizontalCap ? 1 : 0);
                int currentViewProcessedForProgressBar = 0;
                float angleStep = (settings.Views > 0) ? 360.0f / settings.Views : 0;

                // --- 5. Capture Loops ---
                // The actual rendering happens in the loops below.
                // --- Radial Capture Loop ---
                // This loop iterates through each angle around the object to capture the side views.
                if (numRadialViewsToCapture > 0)
                {
                    for (int i = 0; i < numRadialViewsToCapture; i++)
                    {
                        currentViewProcessedForProgressBar++;
                        EditorUtility.DisplayProgressBar("Generating Snapshots", $"Radial View {i + 1}/{numRadialViewsToCapture}", (float)currentViewProcessedForProgressBar / totalViewsToProcessForProgressBar);

                        // Calculate the direction for the camera for this specific angle.
                        Vector3 cameraViewFromDir = Quaternion.Euler(0, i * angleStep, 0) * Vector3.forward;

                        // Determine the camera's distance from the object's center. It needs to be far enough to see the whole object.
                        float maxBoundExtent = Mathf.Max(sourceObjectBounds.extents.x, sourceObjectBounds.extents.y, sourceObjectBounds.extents.z, 0.01f);
                        float effectiveCamDistance = maxBoundExtent + settings.Camera.CaptureDistanceOffset;

                        // Position and orient the camera to look at the object's center from the calculated direction.
                        snapshotCamera.transform.position = sourceObjectBounds.center + cameraViewFromDir * effectiveCamDistance;
                        snapshotCamera.transform.LookAt(sourceObjectBounds.center);

                        // Rotate the temporary lights relative to the camera's new orientation to maintain consistent lighting from the object's perspective.
                        tempKeyLight.transform.rotation = snapshotCamera.transform.rotation * keyLightUserRotation;
                        tempFillLight.transform.rotation = snapshotCamera.transform.rotation * fillLightRelativeRotation;

                        // Project the 3D bounding box into the camera's 2D view to find the precise width and height of the object in that view.
                        // This is crucial for framing the object perfectly without wasting texture space.
                        Matrix4x4 camWorldToLocal = snapshotCamera.transform.worldToLocalMatrix;
                        Vector3 bMin = Vector3.one * float.MaxValue, bMax = Vector3.one * float.MinValue;
                        for (int ix = -1; ix <= 1; ix += 2) for (int iy = -1; iy <= 1; iy += 2) for (int iz = -1; iz <= 1; iz += 2)
                                {
                                    Vector3 wc = sourceObjectBounds.center + Vector3.Scale(sourceObjectBounds.extents, new Vector3(ix, iy, iz));
                                    Vector3 lc = camWorldToLocal.MultiplyPoint3x4(wc);
                                    bMin = Vector3.Min(bMin, lc);
                                    bMax = Vector3.Max(bMax, lc);
                                }
                        float objViewW = Mathf.Max(0.01f, bMax.x - bMin.x), objViewH = Mathf.Max(0.01f, bMax.y - bMin.y);

                        // Store the calculated dimensions and camera direction for this view. This data will be used by the mesh generation service.
                        outputData.SourceObjectViewWidths.Add(objViewW);
                        outputData.SourceObjectViewHeights.Add(objViewH);
                        outputData.ViewFromDirections.Add(cameraViewFromDir);

                        // Set the camera's orthographic size and aspect ratio to tightly fit the object.
                        snapshotCamera.orthographicSize = (objViewH / 2f) * (1f + settings.Camera.FramePadding);
                        snapshotCamera.orthographicSize = Mathf.Max(snapshotCamera.orthographicSize, 0.01f);
                        snapshotCamera.aspect = objViewW / Mathf.Max(0.001f, objViewH);

                        // Adjust the camera's near and far clip planes to encompass only the object, improving rendering precision.
                        float distToAABBFront = effectiveCamDistance - maxBoundExtent;
                        float distToAABBBack = effectiveCamDistance + maxBoundExtent;
                        snapshotCamera.nearClipPlane = Mathf.Max(0.01f, distToAABBFront * 0.9f);
                        snapshotCamera.farClipPlane = distToAABBBack * 1.1f;

                        // Call the helper method to perform the actual rendering and processing for this single view.
                        // The returned width is added to the total atlas width.
                        totalAtlasPixelWidth += CaptureAndProcessSingleView(snapshotCamera, settings, settings.AtlasHeightResolution, instance, tempAlbedoSnapshots, tempNormalSnapshots, false, "");
                    }
                }

                // --- Horizontal (Top-Down) Capture ---
                // If requested, capture a top-down orthographic view of the object.
                if (settings.EnableHorizontalCap)
                {
                    currentViewProcessedForProgressBar++;
                    EditorUtility.DisplayProgressBar("Generating Snapshots", "Processing Top Ortho View", (float)currentViewProcessedForProgressBar / totalViewsToProcessForProgressBar);
                    // Call a specialized helper method for the orthographic capture.
                    totalAtlasPixelWidth += CaptureOrthographicView(instance, snapshotCamera, tempKeyLight, tempFillLight, settings, sourceObjectBounds, Vector3.down, outputData, tempAlbedoSnapshots, tempNormalSnapshots, "Top", settings.AtlasHeightResolution);
                }

                // --- 6. Atlas Creation ---
                // Now that all individual snapshots have been captured and processed, stitch them together into one large texture atlas.
                outputData.AtlasTotalWidth = totalAtlasPixelWidth;
                outputData.AtlasHeight = settings.AtlasHeightResolution;

                if (totalAtlasPixelWidth > 0 && outputData.AtlasHeight > 0)
                {
                    // Create the final atlas textures.
                    outputData.AlbedoAtlasTexture = new Texture2D(totalAtlasPixelWidth, outputData.AtlasHeight, TextureFormat.ARGB32, true);
                    if (settings.GenerateNormalMap && tempNormalSnapshots.Any())
                        outputData.NormalAtlasTexture = new Texture2D(totalAtlasPixelWidth, outputData.AtlasHeight, TextureFormat.ARGB32, true);

                    int currentX = 0;
                    // Loop through each temporary snapshot and copy its pixel data into the correct position in the atlas.
                    for (int i = 0; i < tempAlbedoSnapshots.Count; i++)
                    {
                        Texture2D aTex = tempAlbedoSnapshots[i];
                        if (outputData.AlbedoAtlasTexture != null)
                            outputData.AlbedoAtlasTexture.SetPixels(currentX, 0, aTex.width, aTex.height, aTex.GetPixels());

                        outputData.ProcessedAlbedoSnapshots.Add(aTex);
                        // Record the position and size of this snapshot within the atlas. This is crucial for setting the mesh UVs later.
                        outputData.AtlasPlacementRects.Add(new RectInt(currentX, 0, aTex.width, aTex.height));

                        if (settings.GenerateNormalMap && i < tempNormalSnapshots.Count && outputData.NormalAtlasTexture != null)
                        {
                            Texture2D nTex = tempNormalSnapshots[i];
                            outputData.NormalAtlasTexture.SetPixels(currentX, 0, nTex.width, nTex.height, nTex.GetPixels());
                            outputData.ProcessedNormalSnapshots.Add(nTex);
                        }
                        currentX += aTex.width; // Move the "paste" position for the next snapshot.
                    }
                    // Apply all the SetPixels changes to the GPU textures.
                    if (outputData.AlbedoAtlasTexture != null) outputData.AlbedoAtlasTexture.Apply(true, false);
                    if (outputData.NormalAtlasTexture != null) outputData.NormalAtlasTexture.Apply(true, false);
                }
                else if (tempAlbedoSnapshots.Any())
                {
                    // This case handles if snapshots were made but the final atlas is empty (e.g., zero width).
                    Debug.LogWarning("Atlas generation skipped due to zero width/height, but some snapshots were processed.");
                    outputData.ProcessedAlbedoSnapshots.AddRange(tempAlbedoSnapshots);
                    if (settings.GenerateNormalMap) outputData.ProcessedNormalSnapshots.AddRange(tempNormalSnapshots);
                    tempAlbedoSnapshots.Clear();
                    tempNormalSnapshots.Clear();
                }
            }
            catch (System.Exception e)
            {
                // If any error occurs during the 'try' block, it's caught here.
                Debug.LogError($"Error during texture generation: {e.Message}\n{e.StackTrace}");
                // Clear out any partially generated data and return null to indicate failure.
                outputData.Clear();
                outputData = null;
            }
            finally
            {
                // --- 7. Cleanup ---
                // This block ALWAYS runs, whether the 'try' block succeeded or failed.
                // It's essential for restoring the editor and scene to their original state.
                EditorUtility.ClearProgressBar();
                RestoreOriginalMaterials();
                if (normalCaptureMaterialInstance != null) { UnityEngine.Object.DestroyImmediate(normalCaptureMaterialInstance); normalCaptureMaterialInstance = null; }
                if (instance != null) RestoreOriginalLayers(originalInstanceLayers);
                if (snapshotCamera != null) snapshotCamera.cullingMask = originalCameraCullingMask;

                // Destroy all temporary GameObjects created during the process.
                if (cameraGO != null) UnityEngine.Object.DestroyImmediate(cameraGO);
                if (tempKeyLightGO != null) UnityEngine.Object.DestroyImmediate(tempKeyLightGO);
                if (tempFillLightGO != null) UnityEngine.Object.DestroyImmediate(tempFillLightGO);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);

                // Restore all original scene lighting settings.
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbientLight;
                RenderSettings.ambientIntensity = originalAmbientIntensityVal;
                RenderSettings.ambientProbe = originalAmbientProbe;
                RenderSettings.defaultReflectionMode = originalReflectionMode;
                if (customReflectionModeWasActive) RenderSettings.customReflectionTexture = originalCustomReflection;

                // Re-enable all the scene lights that were disabled.
                for (int i = 0; i < activeLights.Count; ++i)
                    if (activeLights[i] != null) activeLights[i].enabled = lightStates[i];
            }
            return outputData;
        }

        /// <summary>
        /// Handles the rendering and processing for a single camera view.
        /// </summary>
        /// <returns>The pixel width of the captured snapshot.</returns>
        private int CaptureAndProcessSingleView(Camera cam, TextureGeneratorSettings settings, int targetRtHeight, GameObject targetInstance, List<Texture2D> albedoSnaps, List<Texture2D> normalSnaps, bool isOrthoCapture, string orthoViewNameForDebug)
        {
            if (targetRtHeight <= 0)
            {
                Debug.LogWarning("CaptureAndProcessSingleView: targetRtHeight is zero or negative. Skipping capture.");
                return 0;
            }
            float aspect = cam.aspect;
            if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0) aspect = 1f;

            // Calculate render texture dimensions based on target height and camera aspect ratio.
            int rtWidth = Mathf.Max(1, Mathf.RoundToInt(targetRtHeight * aspect));

            // For supersampling, create a render texture at a multiple of the final size.
            int ssaaRtW = rtWidth * settings.SupersamplingFactor, ssaaRtH = targetRtHeight * settings.SupersamplingFactor;
            RenderTexture rtAlbSSAA = RenderTexture.GetTemporary(ssaaRtW, ssaaRtH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture rtNrmSSAA = null;
            if (settings.GenerateNormalMap) rtNrmSSAA = RenderTexture.GetTemporary(ssaaRtW, ssaaRtH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            // --- Albedo Capture ---
            cam.targetTexture = rtAlbSSAA;
            cam.backgroundColor = new Color(0, 0, 0, 0); // Render with a transparent background.
            if (settings.GenerateNormalMap) RestoreOriginalMaterials(); // Ensure original materials are used for albedo.
            cam.Render();

            // Debug logic to save intermediate snapshots if enabled.
            if (isOrthoCapture && DEBUG_SAVE_ORTHO_SNAPSHOTS && !string.IsNullOrEmpty(orthoViewNameForDebug))
            {
                // ... (Debug save logic) ...
            }

            // Read the pixels from the render texture into a standard Texture2D.
            // If supersampling, blit (copy with scaling) to a final-sized render texture first.
            Texture2D snapAlb;
            if (settings.SupersamplingFactor > 1)
            {
                RenderTexture rtFinAlb = RenderTexture.GetTemporary(rtWidth, targetRtHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(rtAlbSSAA, rtFinAlb);
                snapAlb = ReadPixelsToTexture2D(rtFinAlb);
                RenderTexture.ReleaseTemporary(rtFinAlb);
            }
            else snapAlb = ReadPixelsToTexture2D(rtAlbSSAA);

            RenderTexture.ReleaseTemporary(rtAlbSSAA);
            Texture2D snapNrm = null;

            // --- Normal Map Capture ---
            if (settings.GenerateNormalMap && normalCaptureMaterialInstance != null && rtNrmSSAA != null)
            {
                ApplyNormalCaptureMaterials(targetInstance); // Swap to normal capture material.
                cam.targetTexture = rtNrmSSAA;
                cam.backgroundColor = new Color(0.5f, 0.5f, 1f, 1f); // Background color for normals (neutral blue).
                cam.Render();
                RestoreOriginalMaterials(); // Immediately restore original materials.

                if (settings.SupersamplingFactor > 1)
                {
                    RenderTexture rtFinNrm = RenderTexture.GetTemporary(rtWidth, targetRtHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    Graphics.Blit(rtNrmSSAA, rtFinNrm);
                    snapNrm = ReadPixelsToTexture2D(rtFinNrm);
                    RenderTexture.ReleaseTemporary(rtFinNrm);
                }
                else snapNrm = ReadPixelsToTexture2D(rtNrmSSAA);

                RenderTexture.ReleaseTemporary(rtNrmSSAA);
            }
            else if (settings.GenerateNormalMap)
            {
                // If normal map is requested but fails, create a blank (neutral) normal map.
                snapNrm = new Texture2D(rtWidth, targetRtHeight, TextureFormat.ARGB32, false);
                snapNrm.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 1f, 1f), rtWidth * targetRtHeight).ToArray());
                snapNrm.Apply();
            }

            // Process the captured textures (alpha clip, edge padding).
            ProcessSnapshotTexture(snapAlb, snapNrm, settings);
            albedoSnaps.Add(snapAlb);
            if (snapNrm != null) normalSnaps.Add(snapNrm);
            return rtWidth;
        }

        /// <summary>
        /// A specialized method to set up the camera for a top-down orthographic capture.
        /// </summary>
        /// <returns>The pixel width of the captured orthographic snapshot.</returns>
        private int CaptureOrthographicView(GameObject targetInstance, Camera cam, Light keyLight, Light fillLight,
            TextureGeneratorSettings settings, Bounds objectBounds,
            Vector3 cameraLookAxis, GeneratedTextureData outputData,
            List<Texture2D> albedoSnaps, List<Texture2D> normalSnaps, string viewName, int targetOrthoHeight)
        {
            float halfObjectHeightY = objectBounds.extents.y;
            float effectiveCamDistY = halfObjectHeightY + settings.Camera.CaptureDistanceOffset;

            Vector3 camViewFromDir; Vector3 camUpVec;
            // Setup camera direction and "up" vector for a top-down view.
            if (cameraLookAxis == Vector3.down)
            {
                camViewFromDir = Vector3.up;
                camUpVec = Vector3.forward;
            }
            else { Debug.LogError($"CaptureOrthographicView: Unexpected cameraLookAxis {cameraLookAxis}"); return 0; }

            cam.transform.position = objectBounds.center + camViewFromDir * effectiveCamDistY;
            cam.transform.LookAt(objectBounds.center, camUpVec);

            // Set a fixed lighting direction for the top-down view for consistency.
            float lightYaw = settings.Lighting.KeyLightRotation.y;
            keyLight.transform.rotation = Quaternion.Euler(45f, lightYaw, 0f);
            fillLight.transform.rotation = Quaternion.Euler(30f, lightYaw + 180f, 0f);

            // The width and depth for the top-down view come from the object's X and Z bounds.
            float orthoCaptureWidth = objectBounds.size.x;
            float orthoCaptureDepth = objectBounds.size.z;
            if (orthoCaptureWidth <= 0 || orthoCaptureDepth <= 0)
            {
                Debug.LogWarning($"CaptureOrthographicView: Invalid object bounds for ortho capture (W: {orthoCaptureWidth}, D: {orthoCaptureDepth}). Skipping.");
                return 0;
            }
            // Store the dimensions for the mesh service.
            outputData.SourceObjectViewWidths.Add(orthoCaptureWidth);
            outputData.SourceObjectViewHeights.Add(orthoCaptureDepth);
            outputData.ViewFromDirections.Add(camViewFromDir);

            // Set camera parameters to fit the object's top-down profile.
            cam.aspect = orthoCaptureWidth / Mathf.Max(0.001f, orthoCaptureDepth);
            cam.orthographicSize = (orthoCaptureDepth / 2.0f) * (1f + settings.Camera.FramePadding);
            cam.orthographicSize = Mathf.Max(cam.orthographicSize, 0.01f);

            float distToAABBTop = effectiveCamDistY - halfObjectHeightY;
            float distToAABBBottom = effectiveCamDistY + halfObjectHeightY;
            cam.nearClipPlane = Mathf.Max(0.01f, distToAABBTop * 0.9f);
            cam.farClipPlane = distToAABBBottom * 1.1f;

            // Call the general capture method with the ortho-specific setup.
            return CaptureAndProcessSingleView(cam, settings, targetOrthoHeight, targetInstance, albedoSnaps, normalSnaps, true, viewName);
        }

        /// <summary>
        /// A utility function to efficiently read pixels from a RenderTexture to a Texture2D.
        /// </summary>
        private Texture2D ReadPixelsToTexture2D(RenderTexture rt)
        {
            RenderTexture cur = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = cur;
            return tex;
        }

        /// <summary>
        /// Performs post-processing on a captured snapshot texture. This includes alpha clipping and edge padding.
        /// </summary>
        private void ProcessSnapshotTexture(Texture2D alb, Texture2D nrm, TextureGeneratorSettings s)
        {
            if (alb == null) return;
            Color[] pA = alb.GetPixels();
            int w = alb.width, h = alb.height;
            // Alpha Clipping: Make pixels either fully transparent or fully opaque based on a threshold.
            for (int i = 0; i < pA.Length; i++) pA[i].a = (pA[i].a >= s.AlphaProcessingClipThreshold) ? 1f : 0f;

            // Edge Padding: To prevent filtering artifacts (like dark halos), this process bleeds the color
            // from visible pixels into adjacent transparent pixels.
            if (s.EnableEdgePadding && s.EdgePaddingIterations > 0)
            {
                Color[] wp = (Color[])pA.Clone();
                for (int it = 0; it < s.EdgePaddingIterations; it++)
                {
                    Color[] pp = (Color[])wp.Clone();
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int id = y * w + x;
                            if (wp[id].a < 0.5f) // If this pixel is transparent...
                            {
                                Color avC = Color.clear; int ct = 0;
                                // ...check its neighbors.
                                int[] dX = { 0, 0, 1, -1, -1, 1, -1, 1 }, dY = { 1, -1, 0, 0, -1, -1, 1, 1 };
                                for (int n = 0; n < 8; n++)
                                {
                                    int nX = x + dX[n], nY = y + dY[n];
                                    if (nX >= 0 && nX < w && nY >= 0 && nY < h)
                                    {
                                        int nI = nY * w + nX;
                                        if (wp[nI].a >= 0.5f) // If a neighbor is opaque...
                                        {
                                            avC += wp[nI]; // ...add its color to an average.
                                            ct++;
                                        }
                                    }
                                }
                                if (ct > 0) pp[id] = new Color(avC.r / ct, avC.g / ct, avC.b / ct, 0f); // Set the transparent pixel's color to the average.
                            }
                        }
                    wp = pp;
                }
                pA = wp;
            }

            alb.SetPixels(pA);
            alb.Apply(true);

            // Apply the same transparency to the normal map so they match perfectly.
            if (nrm != null && s.GenerateNormalMap)
            {
                Color[] pN = nrm.GetPixels();
                Color nNA0 = new Color(0.5f, 0.5f, 1f, 0f); // Transparent normal color.
                for (int i = 0; i < pN.Length; i++)
                    pN[i] = (pA[i].a < 0.5f) ? nNA0 : new Color(pN[i].r, pN[i].g, pN[i].b, 1f);
                nrm.SetPixels(pN);
                nrm.Apply(true);
            }
        }
    }
}
