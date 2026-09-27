// This HLSL file contains the core GPU logic for the HDRP normal capture shader.
// It is included by the "NormalCapture_HDRP.shader" file.

// This struct defines the data that the GPU receives from the CPU for each vertex.
// The semantics (e.g., : POSITION) tell HDRP what kind of data to expect.
struct Attributes
{
    float4 positionOS : POSITION; // The vertex's position in its local object space.
    float3 normalOS : NORMAL; // The vertex's normal vector in object space.
    uint vertexID : SV_VertexID; // A system-generated ID for the vertex, needed for some HDRP functions.
};

// This struct defines the data passed from the vertex shader to the fragment shader.
// HDRP has specific requirements for this struct, often named "Varyings".
struct Varyings
{
    // The final position of the vertex in clip space (the 2D screen coordinate).
    // The SV_POSITION semantic is required for this output.
    float4 positionCS : SV_POSITION;

    // The calculated world-space normal, passed to the fragment shader via a TEXCOORD semantic.
    float3 normalWS : TEXCOORD0;
};

// The Vertex Shader Function for HDRP
// This function runs once for every vertex in the mesh.
Varyings vert(Attributes input)
{
    Varyings output; // Create an output struct to hold the results.

    // In HDRP, you first get the absolute world-space position of the vertex.
    // This is a more robust way to handle various rendering scenarios in the pipeline.
    float3 positionWS = GetAbsolutePosition(input.positionOS.xyz);

    // Then, transform the world-space position into the final clip-space (screen) position.
    // This uses the main view-projection matrix provided by HDRP.
    output.positionCS = TransformWorldToHClip(positionWS);

    // Transform the normal vector from the mesh's local object space to world space.
    // TransformObjectToWorldNormal is the standard HDRP function for this, correctly
    // handling object transformations like rotation and non-uniform scale.
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);

    return output; // Return the populated struct to be interpolated and passed to the fragment shader.
}

// The Fragment (Pixel) Shader Function for HDRP
// This function runs once for every pixel the mesh covers on the screen.
// Its job is to calculate and return the final color for that pixel.
float4 frag(Varyings input) : SV_Target
{
    // Normalize the incoming normal vector. This is a crucial step to ensure it's a
    // perfect unit vector after being interpolated across the triangle's surface.
    // This guarantees the accuracy of the baked normal map.
    float3 normal = normalize(input.normalWS);

    // This is the core logic: encode the normal vector into an RGB color.
    // A normal's components (X, Y, Z) are in the [-1, 1] range.
    // We remap this to the [0, 1] color range using the formula (vector * 0.5) + 0.5.
    // The result is a color that directly represents the direction of the world-space normal.
    // The alpha channel is set to 1.0 (fully opaque).
    float4 encodedNormal = float4(normal * 0.5 + 0.5, 1.0);

    // Return the final encoded color, which will be written to the render texture.
    return encodedNormal;
}
