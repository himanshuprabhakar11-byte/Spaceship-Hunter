
Advanced Billboard Creator

Thank you for downloading the Advanced Billboard Creator! This tool is designed to help you easily create high-quality, performant billboards (imposters) for your 3D models directly within the Unity Editor.

Full Documentation (Getting Started)
A comprehensive, 38-page user manual is available with detailed instructions, step-by-step guides, images, and tips for getting the best results.

Please read the full documentation before using the tool.

Click here to view the Full User Manual (Google Docs)
https://docs.google.com/document/d/1e_KemKKPbdJfjpAjFAwz8M10oLGDgDoSwaaUAlF4mXE/edit?usp=sharing

Render Pipeline Compatibility
This asset is designed to be compatible with all of Unity's main render pipelines, but HDRP requires a specific workflow.

Generator Tool:

Built-in & URP: The billboard creation tool works projects using the Built-in or URP render pipelines. It will automatically detect your project's pipeline and generate the correct shaders and materials.

HDRP: The tool is functional in HDRP, but requires a specific setup to get correct results. Please see the detailed instructions below.

Demo Scene & Demo Assets: To keep the package size minimal, the included demo scene and bonus assets are configured for the Universal Render Pipeline (URP) only. If you are using Built-in or HDRP, you can easily convert the demo materials by selecting them and changing their shader, or simply use the tool to generate new assets native to your pipeline.

Important Instructions for HDRP Users
To get correct results in a High Definition Render Pipeline (HDRP) project, a few manual steps are required.  (See below)

Quick Start
Open the Tool: In the Unity Editor, go to Tools > Advanced Billboard Creator.

Assign Settings: Create a new Billboard Settings asset (Create > Advanced Billboard Creator > Billboard Settings) and assign it to the "Settings Asset" field in the window.

Select Object: Drag the GameObject or Prefab you want to convert into the "Source Prefab" field.

Generate: Click "1. Generate Snapshot Atlas Data", then "2. Create Billboard".

Review: Your new billboard prefab will be created in a subfolder next to your original asset.

For detailed information on all settings, please refer to the full documentation linked above.

Support
If you have any questions or run into issues, please feel free to reach out. Thank you for using the asset!to reach out. Thank you for using the asset!


Special HDRP Considerations:

1. Add Scripting Define Symbol:
Before using the tool, you must add UNITY_HDRP to your project's Scripting Define Symbols.

Go to Edit > Project Settings > Player.

Find the "Scripting Define Symbols" field.

Add UNITY_HDRP to the list, separating it from others with a semicolon (e.g., EXISTING_SYMBOLS;UNITY_HDRP).

Click Apply. Unity will recompile its scripts.

2. Use an Unlit Shader for Capturing:
The primary challenge with HDRP is its lighting system, which is physically based and controlled by Volumes. When capturing an object that uses the standard HDRP/Lit shader, the intense lighting from the controlled capture environment often results in the billboard texture being almost entirely white (overexposed).

The recommended solution is to temporarily use an Unlit shader on your source object during the capture process.

Workflow:

Create a temporary material that uses a basic Unlit shader (e.g., HDRP/Unlit). Assign the base color texture from your original object to it.

Temporarily apply this Unlit material to the renderers of the object you want to capture.

Use the Billboard Creator tool to generate the billboard. The capture will now use the object's direct texture color without being affected by the intense lighting. You may need to adjust the Snapshot Lighting Settings in the tool to get the desired brightness.

After the billboard is created, you can revert the material on your original object back to the HDRP/Lit shader.

This method provides a reliable way to get a clean capture in HDRP. If you discover a workflow that allows for direct capture from the HDRP/Lit shader without overexposure, please let me know so I can update the asset for everyone!

