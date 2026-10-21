#version 450
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _CenterEast;
    vec4 _NorthAnim;
    vec4 _ScreenParams;
    float _CoreSize;
} material;
layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _Singularity;

float border(vec2 p, vec2 size)
{
    vec2 d = min(p, vec2(1.0) - p) * size;
    return min(d.x, d.y);
}

void main()
{
    vec2 center = material._CenterEast.xy;
    mat2 basis = mat2(material._CenterEast.zw, material._NorthAnim.xy);
    vec2 local = inverse(basis) * (uv - center);
    float radius = length(local);
    vec2 sampleUv = uv;
    if (radius < 1.0)
    {
        float falloff = (1.0 - radius) * (1.0 - radius);
        float angle = (1.6 + material._NorthAnim.z * 1.2) * falloff * material._NorthAnim.w;
        float c = cos(angle), s = sin(angle);
        vec2 twisted = mat2(c, s, -s, c) * local * (1.0 + .12 * falloff * material._NorthAnim.w);
        vec2 samplePoint = center + basis * twisted;
        float edge = clamp(min(border(uv, material._ScreenParams.xy), border(samplePoint, material._ScreenParams.xy)) / 24.0, 0.0, 1.0);
        sampleUv = mix(uv, samplePoint, edge * edge * (3.0 - 2.0 * edge));
    }
    vec4 scene = texture(_MainTex, sampleUv);
    float pixels = max(material._CoreSize * material._ScreenParams.y, .001);
    vec2 core = (uv - center) * material._ScreenParams.xy / pixels;
    if (length(core) < .29) scene.rgb = vec3(0.0);
    if (all(lessThan(abs(core), vec2(.5))))
    {
        vec4 sprite = texture(_Singularity, core + vec2(.5));
        scene.rgb = mix(scene.rgb, sprite.rgb, sprite.a);
    }
    outColor = vec4(scene.rgb, 1.0);
}
