#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Jonastech Shape Projector — see-through pass vertex shader (spec 6/9; recipe docs/api-notes.md f.4).
// Vertex layout matches the mod's RGBA-only MeshData: xyz at location 0, rgba at location 1
// (attribute-location rule IRenderAPI.cs:519-521; identical to blockhighlights.vsh:4-5).
// Include structure copied from the proven rift.vsh pairing (vertexflagbits.ash + fogandlight.vsh):
// fogandlight.vsh declares the outs the engine's includes expect (glowLevel, blockLight,
// blockBrightness under SHADOWQUALITY) and its presence makes ShaderProgramBase.Use() fill the
// default uniforms, zNear/zFar included (ShaderProgramBase.cs:276-318). No w bias here — this pass
// must compare true depth in the fragment shader.

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
