#version 450
// Use a globally unique basename: Waylith also resolves sources by leaf name.
layout(location = 0) in vec4 color;
layout(location = 0) out vec4 outColor;
void main() { outColor = color; }
