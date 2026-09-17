#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Jonastech Shape Projector - world-pass vertex shader (OIT stage, spec 6).
// This is the engine's blockhighlights.vsh (assets/game/shaders/blockhighlights.vsh, 1.22.7) with
// ONE line removed: "gl_Position.w += 0.0004;" - vanilla pretends its selection highlight is closer to
// the camera so it wins z-fights against the block face it lies on. Our marks are inset 0.05 blocks
// inside their cell and never share a plane with a block face, so they need no such push - and the
// push is not free: it is a constant in clip space, which in world units grows with distance
// (about 0.0004 * distance / (2 * zNear); zNear is 0.025..0.1 from the field of view, ClientMain.cs:865).
// Past roughly 17 blocks (FOV 70) it exceeds the inset, so a mark whose cell holds a real block won
// the depth test against that block and showed through it (user report 2026-09-17, "I can still see
// the projection through the soil blocks"). Honest depth here; the inset does the rest.
//
// ASCII ONLY in this file and its .fsh (see projectorghost.vsh for the truncation bug behind the rule).
// Vertex layout: xyz at location 0, rgba at location 1 (IRenderAPI.cs:519-521; blockhighlights.vsh:4-5).

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

out vec4 color;
out vec4 rgbaFog;

#include vertexflagbits.ash
#include shadowcoords.vsh
#include fogandlight.vsh

void main(void)
{
	vec4 cameraPos = modelViewMatrix * vec4(vertexPositionIn, 1.0);

	color = vertexColor;
	gl_Position = projectionMatrix * cameraPos;

	rgbaFog = vec4(0);
	glowLevel = 0;
}
