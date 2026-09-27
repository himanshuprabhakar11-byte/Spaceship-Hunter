// This shader is specifically designed for Unity's Built-in (Standard) Render Pipeline.
// Its purpose is to render an object's world-space normals as colors, which are then
// baked into a normal map texture for the billboard.

// The internal name on the next line must EXACTLY match the string used in the C# script's
// Shader.Find() call. If they do not match, you will get a "shader not found" error.
Shader "Hidden/AdvancedBillboardCreator/NormalCapture_Builtin"
{
    // The Properties block is required for shader syntax but is empty
    // because this shader doesn't need any user-configurable parameters.
    Properties
    {
    }

    SubShader
    {
        // No specific tags are required for a simple shader in the Built-in pipeline,
        // unlike URP or HDRP.

        Pass
        {
            // This marks the start of a block of Cg/HLSL code.
            // For the Built-in pipeline, CGPROGRAM is traditionally used.
            CGPROGRAM

            // Compiler directives (pragmas) to specify the vertex and fragment shader functions.
            #pragma vertex vert
            #pragma fragment frag

            // This is the essential library for the Built-in pipeline. It contains a vast
            // number of helper functions and variables, including the transformation matrices
            // and functions we need below (like UnityObjectToClipPos).
            #include "UnityCG.cginc"

            // This struct defines the data passed from the vertex shader to the fragment shader.
            // The name v2f (vertex-to-fragment) is a common convention.
            struct v2f
            {
                // The vertex position in clip space (the final 2D screen position).
                // SV_POSITION is the required semantic for this output.
                float4 pos : SV_POSITION;
                
                // We'll pass the calculated world-space normal to the fragment shader
                // using the TEXCOORD0 semantic as a data channel.
                float3 worldNormal : TEXCOORD0;
            };

            // The Vertex Shader Function
            // This runs for each vertex of the mesh.
            // It uses 'appdata_base', a standard input struct defined in UnityCG.cginc,
            // which contains vertex position, normal, and one texture coordinate.
            v2f vert(appdata_base v)
            {
                v2f o; // Create an output struct.

                // UnityObjectToClipPos is the Built-in pipeline's function to transform
                // a vertex from its local object space into the final screen position.
                o.pos = UnityObjectToClipPos(v.vertex);

                // UnityObjectToWorldNormal is the function that correctly transforms the
                // normal vector from object space to world space, handling object rotation and scale.
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                return o;
            }

            // The Fragment (Pixel) Shader Function
            // This runs for each pixel covered by the mesh.
            fixed4 frag(v2f i) : SV_Target
            {
                // Normalize the incoming normal vector. This ensures it's a unit vector,
                // correcting for any minor imprecision from interpolation across the triangle.
                float3 normal = normalize(i.worldNormal);

                // Encode the normal vector (range -1 to 1) into a color (range 0 to 1).
                // This is the core logic that allows us to store directional data in a texture.
                // The 'fixed4' type is a lower-precision floating-point format, suitable for colors.
                return fixed4(normal * 0.5 + 0.5, 1.0);
            }
            ENDCG
        }
    }
    // No fallback is needed for this internal tool shader.
    Fallback Off
}
