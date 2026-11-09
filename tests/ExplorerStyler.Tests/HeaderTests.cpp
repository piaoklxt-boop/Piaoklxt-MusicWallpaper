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
    INITCOMMONCONTROLSEX common{sizeof(common), ICC_TREEVIEW_CLASSES}; InitCommonControlsEx(&common);
    HWND tree = CreateWindowW(WC_TREEVIEWW, L"", WS_CHILD, 0,0,80,150,navigation,nullptr,nullptr,nullptr);
    TreeView_SetBkColor(tree, RGB(25,25,25)); // Captured dark navigation background; COLOR_WINDOW is white.
    HWND files = CreateWindowW(classes[2], L"", WS_CHILD, 0,0,200,150,root,nullptr,nullptr,nullptr);
    HDC navDC = GetDC(navigation), fileDC = GetDC(files);
    g_settings.sidebarAcrylic = false;
    bool classified = IsNavigationBackgroundDC(navDC) && !IsNavigationBackgroundDC(fileDC);
    BufferedPaintInit(); FillRect_Original = FillRect; PatBlt_Original = PatBlt;
    g_entireWindowEffectWndForThread = root;
    HDC dc = CreateCompatibleDC(nullptr);
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = 8; bi.bmiHeader.biHeight = -8; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    void* pixels = nullptr;
    HBITMAP bitmap = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &pixels, nullptr, 0);
    auto previous = SelectObject(dc, bitmap);
    g_doubleBufferDCCountForThread = 1;
    g_doubleBufferDCsForThread[0] = {dc, {tree, true}};
    RECT area{0,0,8,8};
    HBRUSH navBrush = CreateSolidBrush(TreeView_GetBkColor(tree));
    FillRect_Hook(dc, &area, navBrush);
    bool solid = static_cast<RGBQUAD*>(pixels)->rgbReserved == 255;
    ExtTextOutW_Original = ExtTextOutW;
    SetBkColor(dc, RGB(25,25,25));
    // Common controls clear item rows with ETO_OPAQUE and zero characters.
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, nullptr, 0, nullptr);
    bool rowOpaque = static_cast<RGBQUAD*>(pixels)->rgbReserved == 255;
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, L"A", 1, nullptr);
    bool textOpaque = static_cast<RGBQUAD*>(pixels)[63].rgbReserved == 255;
    g_settings.sidebarAcrylic = true;
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, nullptr, 0, nullptr);
    bool rowTransparent = *static_cast<DWORD*>(pixels) == 0;
    SetBkColor(dc, RGB(80,90,100));
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, nullptr, 0, nullptr);
    bool rowSelected = *static_cast<DWORD*>(pixels) == 0xFF505A64;
    g_settings.sidebarAcrylic = false;
    if (!rowOpaque) puts("Navigation row erasure punched a transparent hole in the opaque pane.");
    g_settings.sidebarAcrylic = true;
    FillRect_Hook(dc, &area, navBrush);
    bool acrylic = *static_cast<DWORD*>(pixels) == 0;
    // The other background fill path must obey the same control-level rule.
    auto oldBrush = SelectObject(dc, navBrush);
    g_settings.sidebarAcrylic = false;
    PatBlt_Hook(dc, 0,0,8,8, PATCOPY);
    bool patSolid = static_cast<RGBQUAD*>(pixels)->rgbReserved == 255;
    g_settings.sidebarAcrylic = true;
    PatBlt_Hook(dc, 0,0,8,8, PATCOPY);
    bool patAcrylic = *static_cast<DWORD*>(pixels) == 0;
    SelectObject(dc, oldBrush);
    g_settings.sidebarAcrylic = false;
    HBRUSH selection = CreateSolidBrush(RGB(80,90,100));
    bool selectionUntouched = !IsWindowBackgroundBrush(dc, selection);
    DeleteObject(selection);
    g_doubleBufferDCsForThread[0].wnd.hWnd = files;
    FillRect_Hook(dc, &area, navBrush);
    bool filesUntouched = (*static_cast<DWORD*>(pixels) & 0xFFFFFF) == 0x191919;
    FillRect_Hook(dc, &area, GetSysColorBrush(COLOR_WINDOW));
    bool fileAcrylic = *static_cast<DWORD*>(pixels) == 0;
    DeleteObject(navBrush);
    if (!solid || !acrylic) printf("Navigation dark-background regression: opaque=%d transparent=%d\n", solid, acrylic);
    SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc);
    g_doubleBufferDCCountForThread = 0; g_entireWindowEffectWndForThread = nullptr;
    ReleaseDC(navigation, navDC); ReleaseDC(files, fileDC); DestroyWindow(root);
    return rowOpaque && textOpaque && rowTransparent && rowSelected && classified && solid && acrylic && patSolid && patAcrylic && selectionUntouched && filesUntouched && fileAcrylic;
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
