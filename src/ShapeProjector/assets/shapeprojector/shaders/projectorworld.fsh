#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Jonastech Shape Projector - world-pass fragment shader (OIT stage, spec 6).
// Verbatim engine blockhighlights.fsh (assets/game/shaders/blockhighlights.fsh, 1.22.7): the OIT()
// helper from the oit.fsh include writes the transparency stage's accumulation and reveal targets,
// which is what makes this program usable only inside EnumRenderStage.OIT (api-notes d.2).
// ASCII ONLY (see projectorghost.vsh).

in vec4 color;
in float glowLevel;
in vec4 rgbaFog;

uniform sampler2D particleTex;

#include fogandlight.fsh
#include oit.fsh

void main()
{
    OIT(color, glowLevel);
}
