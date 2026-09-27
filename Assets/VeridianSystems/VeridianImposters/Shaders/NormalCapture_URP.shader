// This shader's purpose is to render a mesh's world-space normals as colors.
// It's a fundamental part of the billboard generation process, as the output
// texture from this shader becomes the billboard's normal map, allowing it to
// react to lighting as if it had 3D surface detail.
// This .shader file itself is primarily a structural wrapper; the actual
// graphics calculations are performed in the included HLSL file.

// The path "Hidden/..." ensures this shader does not appear in the standard
// material shader selection dropdowns, as it's an internal tool shader.
Shader "Hidden/AdvancedBillboardCreator/NormalCapture_URP"
{
    // The Properties block is where you would normally define variables
    // that can be adjusted in the material inspector (e.g., colors, sliders).
    // For this shader, no user-adjustable properties are needed, but an
    // empty Properties block is still required by Unity's shader syntax.
    Properties
    {
        // No properties are needed for this effect.
    }

    // A SubShader block contains the actual rendering commands. A shader can
    // have multiple SubShaders to support different hardware or render pipelines,
    // but for this tool, we only need one.
    SubShader
    {
        // Tags provide metadata to the render pipeline about how and when to use this shader.
        // "RenderPipeline" = "UniversalPipeline": This is crucial. It tells Unity that
        //   this SubShader is specifically designed for the Universal Render Pipeline (URP).
        //   URP will ignore SubShaders that don't have this tag or have a different one.
        // "RenderType"="Opaque": This tag classifies the shader as rendering non-transparent
        //   geometry. It helps the pipeline organize rendering order.
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType"="Opaque" }

        // A Pass block represents a single drawing operation for the object.
        // Simple shaders have one Pass; more complex effects might have several.
        Pass
        {
            // Assigns a name to the Pass. This is useful for debugging, especially when
            // using tools like Unity's Frame Debugger, as it clearly identifies this
            // drawing step.
            Name "NormalCapturePass"
            
            // This marks the beginning of a block of High-Level Shading Language (HLSL) code.
            // All the GPU programming logic happens between HLSLPROGRAM and ENDHLSL.
            HLSLPROGRAM

            // These are compiler directives, called "pragmas".
            // #pragma vertex vert: Tells the compiler that the function named 'vert'
            //   is the vertex shader program.
            #pragma vertex vert
            // #pragma fragment frag: Tells the compiler that the function named 'frag'
            //   is the fragment (or pixel) shader program.
            #pragma fragment frag

            // This is the most important line in the wrapper. It tells the compiler to
            // find the file "NormalCaptureURP.hlsl" and insert its entire contents
            // here before compilation. This keeps the shader logic separate from the
            // structural boilerplate, improving organization.
            #include "NormalCapture_URP.hlsl"

            // This marks the end of the HLSL code block.
            ENDHLSL
        }
    }
    // A Fallback defines which shader Unity should try to use if no SubShaders
    // in this file are compatible with the user's hardware.
    // 'Off' means there is no fallback; if this shader can't run, the object
    // will likely render in magenta to indicate an error. For an internal editor
    // tool, this is acceptable behavior.
    Fallback Off
}
