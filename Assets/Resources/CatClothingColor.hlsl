#ifndef DOODLIK_CAT_CLOTHING
#define DOODLIK_CAT_CLOTHING

half4 DoodlikCatColor(half4 source, float2 textureUV)
{
    float2 uv = (textureUV - _SpriteUVRect.xy) / max(_SpriteUVRect.zw, float2(0.0001, 0.0001));
    float2 pixel = float2(uv.x * 842.0, (1.0 - uv.y) * 758.0);
    // Follow the sloping upper edge of the original orange striped clothing.
    // The horizontal bounds exclude both the tail and the paws.
    float top = lerp(481.0, 463.0, saturate((pixel.x - 100.0) / 547.0));
    half zone = step(97.0, pixel.x) * step(pixel.x, 651.0) * step(top, pixel.y) * step(pixel.y, 646.0);
    half maximum = max(source.r, max(source.g, source.b));
    half minimum = min(source.r, min(source.g, source.b));
    // Black ink and pale crayon scratches remain the original pixels.
    half pigment = smoothstep(0.05h, 0.25h, maximum - minimum) * smoothstep(0.06h, 0.20h, maximum);
    half3 dyed = lerp(source.rgb, _ClothingColor.rgb * maximum, pigment * _ClothingStrength);
    half3 fur = source.rgb * _BodyTint.rgb;
    // Trim the original upper-head fringe outside the fitted helmet.
    half headInterior = smoothstep(96.0, 104.0, pixel.x) *
        (1.0 - smoothstep(638.0, 646.0, pixel.x));
    half upperHead = 1.0 - smoothstep(425.0, 450.0, pixel.y);
    // The original right cheek otherwise peeks through between the helmet's
    // curved lower rim and the suit. This is a narrow source-sprite region,
    // above the tail; keep the face inside the visor and the tail untouched.
    half rightShoulder = smoothstep(570.0, 584.0, pixel.x) *
        (1.0 - smoothstep(635.0, 650.0, pixel.x));
    half seamHeight = smoothstep(342.0, 358.0, pixel.y) *
        (1.0 - smoothstep(412.0, 432.0, pixel.y));
    half trim = saturate(_AstronautHeadMask * upperHead * (1.0 - headInterior) +
        _AstronautSuitSeamMask * rightShoulder * seamHeight);
    half visibleAlpha = 1.0 - trim;
    return half4(lerp(fur, dyed, zone), source.a * visibleAlpha);
}
#endif
