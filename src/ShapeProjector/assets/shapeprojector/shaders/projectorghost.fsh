#version 330 core

// Jonastech Shape Projector - see-through pass fragment shader (spec 6/9).
// Depth compare copied from vanilla rift.fsh:26-33 with the hardcoded tolerance replaced by the
// seeThroughDepth uniform (recipe docs/api-notes.md f.4). linearDepth() and the zNear/zFar uniforms
// come from the engine include fogandlight.fsh (assets/game/shaderincludes/fogandlight.fsh:214-227);
// the "* zFar" conversion of the normalized depth difference to blocks is api-notes f.3.
// The scene depth texture holds only the opaque world (primary framebuffer depth, f.2), so:
//   behind <= 0  -> the fragment is in open view; the OIT pass already drew it -> discard (no double draw)
//   behind > tol -> buried deeper than seeThroughDepth -> hidden (spec 9: not a wallhack)
//   otherwise    -> draw dimmed, fading out with burial depth.

in vec4 color;

out vec4 outColor;

uniform sampler2D depthTex;
uniform vec2 invFrameSize;
uniform float seeThroughDepth;

#include fogandlight.fsh

void main()
{
	float x = gl_FragCoord.x * invFrameSize.x;
	float y = gl_FragCoord.y * invFrameSize.y;

	float zScene = linearDepth(texture(depthTex, vec2(x, y)).r);
	float zFrag  = linearDepth(gl_FragCoord.z);

	// View-space depth difference in blocks ("depth along the line of sight" per the design ruling).
	float behind = (zFrag - zScene) * zFar;

	// 0.05 epsilon: ghost cubes are inset 0.05, so a face coplanar with terrain sits at most this
	// far behind it; treating that as "visible" avoids double-drawing silhouette pixels.
	if (behind <= 0.05 || behind > seeThroughDepth) discard;

	float fade = 1.0 - behind / seeThroughDepth;
	outColor = vec4(color.rgb, color.a * 0.55 * fade);
}
