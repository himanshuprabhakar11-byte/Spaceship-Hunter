using UnityEngine;
using System.Collections.Generic;

namespace Veridian.Imposters
{
    // --- ENUMS ---
    // These enums define the core options and settings used throughout the billboard generation process.

    /// <summary>
    /// Defines the geometric shape of the vertical planes that make up the billboard.
    /// </summary>
    public enum BillboardProfile
    {
        /// <summary>A simple, rectangular plane. Most efficient and suitable for many objects.</summary>
        Quad,
        /// <summary>An 8-sided shape that can be tapered. Provides a tighter fit for objects like trees, reducing transparent space.</summary>
        Octagon
    }

    /// <summary>
    /// Defines whether a mesh face should be rendered from the front, or from both front and back.
    /// Note: This is not currently used in the main workflow but is kept for potential future features.
    /// </summary>
    public enum RenderFaceSetting { Front, Both }


    // --- DATA STRUCTURES ---
    // These structs are simple data containers used to group related parameters.

    /// <summary>
    /// Defines the properties for a single horizontal quad to be added to the billboard.
    /// Used for features like tree canopies.
    /// </summary>
    [System.Serializable]
    public struct HorizontalCrossSectionDefinition
    {
        /// <summary>The vertical position of the quad, as a fraction of the billboard's total height (0.0 = bottom, 1.0 = top).</summary>
        [Range(0f, 1f)]
        public float heightFraction;
        /// <summary>A multiplier for the quad's size, relative to the average width of the billboard's radial planes.</summary>
        [Range(0f, 1f)]
        public float sizeMultiplier;
        /// <summary>The rotation of the quad around the Y-axis, in degrees.</summary>
        [Range(0f, 360f)]
        public float rotationDegrees;

        public HorizontalCrossSectionDefinition(float fraction, float size = 0.5f, float rotation = 0f)
        {
            this.heightFraction = Mathf.Clamp01(fraction);
            this.sizeMultiplier = Mathf.Clamp01(size);
            this.rotationDegrees = rotation;
        }
    }

    /// <summary>
    /// A container for lighting settings used in the temporary snapshot scene.
    /// Ensures consistent lighting for every billboard capture.
    /// </summary>
    public struct SnapshotLightingSettings
    {
        public float AmbientLightMultiplier;
        public float AmbientIntensity;
        public float KeyLightIntensity;
        public Vector3 KeyLightRotation;
    }

    /// <summary>
    /// A container for camera settings used during the snapshot process.
    /// </summary>
    public struct SnapshotCameraParameters
    {
        /// <summary>Extra distance added to the camera's position to prevent clipping.</summary>
        public float CaptureDistanceOffset;
        /// <summary>Padding added around the object in the camera's view to prevent texture artifacts.</summary>
        public float FramePadding;
    }

    /// <summary>
    /// A container for the parameters that define the shape of an 'Octagon' profile billboard.
    /// </summary>
    public struct OctagonShapeParameters
    {
        public float BottomWidthFraction;
        public float TopWidthFraction;
        public float MaxWidthCenterYFraction;
        /// <summary>The vertical size of the octagon's 'shoulders' (the widest part), as a fraction of total height.</summary>
        public float ShoulderVerticalFraction;
    }


    // --- DATA-TRANSFER CLASSES ---
    // These classes are used to pass complex data between different services and modules.

    /// <summary>
    /// Holds all the raw texture data generated from the snapshot process (Step 1).
    /// This object is the primary input for the mesh generation process (Step 2).
    /// </summary>
    public class GeneratedTextureData
    {
        /// <summary>A list of the individual albedo (color) snapshot textures before they are combined into an atlas.</summary>
        public List<Texture2D> ProcessedAlbedoSnapshots { get; set; } = new List<Texture2D>();
        /// <summary>A list of the individual normal map snapshot textures before they are combined into an atlas.</summary>
        public List<Texture2D> ProcessedNormalSnapshots { get; set; } = new List<Texture2D>();
        /// <summary>The final combined texture atlas for the albedo map.</summary>
        public Texture2D AlbedoAtlasTexture { get; set; }
        /// <summary>The final combined texture atlas for the normal map.</summary>
        public Texture2D NormalAtlasTexture { get; set; }
        /// <summary>A list of rectangles defining where each individual snapshot is placed within the final texture atlases.</summary>
        public List<RectInt> AtlasPlacementRects { get; set; } = new List<RectInt>();
        /// <summary>The world-space directions from which each snapshot was taken.</summary>
        public List<Vector3> ViewFromDirections { get; set; } = new List<Vector3>();
        /// <summary>The calculated world-space width of the object from each snapshot's perspective.</summary>
        public List<float> SourceObjectViewWidths { get; set; } = new List<float>();
        /// <summary>The calculated world-space height of the object from each snapshot's perspective.</summary>
        public List<float> SourceObjectViewHeights { get; set; } = new List<float>();
        /// <summary>The number of radial views (e.g., front, side, back) that were captured.</summary>
        public int Views { get; set; }
        /// <summary>The total width, in pixels, of the final generated texture atlases.</summary>
        public int AtlasTotalWidth { get; set; }
        /// <summary>The height, in pixels, of the final generated texture atlases.</summary>
        public int AtlasHeight { get; set; }

        /// <summary>
        /// Clears all data and destroys any created Texture2D objects to prevent memory leaks in the editor.
        /// </summary>
        public void Clear()
        {
            ProcessedAlbedoSnapshots.ForEach(UnityEngine.Object.DestroyImmediate);
            ProcessedAlbedoSnapshots.Clear();
            ProcessedNormalSnapshots.ForEach(UnityEngine.Object.DestroyImmediate);
            ProcessedNormalSnapshots.Clear();
            if (AlbedoAtlasTexture != null) UnityEngine.Object.DestroyImmediate(AlbedoAtlasTexture);
            AlbedoAtlasTexture = null;
            if (NormalAtlasTexture != null) UnityEngine.Object.DestroyImmediate(NormalAtlasTexture);
            NormalAtlasTexture = null;
            AtlasPlacementRects.Clear();
            ViewFromDirections.Clear();
            SourceObjectViewWidths.Clear();
            SourceObjectViewHeights.Clear();
            Views = 0;
            AtlasTotalWidth = 0;
            AtlasHeight = 0;
        }
    }

    /// <summary>
    /// A temporary container (Data Transfer Object) to pass all necessary parameters from the UI (BillboardSnapshotWindow)
    /// to the TextureGeneratorService. This keeps the service's method signature clean and organized.
    /// </summary>
    public class TextureGeneratorSettings
    {
        public GameObject TargetObject { get; set; }
        public int Views { get; set; }
        public int AtlasHeightResolution { get; set; }
        public int SupersamplingFactor { get; set; }
        public bool EnableEdgePadding { get; set; }
        public int EdgePaddingIterations { get; set; }
        public float AlphaProcessingClipThreshold { get; set; }
        public bool GenerateNormalMap { get; set; }
        public Shader NormalCaptureShader { get; set; }
        public SnapshotLightingSettings Lighting { get; set; }
        public SnapshotCameraParameters Camera { get; set; }
        public bool EnableHorizontalCap { get; set; }
        public bool OptimizeTexturesForFrontFace { get; set; }
        public int SnapshotLayerIndex { get; set; }
    }

    /// <summary>
    /// A temporary container (Data Transfer Object) to pass all necessary parameters from the UI (BillboardSnapshotWindow)
    /// to the BillboardMeshService.
    /// </summary>
    public class MeshGenerationSettings
    {
        public BillboardProfile ProfileShape { get; set; }
        public Bounds SourceObjectNormalizedBounds { get; set; }
        public OctagonShapeParameters OctagonParams { get; set; }
        public int Views { get; set; }
        public Vector3 SourcePrefabOriginalScale { get; set; }
        public List<HorizontalCrossSectionDefinition> HorizontalCrossSections { get; set; } = new List<HorizontalCrossSectionDefinition>();
        public float VerticalOffset { get; set; }
        public BillboardRenderMode RenderMode { get; set; }
    }
}