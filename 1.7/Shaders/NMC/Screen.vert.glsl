#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec2 inUv; // @vertex TexCoord0 required
layout(location = 0) out vec2 uv;
void main()
{
    gl_Position = vec4(inPosition, 1.0);
    // The ScreenEfx triangle supplies Unity UVs; sample Vulkan images top-down.
    uv = vec2(inUv.x, 1.0 - inUv.y);
}
