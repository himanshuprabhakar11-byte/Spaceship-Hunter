// This HLSL (High-Level Shading Language) file contains the core GPU logic for the normal capture shader.
// It is included by the .shader file and compiled by Unity. This script defines two main functions:
// a vertex shader, which processes each vertex of the 3D model, and a fragment shader, which
// calculates the final color for each pixel on the screen covered by the model.

// This #include directive is essential for URP. It brings in a library of helper functions,
// variables (like the view-projection matrix), and structs that are necessary for writing
// shaders that are compatible with the Universal Render Pipeline.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// This struct defines the data that the GPU receives from the CPU for each vertex of the mesh.
// The ": POSITION" and ": NORMAL" parts are called "semantics," and they tell the GPU what
// kind of data to expect in each variable.
struct Attributes
{
    // The vertex's position in its own local "object space".
    float4 positionOS : POSITION;
    // The vertex's normal vector in "object space".
    float3 normalOS : NORMAL;
};

// This struct defines the data that is passed from the vertex shader to the fragment shader.
// The GPU automatically interpolates these values across the surface of a triangle, so each
// fragment receives a smoothly blended value from the triangle's vertices.
struct Varyings
{
    // The final position of the vertex in "clip space," which is the 2D coordinate system of the screen.
    // The SV_POSITION semantic is required for this output.
    float4 positionCS : SV_POSITION;
    // The calculated world-space normal. We pass this to the fragment shader via a general-purpose
    // texture coordinate semantic (TEXCOORD0).
    float3 normalWS : TEXCOORD0;
};

// The Vertex Shader Function
// This function is executed once for every vertex in the mesh being rendered.
// Its primary jobs are to calculate the final screen position of the vertex and
// to perform any calculations that can be done per-vertex (like transforming normals).
Varyings vert(Attributes IN)
{
    Varyings OUT; // Create an output struct to hold the results.

    // Transform the vertex position from object space to clip space.
    // TransformObjectToHClip is the modern, pipeline-agnostic URP function for this common operation.
    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

    // Transform the normal vector from object space to world space.
    // GetVertexNormalInputs is the correct URP helper function to handle this transformation accurately,
    // accounting for non-uniform scaling of the object.
    VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);
    OUT.normalWS = normalInputs.normalWS;

    return OUT; // Return the populated struct to be passed to the fragment shader.
}

// The Fragment (Pixel) Shader Function
// This function is executed once for every pixel that the mesh covers on the screen.
// Its job is to calculate the final color of that single pixel.
half4 frag(Varyings IN) : SV_Target
{
    // The normal vector arrives from the vertex shader already interpolated across the triangle's surface.
    // We normalize it again here to ensure it's a perfect unit vector, which corrects for any minor
    // inaccuracies introduced during interpolation. This is crucial for an accurate normal map.
    float3 normal = normalize(IN.normalWS);

    // This is the core logic of the shader. A normal vector's components (X, Y, Z) are in the
    // range [-1, 1]. To store this data in a standard color texture, we must remap this range
    // to the [0, 1] color range. The formula (vector * 0.5) + 0.5 achieves this.
    // For example, a value of -1 becomes 0, 0 becomes 0.5, and 1 becomes 1.
    // The result is an RGB color that directly represents the XYZ direction of the world-space normal.
    // The alpha channel is set to 1.0 (fully opaque).
    half4 encodedNormal = half4(normal * 0.5h + 0.5h, 1.0h);

    // Return the final encoded color. This is what gets written to the render texture.
    return encodedNormal;
}
