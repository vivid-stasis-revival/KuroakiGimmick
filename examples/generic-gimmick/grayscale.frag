varying vec2 v_vTexcoord;
varying vec4 v_vColour;
uniform sampler2D gm_BaseTexture;
uniform float amount;
void main() {
    vec4 color = texture2D(gm_BaseTexture, v_vTexcoord);
    float gray = dot(color.rgb, vec3(0.299, 0.587, 0.114));
    gl_FragColor = vec4(mix(color.rgb, vec3(gray), clamp(amount, 0.0, 1.0)), color.a) * v_vColour;
}
