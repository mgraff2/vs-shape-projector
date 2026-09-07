#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Jonastech Shape Projector - see-through pass vertex shader (spec 6/9; recipe docs/api-notes.md f.4).
// Vertex layout matches the mod's RGBA-only MeshData: xyz at location 0, rgba at location 1
// (attribute-location rule IRenderAPI.cs:519-521; identical to blockhighlights.vsh:4-5).
// Include structure copied from the proven rift.vsh pairing (vertexflagbits.ash + fogandlight.vsh):
// fogandlight.vsh declares the outs the engine's includes expect (glowLevel, blockLight,
// blockBrightness under SHADOWQUALITY) and its presence makes ShaderProgramBase.Use() fill the
// default uniforms, zNear/zFar included (ShaderProgramBase.cs:276-318). No w bias here - this pass
// must compare true depth in the fragment shader.
//
// ASCII ONLY in this file and its .fsh (bug fixed 2026-09-07): the engine hands the assembled source
// to GL.ShaderSource as a .NET string, and the driver received it truncated by exactly the number of
// extra UTF-8 bytes the non-ASCII characters in these comments carried - two em-dashes cost the last
// four bytes, which cut "glowLevel = 0.0;" to "glowLevel = 0." and gave NVIDIA's
// "unexpected $end, expecting ',' or ';' at token <EOF>" on every client since 1.0.0 (vanilla shaders
// are pure ASCII, so the engine never shows it). The see-through pass was silently disabled all along.

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

out vec4 color;

#include vertexflagbits.ash
#include fogandlight.vsh

void main(void)
{
	color = vertexColor;
	gl_Position = projectionMatrix * (modelViewMatrix * vec4(vertexPositionIn, 1.0));
	glowLevel = 0.0;
}
