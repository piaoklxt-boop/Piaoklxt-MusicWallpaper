// GPL-3.0: exercises the bundled mod's production parser and tint composition.
#include "../../third_party/ExplorerThemeRuntime/licenses/windows-11-file-explorer-styler.wh.cpp"
#include <cstdio>

int main() {
    auto parse = [](std::wstring_view value) {
        return std::get<XamlBlurBrushParams>(*ParseNonXamlPropertyOverrideValue(value));
    };
    auto header = parse(L"<WindhawkBlur TintColor=\"{ThemeResource SystemAccentColor}\" TintOpacity=\"0.38\" OpaqueBase=\"True\"/>");
    if (!header.opaqueBase || header.tintThemeResourceKey != L"SystemAccentColor" || !header.tintOpacity) return 1;
    auto existing = parse(L"<WindhawkBlur TintColor=\"#FFFFFF\" TintOpacity=\"0.38\"/>");
    if (existing.opaqueBase) return 2;
    auto neutral = CompositeHeaderTint({0, 255, 0, 0});
    auto full = CompositeHeaderTint({255, 30, 140, 200});
    if (neutral.A != 255 || neutral.R != 32 || neutral.G != 32 || neutral.B != 32) return 3;
    if (full.A != 255 || full.R != 30 || full.G != 140 || full.B != 200) return 4;
    auto first = CompositeHeaderTint({*header.tintOpacity, 225, 151, 119});
    auto second = CompositeHeaderTint({*header.tintOpacity, 119, 151, 225});
    if (first.A != 255 || first.R != 105 || first.G != 77 || first.B != 65) return 5;
    if (second.A != 255 || second.R != first.B || second.B != first.R) return 6;
    puts("Header parser preserves theme binding; opaque tint matches inactive material; existing blur remains opt-in. PASS");
    return 0;
}
