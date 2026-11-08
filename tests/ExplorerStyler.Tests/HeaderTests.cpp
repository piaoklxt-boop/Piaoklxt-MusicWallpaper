// GPL-3.0: exercises the bundled mod's production parser and tint composition.
#include "../../third_party/ExplorerThemeRuntime/licenses/windows-11-file-explorer-styler.wh.cpp"
#include <cstdio>

bool TestNavigationBackground() {
    const wchar_t* classes[]{L"CabinetWClass", L"NamespaceTreeControl", L"SHELLDLL_DefView"};
    for (auto name : classes) {
        WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.lpszClassName = name;
        wc.hInstance = GetModuleHandleW(nullptr); RegisterClassW(&wc);
    }
    HWND root = CreateWindowW(classes[0], L"", WS_OVERLAPPEDWINDOW, 0,0,300,200,nullptr,nullptr,nullptr,nullptr);
    HWND navigation = CreateWindowW(classes[1], L"", WS_CHILD, 0,0,80,150,root,nullptr,nullptr,nullptr);
    HWND files = CreateWindowW(classes[2], L"", WS_CHILD, 0,0,200,150,root,nullptr,nullptr,nullptr);
    HDC navDC = GetDC(navigation), fileDC = GetDC(files);
    g_settings.sidebarAcrylic = false;
    bool classified = IsNavigationBackgroundDC(navDC) && !IsNavigationBackgroundDC(fileDC);
    BufferedPaintInit(); FillRect_Original = FillRect;
    g_entireWindowEffectWndForThread = root;
    HDC dc = CreateCompatibleDC(nullptr);
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = 8; bi.bmiHeader.biHeight = -8; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    void* pixels = nullptr;
    HBITMAP bitmap = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &pixels, nullptr, 0);
    auto previous = SelectObject(dc, bitmap);
    g_doubleBufferDCCountForThread = 1;
    g_doubleBufferDCsForThread[0] = {dc, {navigation, true}};
    RECT area{0,0,8,8};
    FillRect_Hook(dc, &area, GetSysColorBrush(COLOR_WINDOW));
    bool solid = static_cast<RGBQUAD*>(pixels)->rgbReserved == 255;
    g_settings.sidebarAcrylic = true;
    FillRect_Hook(dc, &area, GetSysColorBrush(COLOR_WINDOW));
    bool acrylic = *static_cast<DWORD*>(pixels) == 0;
    SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc);
    g_doubleBufferDCCountForThread = 0; g_entireWindowEffectWndForThread = nullptr;
    ReleaseDC(navigation, navDC); ReleaseDC(files, fileDC); DestroyWindow(root);
    return classified && solid && acrylic;
}

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
    if (!TestNavigationBackground()) return 7;
    puts("Native navigation switch changes actual GDI alpha while leaving the file-view owner separate. PASS");
    puts("Header parser preserves theme binding; opaque tint matches inactive material; existing blur remains opt-in. PASS");
    return 0;
}
