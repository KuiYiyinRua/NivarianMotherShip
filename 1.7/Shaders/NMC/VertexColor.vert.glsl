#version 450
layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 1) in vec4 inColor; // @vertex Color required
layout(push_constant) uniform Push { mat4 modelMatrix; } push;
layout(set = 0, binding = 0) uniform VertexUniforms { mat4 ViewProj; } view;
layout(location = 0) out vec4 color;
void main()
{
    gl_Position = view.ViewProj * push.modelMatrix * vec4(inPosition, 1.0);
    color = inColor;
}
