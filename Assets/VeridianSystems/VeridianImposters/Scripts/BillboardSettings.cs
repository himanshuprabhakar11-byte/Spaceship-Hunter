using UnityEngine;
using System.Collections.Generic;

namespace Veridian.Imposters
{
    // --- ENUMS ---
    // Defines the two primary methods for constructing the billboard's mesh and material.
    public enum BillboardRenderMode { Efficient, HighQuality }

    /// <summary>
    /// A ScriptableObject that holds all the configuration parameters for generating a billboard.
    /// What is a ScriptableObject? It's a data container that you can use to save large quantities of data,
    /// independent of class instances. This allows you to create, save, and reuse different billboard
    /// generation presets as assets in your project, which is great for managing different settings
    /// for various types of objects (e.g., "Tree_Settings", "Rock_Settings").
    /// </summary>
    [CreateAssetMenu(fileName = "New Billboard Settings", menuName = "Advanced Billboard Creator/Billboard Settings", order = 1)]
    public class BillboardSettings : ScriptableObject
    {
        // =================================================================================
        // CORE SETUP
        // These settings define the fundamental properties of the billboard, such as the
        // source object detail and the resolution of the output textures.
        // =================================================================================
        [Header("Core Setup")]
        [Tooltip("The LOD level of the source prefab to use for generating the billboard. LOD 0 is the highest quality.")]
        public int SourceLODIndex = 0;

        [Tooltip("The height of the output texture atlas for each view. Width is calculated automatically based on the object's aspect ratio.")]
        public int ResolutionIndex = 2; // Corresponds to [128, 256, 512, 1024, 2048]

        [Tooltip("The number of different angles the billboard will be captured from. More angles result in smoother transitions but larger textures.")]
        public int NumSnapshotsIndex = 1; // Corresponds to [4, 6, 8, 12, 16]

        // =================================================================================
        // CAPTURE SETTINGS
        // These settings control the camera and rendering environment during the texture capture process.
        // =================================================================================
        [Header("Capture Settings")]
        [Tooltip("How far the camera is positioned from the object's bounding box during capture. Increase this if parts of the model get clipped.")]
        public float CameraCaptureDistanceOffset = 2f;

        [Tooltip("Padding added around the object in the snapshot frame to prevent clipping and texture filtering artifacts (like dark halos).")]
        [Range(0f, 0.3f)]
        public float FramePadding = 0.05f;

        [Tooltip("If enabled, generates fewer radial textures (half). This saves memory but is incompatible with the 'HighQuality' Render Mode.")]
        public bool OptimizeTexturesForFrontFace = false;

        [Tooltip("The layer used to isolate the object during rendering. Use a dedicated, otherwise unused layer for best results.")]
        public int SnapshotLayerIndex = 0;

        [Tooltip("If enabled, a top-down orthographic snapshot will be taken for use with horizontal quads. This is set automatically if you add Horizontal Cross-Sections.")]
        public bool EnableHorizontalCap;

        // =================================================================================
        // IMAGE PROCESSING (POST-CAPTURE)
        // These settings apply post-processing effects to the captured images to improve quality and fix artifacts.
        // =================================================================================
        [Header("Image Processing (Post-Capture)")]
        [Tooltip("Renders the snapshot at a higher resolution and scales it down (Supersampling/SSAA) to reduce jagged edges (aliasing).")]
        public int SupersamplingIndex = 0; // Corresponds to [1x, 2x, 4x]

        [Tooltip("Extends the color of pixels at the edge of the billboard's alpha channel to prevent dark halos or seams from texture filtering.")]
        public bool EnableEdgePadding = true;

        [Range(1, 10)]
        [Tooltip("How many pixels deep the edge padding effect should extend. Higher values can fix more severe artifacts but take longer to process.")]
        public int EdgePaddingIterations = 3;

        [Tooltip("During texture processing, alpha values below this threshold will be discarded (made fully transparent). This helps clean up noise.")]
        [Range(0.01f, 0.99f)]
        public float AlphaProcessingClipThreshold = 0.1f;

        [Tooltip("If enabled, a normal map will be generated for the billboard, allowing it to interact more realistically with lighting.")]
        public bool GenerateNormalMap = true;

        // =================================================================================
        // HORIZONTAL CROSS-SECTION QUADS
        // Defines optional horizontal planes that can be added to the billboard, often used for tree canopies or other flat tops.
        // =================================================================================
        [Header("Horizontal Cross-Section Quads")]
        [Tooltip("Define horizontal quads. Each uses the top-down snapshot. Size is relative to average billboard width.")]
        public List<HorizontalCrossSectionDefinition> HorizontalCrossSectionDefs = new List<HorizontalCrossSectionDefinition>();

        // =================================================================================
        // BILLBOARD SHAPE & SIZE
        // These settings control the shape and final dimensions of the generated billboard mesh.
        // =================================================================================
        [Header("Billboard Shape & Size")]
        [Tooltip("The geometric profile of the vertical planes. Quad is a simple rectangle, while Octagon creates a more fitted, tapered shape.")]
        public BillboardProfile ProfileShape = BillboardProfile.Quad;

        [Tooltip("A manual vertical offset (in meters) applied to the final billboard prefab. Use this to fine-tune the height if the automatic pivot calculation is not perfect.")]
        public float ManualVerticalOffset = -.2f;

        [Tooltip("A small Z-offset applied to each successive radial plane to prevent z-fighting when planes overlap slightly.")]
        public float QuadOffsetAmount = 0.001f;

        [Tooltip("Multiplies the width (X/Z) of the final generated prefab to make it appear fuller. Does not affect height or the mesh itself.")]
        [Range(1.0f, 2.0f)]
        public float XZScaleFactor = 1.0f;


        // =================================================================================
        // OCTAGON PROFILE PARAMETERS
        // These parameters are only used if the 'ProfileShape' is set to 'Octagon'. They control the tapered shape of the mesh.
        // =================================================================================
        [Header("Octagon Profile Parameters")]
        [Range(0.01f, 1.0f)]
        [Tooltip("The width of the octagon's base as a fraction of its maximum width.")]
        public float OctagonBottomWidthFraction = 0.3f;

        [Range(0.01f, 1.0f)]
        [Tooltip("The width of the octagon's top as a fraction of its maximum width.")]
        public float OctagonTopWidthFraction = 0.2f;

        [Range(0.05f, 0.95f)]
        [Tooltip("The vertical center of the widest part (the 'shoulders') of the octagon, as a fraction of total height.")]
        public float OctagonMaxWidthCenterYFraction = 0.5f;

        // =================================================================================
        // SNAPSHOT LIGHTING SETTINGS
        // Controls the lighting conditions in the temporary scene where the billboard textures are rendered.
        // =================================================================================
        [Header("Snapshot Lighting Settings")]
        [Range(0f, 3f)]
        [Tooltip("A multiplier for the scene's ambient light color during capture.")]
        public float SnapshotAmbientLightMultiplier = 1.0f;

        [Range(0f, 3f)]
        [Tooltip("The intensity of the ambient light during capture.")]
        public float SnapshotAmbientIntensity = 1.0f;

        [Range(0f, 5f)]
        [Tooltip("The intensity of the main directional light (key light) during capture.")]
        public float SnapshotKeyLightIntensity = 1.0f;

        [Tooltip("The rotation of the main directional light, used to control the direction of shadows and highlights.")]
        public Vector3 SnapshotKeyLightRotation = new Vector3(45, -30, 0);

        // =================================================================================
        // MATERIAL SETTINGS
        // Controls the final output material's properties and the mesh generation strategy.
        // =================================================================================
        [Header("Material Settings")]
        [Tooltip("Determines the mesh and material strategy.\n\n• Efficient: Uses single planes with a double-sided shader. Most performant.\n\n• HighQuality: Creates separate front and back planes for better lighting fidelity, at the cost of more triangles.")]
        public BillboardRenderMode RenderMode = BillboardRenderMode.Efficient;

        [Tooltip("The alpha clip cutoff value set on the final output material's shader. Pixels with alpha below this value will not be rendered.")]
        [Range(0.0f, 1.0f)]
        public float MaterialAlphaClipThreshold = 0.5f;

        [Tooltip("Sets the smoothness/glossiness of the final material. Higher values result in a shinier surface.")]
        [Range(0.0f, 1.0f)]
        public float MaterialSmoothness = 0.0f;

        // =================================================================================
        // OUTPUT & LOD SETTINGS
        // These settings manage the file naming and Level of Detail (LOD) integration for the generated assets.
        // =================================================================================
        [Header("Output & LOD Settings")]
        [Tooltip("The prefix for all generated asset files (mesh, material, textures, prefab).")]
        public string UserDefinedBaseName = "Billboard";

        [Tooltip("If enabled, a new LOD will be added to the source prefab's LODGroup, containing the billboard.")]
        public bool EnableLODCreation = true;

        [Tooltip("The screen height percentage below which the object will be culled (disappear).")]
        [Range(0.1f, 20.0f)]
        public float CullCutoffPercent = 1.5f;

        [Tooltip("The screen height percentage at or below which the new billboard LOD becomes visible.")]
        [Range(0.2f, 99.0f)]
        public float BillboardLODTransitionPercent = 12.0f;

        /// <summary>
        /// If the source object is in the scene, this will destroy it after the billboard is successfully created.
        /// </summary>
        [Tooltip("IMPORTANT: If the source object is an object in the current scene (not a prefab from your project files), checking this will destroy that scene object after the billboard is created. This is useful for quickly replacing objects with their billboards.")]
        public bool DestroySourceSceneObject = false;
    }
}