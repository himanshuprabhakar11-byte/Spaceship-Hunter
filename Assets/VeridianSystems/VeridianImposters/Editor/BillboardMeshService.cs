using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Veridian.Imposters
{
    /// <summary>
    /// This service class is responsible for generating the actual 3D mesh for the billboard imposters.
    /// It takes texture data (from TextureGeneratorService) and mesh settings
    /// to construct a mesh with radial planes and optional horizontal caps.
    /// It supports different shapes (Quad, Octagon) and render modes (Efficient, HighQuality).
    /// </summary>
    public class BillboardMeshService
    {
        // A small constant value to offset each radial quad slightly along its forward vector.
        // This helps prevent "z-fighting," an artifact where two planes at the same depth flicker.
        private const float QUAD_Z_OFFSET = 0.001f;

        /// <summary>
        /// Calculates the half-width of the billboard at a specific height.
        /// For a 'Quad' profile, this is constant. For an 'Octagon' profile, it varies based on the shape parameters,
        /// creating a tapered effect. This function is not currently used in the main mesh generation but is
        /// kept as a utility for potential future mesh generation algorithms.
        /// </summary>
        /// <param name="currentY">The current vertical position on the mesh being calculated.</param>
        /// <param name="totalHeight">The total height of the billboard mesh.</param>
        /// <param name="profile">The shape profile (Quad or Octagon).</param>
        /// <param name="octP">The parameters defining the octagon's shape.</param>
        /// <param name="maxAvailableHalfWidth">The maximum half-width of the billboard plane.</param>
        /// <returns>The calculated half-width at the given height.</returns>
        private float GetHalfWidthAtHeight(float currentY, float totalHeight, BillboardProfile profile, OctagonShapeParameters octP, float maxAvailableHalfWidth)
        {
            // If the profile is a simple Quad, the width is constant across the entire height.
            if (profile == BillboardProfile.Quad)
            {
                return maxAvailableHalfWidth;
            }
            else // Otherwise, calculate the width for the Octagon profile.
            {
                // Handle division-by-zero case for very short objects.
                if (totalHeight <= 0.001f) return maxAvailableHalfWidth * octP.BottomWidthFraction;

                // Normalize the current height to a 0-1 range.
                float y_norm = Mathf.Clamp01(currentY / totalHeight);

                // Calculate the normalized vertical positions of the octagon's "shoulders" (the widest part).
                float y_shoulder_bottom_norm = Mathf.Clamp01(octP.MaxWidthCenterYFraction - (octP.ShoulderVerticalFraction / 2.0f));
                float y_shoulder_top_norm = Mathf.Clamp01(octP.MaxWidthCenterYFraction + (octP.ShoulderVerticalFraction / 2.0f));

                // Sanity check to handle cases where shoulder fractions might be invalid.
                if (y_shoulder_bottom_norm > y_shoulder_top_norm)
                {
                    float midY = Mathf.Clamp01(octP.MaxWidthCenterYFraction);
                    if (Mathf.Abs(y_norm - midY) < 0.001f) return maxAvailableHalfWidth;
                    else if (y_norm < midY) y_shoulder_bottom_norm = y_shoulder_top_norm = y_norm;
                    else y_shoulder_top_norm = y_shoulder_bottom_norm = y_norm;
                }

                float widthFraction;
                // Determine which segment of the octagon the current height falls into and interpolate the width.
                if (y_norm <= y_shoulder_bottom_norm) // Below the shoulder (bottom taper)
                {
                    if (y_shoulder_bottom_norm < 0.0001f) widthFraction = Mathf.Lerp(octP.BottomWidthFraction, 1.0f, y_norm / 0.0001f);
                    else widthFraction = Mathf.Lerp(octP.BottomWidthFraction, 1.0f, y_norm / y_shoulder_bottom_norm);
                }
                else if (y_norm <= y_shoulder_top_norm) // Within the shoulder (max width)
                {
                    widthFraction = 1.0f;
                }
                else // Above the shoulder (top taper)
                {
                    float segmentHeight = 1.0f - y_shoulder_top_norm;
                    if (segmentHeight < 0.0001f) widthFraction = Mathf.Lerp(1.0f, octP.TopWidthFraction, (y_norm - y_shoulder_top_norm) / 0.0001f);
                    else widthFraction = Mathf.Lerp(1.0f, octP.TopWidthFraction, (y_norm - y_shoulder_top_norm) / segmentHeight);
                }
                // Return the final width, ensuring it's clamped between 0 and the max available width.
                return maxAvailableHalfWidth * Mathf.Clamp01(widthFraction);
            }
        }

        /// <summary>
        /// The main entry point for this service. It generates a complete billboard mesh based on the provided texture data and settings.
        /// </summary>
        /// <param name="textureData">The data object containing all captured textures, atlas info, and view dimensions.</param>
        /// <param name="meshParams">The settings object containing parameters for mesh shape, size, and render mode.</param>
        /// <returns>A new, fully constructed Mesh object, or null if generation fails.</returns>
        public Mesh GenerateBillboardMeshData(GeneratedTextureData textureData, MeshGenerationSettings meshParams)
        {
            // --- Input Validation ---
            if (textureData == null || textureData.AlbedoAtlasTexture == null) { Debug.LogError("MeshGen: Invalid texture data provided."); return null; }
            if (meshParams.SourceObjectNormalizedBounds.size == Vector3.zero) { Debug.LogError("MeshGen: SourceObjectNormalizedBounds has zero size, cannot generate mesh."); return null; }

            // Check if horizontal sections are requested.
            bool generateHorizontalSections = meshParams.HorizontalCrossSections != null && meshParams.HorizontalCrossSections.Count > 0;

            // If horizontal sections are needed, we require an extra texture entry for the top-down orthographic snapshot.
            if (generateHorizontalSections && textureData.AtlasPlacementRects.Count < meshParams.Views + 1)
            {
                Debug.LogError($"MeshGen: Not enough texture data for horizontal sections. Expected at least {meshParams.Views + 1} atlas entries for top-down ortho. Found: {textureData.AtlasPlacementRects.Count}");
                return null;
            }

            // --- Mesh Initialization ---
            // Construct a descriptive name for the new mesh asset.
            string meshNameSuffix = $"_{meshParams.RenderMode}";
            if (generateHorizontalSections) meshNameSuffix += "_HQuads";
            Mesh mesh = new Mesh { name = $"BillboardMesh_{meshParams.ProfileShape}_{meshParams.Views}Dir" + meshNameSuffix };

            // Initialize lists to hold the mesh data.
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            // Determine the base height of the mesh from the normalized bounds of the source object.
            float baseUnscaledMeshHeight = Mathf.Max(0.01f, meshParams.SourceObjectNormalizedBounds.size.y);
            float actualMeshHeightRaw = baseUnscaledMeshHeight;

            // The vertical offset corrects the pivot point. It's calculated from the original object's lowest point.
            Vector3 verticalOffsetVector = new Vector3(0, meshParams.VerticalOffset, 0);

            // A pre-calculated rotation to flip quads 180 degrees so they face outward from the center.
            Quaternion yRotation180 = Quaternion.Euler(0, 180, 0);
            // This list will store the widths of each radial view, used later for averaging if horizontal quads are needed.
            List<float> allActualMeshWidths = new List<float>();

            // --- Generate Radial Billboard Planes (Sides) ---
            // This loop creates one or more vertical planes (quads or octagons) arranged in a circle.
            for (int i = 0; i < meshParams.Views; i++)
            {
                // Get the data for the current view (angle).
                RectInt atlasPlacementRect = textureData.AtlasPlacementRects[i];
                Vector3 viewFromDirection = textureData.ViewFromDirections[i];
                float sourceObjectViewWidth = textureData.SourceObjectViewWidths[i];

                float currentActualMeshWidthRaw = sourceObjectViewWidth;
                if (generateHorizontalSections) allActualMeshWidths.Add(currentActualMeshWidthRaw);

                float halfMeshWidth = currentActualMeshWidthRaw / 2.0f;

                // Determine the rotation for this plane to face the correct direction.
                Quaternion lookRotation = Quaternion.LookRotation(viewFromDirection, Vector3.up);
                Quaternion finalQuadRotation = lookRotation * yRotation180;

                // Apply a small offset to prevent z-fighting between the planes.
                Vector3 offsetVector = finalQuadRotation * Vector3.forward * QUAD_Z_OFFSET * i;

                int vertStartIndex = vertices.Count;
                Vector3[] localVerts;
                int[] localTriIndices;

                // --- Define Local Vertex Positions and Triangles for the chosen profile ---
                if (meshParams.ProfileShape == BillboardProfile.Quad)
                {
                    // A simple rectangle with 4 vertices and 2 triangles.
                    localVerts = new Vector3[4];
                    localVerts[0] = new Vector3(-halfMeshWidth, 0, 0); localVerts[1] = new Vector3(halfMeshWidth, 0, 0);
                    localVerts[2] = new Vector3(-halfMeshWidth, actualMeshHeightRaw, 0); localVerts[3] = new Vector3(halfMeshWidth, actualMeshHeightRaw, 0);
                    localTriIndices = new int[] { 0, 2, 1, 1, 2, 3 }; // Standard quad triangle indices.
                }
                else // Octagon
                {
                    // A more complex 8-vertex shape that tapers at the top and bottom.
                    localVerts = new Vector3[8];
                    float H = actualMeshHeightRaw;
                    float W = currentActualMeshWidthRaw;
                    float hW = W / 2f;
                    OctagonShapeParameters octP = meshParams.OctagonParams;

                    // Calculate the corner positions of the octagon based on the user-defined parameters.
                    float p0x = hW * octP.BottomWidthFraction; float p0y = 0f;
                    float p3x = hW * octP.TopWidthFraction; float p3y = H;
                    float shoulderCenterY = H * octP.MaxWidthCenterYFraction;
                    float halfShoulderV = (H * octP.ShoulderVerticalFraction) / 2f;
                    float p1x = hW; float p1y = Mathf.Clamp(shoulderCenterY - halfShoulderV, p0y, p3y);
                    float p2x = hW; float p2y = Mathf.Clamp(shoulderCenterY + halfShoulderV, p1y, p3y);
                    if (p1y > p2y) p1y = p2y; // Ensure bottom shoulder point is not above top shoulder point.

                    // Define the 8 vertices of the octagon.
                    localVerts[0] = new Vector3(-p0x, p0y, 0); localVerts[1] = new Vector3(p0x, p0y, 0);
                    localVerts[2] = new Vector3(-p1x, p1y, 0); localVerts[3] = new Vector3(p1x, p1y, 0);
                    localVerts[4] = new Vector3(-p2x, p2y, 0); localVerts[5] = new Vector3(p2x, p2y, 0);
                    localVerts[6] = new Vector3(-p3x, p3y, 0); localVerts[7] = new Vector3(p3x, p3y, 0);

                    // Define the triangles to create the octagon shape from the 8 vertices.
                    localTriIndices = new int[] { 0, 1, 3, 0, 3, 2, 2, 3, 5, 2, 5, 4, 4, 5, 7, 4, 7, 6 };
                }

                // The normal vector for this plane, pointing away from the center.
                Vector3 faceNormalForQuad = finalQuadRotation * Vector3.forward;

                // --- Add Vertices, UVs, and Normals for the front-facing plane ---
                for (int v = 0; v < localVerts.Length; ++v)
                {
                    // Transform the local vertex position to its final world-space position and add it to the list.
                    vertices.Add(finalQuadRotation * localVerts[v] + offsetVector + verticalOffsetVector);

                    // Calculate the normalized U and V coordinates for this vertex within its own plane (0-1 range).
                    float uN = (localVerts[v].x + halfMeshWidth) / Mathf.Max(0.001f, currentActualMeshWidthRaw);
                    float vN = localVerts[v].y / Mathf.Max(0.001f, actualMeshHeightRaw);

                    // Map the normalized UVs to the correct rectangle within the texture atlas.
                    float uvXS = (float)atlasPlacementRect.x / textureData.AtlasTotalWidth;
                    float uvWS = (float)atlasPlacementRect.width / textureData.AtlasTotalWidth;
                    uvs.Add(new Vector2(uvXS + uN * uvWS, vN));

                    // All vertices on this plane share the same normal vector.
                    normals.Add(faceNormalForQuad);
                }
                // Add the triangle indices to the main list, offsetting them by the starting vertex index.
                for (int t = 0; t < localTriIndices.Length; ++t) triangles.Add(vertStartIndex + localTriIndices[t]);


                // --- Generate back-facing plane for HighQuality mode ---
                // This creates a second, identical plane facing the opposite direction. This allows for
                // correct two-sided lighting, as opposed to "Efficient" mode which uses a double-sided shader.
                if (meshParams.RenderMode == BillboardRenderMode.HighQuality)
                {
                    // We need at least 2 views to find an opposite texture.
                    if (meshParams.Views < 2) continue;

                    // Calculate the index of the texture on the opposite side of the billboard.
                    int backViewIndex = (i + (meshParams.Views / 2)) % meshParams.Views;
                    RectInt backAtlasPlacementRect = textureData.AtlasPlacementRects[backViewIndex];

                    // This small offset pushes the back plane away from the front plane to prevent Z-fighting.
                    Vector3 backOffset = -faceNormalForQuad * 0.001f;

                    int backVertStartIndex = vertices.Count;

                    // Add vertices for the back plane.
                    for (int v = 0; v < localVerts.Length; ++v)
                    {
                        // Add the vertex, including the small backward offset.
                        vertices.Add(finalQuadRotation * localVerts[v] + offsetVector + verticalOffsetVector + backOffset);

                        // Calculate UVs, but flip the U coordinate so the back texture isn't mirrored.
                        float uN_flipped = 1.0f - ((localVerts[v].x + halfMeshWidth) / Mathf.Max(0.001f, currentActualMeshWidthRaw));
                        float vN = localVerts[v].y / Mathf.Max(0.001f, actualMeshHeightRaw);

                        // Map the flipped UVs to the correct back-face texture rectangle in the atlas.
                        float uvXS = (float)backAtlasPlacementRect.x / textureData.AtlasTotalWidth;
                        float uvWS = (float)backAtlasPlacementRect.width / textureData.AtlasTotalWidth;
                        uvs.Add(new Vector2(uvXS + uN_flipped * uvWS, vN));

                        // The normal for the back face must be inverted.
                        normals.Add(-faceNormalForQuad);
                    }

                    // Add triangles for the back face, but reverse the winding order (e.g., 0, 1, 2 -> 0, 2, 1).
                    // This is crucial to make the face point inward (visible from the back).
                    for (int t = 0; t < localTriIndices.Length; t += 3)
                    {
                        triangles.Add(backVertStartIndex + localTriIndices[t]);
                        triangles.Add(backVertStartIndex + localTriIndices[t + 2]); // Swapped
                        triangles.Add(backVertStartIndex + localTriIndices[t + 1]); // Swapped
                    }
                }
            }

            // --- Generate Horizontal Quad Planes (Optional) ---
            // This section adds one or more flat, horizontal quads, often used for tree canopies.
            if (generateHorizontalSections && meshParams.Views > 0)
            {
                // The top-down orthographic texture is always the last one in the data list.
                int topOrthoDataIndex = meshParams.Views;
                RectInt orthoAtlasRect = textureData.AtlasPlacementRects[topOrthoDataIndex];
                // Calculate an average width from all the radial views to determine a sensible default size.
                float averageMaxHalfWidth = (allActualMeshWidths.Any()) ? allActualMeshWidths.Average() / 2.0f : 0.1f;
                float baseQuadFullWidth = averageMaxHalfWidth * 2.0f;

                foreach (var sectionDef in meshParams.HorizontalCrossSections)
                {
                    // Calculate the Y position and size of this specific horizontal quad.
                    float currentYPlane = Mathf.Clamp01(sectionDef.heightFraction) * actualMeshHeightRaw;
                    float quadActualWidth = baseQuadFullWidth * sectionDef.sizeMultiplier;
                    float quadActualDepth = quadActualWidth; // Quads are square.

                    if (quadActualWidth < 0.001f) continue; // Skip if the quad is too small.

                    float halfW = quadActualWidth / 2.0f;
                    float halfD = quadActualDepth / 2.0f;

                    // Define the four corners of the quad in local space.
                    Vector3[] local_quad_verts = new Vector3[] { new Vector3(-halfW, 0, -halfD), new Vector3(halfW, 0, -halfD), new Vector3(-halfW, 0, halfD), new Vector3(halfW, 0, halfD) };
                    Quaternion rotation = Quaternion.Euler(0, sectionDef.rotationDegrees, 0);
                    Vector3[] world_quad_verts = new Vector3[4];
                    // Transform the local quad vertices to their final world positions.
                    for (int i = 0; i < 4; ++i)
                    {
                        world_quad_verts[i] = rotation * local_quad_verts[i] + new Vector3(0, currentYPlane, 0) + verticalOffsetVector;
                    }
                    // Standard UV coordinates for a full quad.
                    Vector2[] quad_uvs_local = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };

                    int topQuadBaseVertexIndex = vertices.Count;
                    // Add the vertices, normals, and UVs for the top-facing horizontal quad.
                    for (int i = 0; i < 4; ++i)
                    {
                        vertices.Add(world_quad_verts[i]);
                        normals.Add(Vector3.up); // Normal points straight up.
                        // Map the local UVs to the top-down orthographic texture's rectangle in the atlas.
                        float final_u = ((float)orthoAtlasRect.x + quad_uvs_local[i].x * orthoAtlasRect.width) / textureData.AtlasTotalWidth;
                        float final_v = ((float)orthoAtlasRect.y + quad_uvs_local[i].y * orthoAtlasRect.height) / textureData.AtlasHeight;
                        uvs.Add(new Vector2(final_u, final_v));
                    }
                    // Add the triangles for the top quad.
                    triangles.AddRange(new int[] { topQuadBaseVertexIndex + 0, topQuadBaseVertexIndex + 2, topQuadBaseVertexIndex + 1, topQuadBaseVertexIndex + 1, topQuadBaseVertexIndex + 2, topQuadBaseVertexIndex + 3 });

                    // In HighQuality mode, we also generate a bottom-facing quad.
                    // (Efficient mode uses a double-sided shader, so this isn't needed).
                    if (meshParams.RenderMode == BillboardRenderMode.HighQuality)
                    {
                        int bottomQuadBaseVertexIndex = vertices.Count;
                        // Add the vertices for the bottom quad.
                        for (int i = 0; i < 4; ++i)
                        {
                            vertices.Add(world_quad_verts[i]);
                            normals.Add(Vector3.down); // Normal points straight down.
                            uvs.Add(uvs[topQuadBaseVertexIndex + i]); // Reuse the same UVs as the top face.
                        }
                        // Add triangles with reversed winding order for the bottom face.
                        triangles.AddRange(new int[] { bottomQuadBaseVertexIndex + 0, bottomQuadBaseVertexIndex + 1, bottomQuadBaseVertexIndex + 2, bottomQuadBaseVertexIndex + 1, bottomQuadBaseVertexIndex + 3, bottomQuadBaseVertexIndex + 2 });
                    }
                }
            }

            // --- Finalize Mesh ---
            // Assign all the generated data to the Mesh object.
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);

            // Ask Unity to recalculate bounds and tangents, which are important for rendering and lighting.
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            return mesh;
        }
    }
}