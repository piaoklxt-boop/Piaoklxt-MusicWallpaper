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
    PublishOpaqueHeaderColor({255, 98, 132, 188});
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
    bool solid = *static_cast<DWORD*>(pixels) == 0xFF6284BC;
    ExtTextOutW_Original = ExtTextOutW;
    SetBkColor(dc, RGB(25,25,25));
    // Common controls clear item rows with ETO_OPAQUE and zero characters.
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, nullptr, 0, nullptr);
    bool rowOpaque = *static_cast<DWORD*>(pixels) == 0xFF6284BC;
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
    g_settings.fileAreaAcrylic = false;
    FillRect_Hook(dc, &area, navBrush);
    bool fileOpaque = *static_cast<DWORD*>(pixels) == 0xFF191919;
    oldBrush = SelectObject(dc, navBrush);
    PatBlt_Hook(dc, 0,0,8,8, PATCOPY);
    bool filePatOpaque = *static_cast<DWORD*>(pixels) == 0xFF191919;
    SelectObject(dc, oldBrush);
    SetBkColor(dc, RGB(80,90,100));
    ExtTextOutW_Hook(dc, 0,0, ETO_OPAQUE, &area, nullptr, 0, nullptr);
    bool fileRowOpaque = *static_cast<DWORD*>(pixels) == 0xFF505A64;
    g_doubleBufferDCsForThread[0].wnd.hWnd = tree;
    g_settings.sidebarAcrylic = true;
    FillRect_Hook(dc, &area, navBrush);
    bool fileSwitchLeavesSidebar = *static_cast<DWORD*>(pixels) == 0;
    g_settings.fileAreaAcrylic = true;
    DeleteObject(navBrush);
    if (!solid || !acrylic) printf("Navigation dark-background regression: opaque=%d transparent=%d\n", solid, acrylic);
    SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc);
    g_doubleBufferDCCountForThread = 0; g_entireWindowEffectWndForThread = nullptr;
    ReleaseDC(navigation, navDC); ReleaseDC(files, fileDC); DestroyWindow(root);
    return rowOpaque && textOpaque && rowTransparent && rowSelected && classified && solid && acrylic && patSolid && patAcrylic && selectionUntouched && filesUntouched && fileAcrylic && fileOpaque && filePatOpaque && fileRowOpaque && fileSwitchLeavesSidebar;
}

// The production unadvise barrier must outlive a queued callback on a
// message-only UI window, which EnumThreadWindows cannot enumerate.
HANDLE callbackEntered;
std::atomic<bool> callbackExited = false;
LRESULT CALLBACK CallbackFixtureProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_APP) {
        SetEvent(callbackEntered);
        Sleep(150);
        callbackExited = true;
        return 0;
    }
    if (m == WM_CLOSE) { DestroyWindow(h); PostQuitMessage(0); return 0; }
    return DefWindowProcW(h, m, w, l);
}
bool TestCallbackDrain() {
    callbackEntered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    DWORD threadId = 0;
    HANDLE thread = CreateThread(nullptr, 0, [](void*) -> DWORD {
        WNDCLASSW wc{}; wc.lpfnWndProc = CallbackFixtureProc;
        wc.lpszClassName = L"MusicWallpaper.CallbackFixture";
        wc.hInstance = GetModuleHandleW(nullptr); RegisterClassW(&wc);
        HWND window = CreateWindowW(wc.lpszClassName, L"", 0, 0,0,0,0, HWND_MESSAGE,nullptr,wc.hInstance,nullptr);
        if (!window) return 1;
        PostMessageW(window, WM_APP, 0, 0);
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) DispatchMessageW(&message);
        return 0;
    }, nullptr, 0, &threadId);
    bool entered = WaitForSingleObject(callbackEntered, 3000) == WAIT_OBJECT_0;
    if (entered) WaitForVisualTreeCallbackThreads({threadId});
    bool drained = callbackExited.load();
    HWND window = FindThreadWindow(threadId);
    if (window) PostMessageW(window, WM_CLOSE, 0, 0);
    bool stopped = WaitForSingleObject(thread, 3000) == WAIT_OBJECT_0;
    CloseHandle(thread); CloseHandle(callbackEntered);
    return entered && drained && stopped;
}

int observedAccent = -1, accentWrites = 0;
unsigned observedColor;
SetExplorerComposition_t forwardComposition = nullptr;
bool compositionAccepted = true;
BOOL WINAPI ObserveAccent(HWND window, ExplorerCompositionData* data) {
    auto policy = static_cast<ExplorerAccentPolicy*>(data->data);
    observedAccent = policy->state;
    observedColor = static_cast<unsigned>(policy->color);
    accentWrites++;
    BOOL accepted = forwardComposition ? forwardComposition(window, data) : TRUE;
    compositionAccepted &= accepted != FALSE;
    return accepted;
}
bool TestDragBackdrop() {
    HWND window = CreateWindowW(L"STATIC",L"",WS_OVERLAPPEDWINDOW,-18000,-18000,400,300,nullptr,nullptr,nullptr,nullptr);
    HWND other = CreateWindowW(L"STATIC",L"",WS_OVERLAPPEDWINDOW,-18000,-18000,400,300,nullptr,nullptr,nullptr,nullptr);
    if (!window || !other) return false;
    auto original = SetExplorerComposition; SetExplorerComposition = ObserveAccent;
    DwmSetWindowAttribute_Original = DwmSetWindowAttribute;
    auto saved = g_settings;
    g_settings.acrylicGray = 33; g_settings.acrylicOpacity = 25;
    SetAccentBlurBehind(window,true);
    bool idle = observedAccent == 4 && observedColor == 0x19212121;
    SendMessageW(window,WM_ENTERSIZEMOVE,0,0);
    bool moving = observedAccent == 2 && observedColor == 0xFF212121;
    int before = accentWrites;
    SendMessageW(window,WM_ENTERSIZEMOVE,0,0);
    RECT rectangle{0,0,400,300};
    for(int i=0;i<200;i++) {
        SendMessageW(window,WM_MOVING,0,reinterpret_cast<LPARAM>(&rectangle));
        SendMessageW(window,WM_SIZING,WMSZ_RIGHT,reinterpret_cast<LPARAM>(&rectangle));
    }
    bool stable = accentWrites == before;
    SetAccentBlurBehind(other,true);
    bool isolated = observedAccent == 4;
    SendMessageW(window,WM_EXITSIZEMOVE,0,0);
    bool restored = observedAccent == 4 && observedColor == 0x19212121;
    before = accentWrites;
    SendMessageW(window,WM_EXITSIZEMOVE,0,0);
    restored &= accentWrites == before;
    SendMessageW(window,WM_ENTERSIZEMOVE,0,0);
    g_settings.acrylicGray = 80; g_settings.acrylicOpacity = 100;
    SetAccentBlurBehind(window,true);
    bool changed = observedAccent == 2 && observedColor == 0xFF505050;
    SendMessageW(window,WM_EXITSIZEMOVE,0,0);
    changed &= observedAccent == 4 && observedColor == 0x64505050;
    SendMessageW(window,WM_ENTERSIZEMOVE,0,0);
    SetAccentBlurBehind(window,false);
    before = accentWrites;
    SendMessageW(window,WM_EXITSIZEMOVE,0,0);
    bool removed = !GetPropW(window,kExplorerDragState) && accentWrites == before;
    SetAccentBlurBehind(window,true);
    bool reenabled = observedAccent == 4;
    SendMessageW(window,WM_ENTERSIZEMOVE,0,0);
    before = accentWrites;
    DestroyWindow(window);
    bool destroyed = accentWrites == before;
    SetAccentBlurBehind(other,false); DestroyWindow(other);
    g_settings = saved; SetExplorerComposition = original;
    printf("Drag backdrop: idle=%d movingSolid=%d noPerMoveWrites=%d restored=%d isolated=%d settings=%d disabled=%d reenabled=%d destroyed=%d\n",
        idle,moving,stable,restored,isolated,changed,removed,reenabled,destroyed);
    return idle && moving && stable && restored && isolated && changed && removed && reenabled && destroyed;
}

// Exercise the actual compositor API on an owned offscreen window. WCA 19
// cannot be queried back on every Windows build, so check native acceptance
// while observing the production requests. This does not measure Explorer FPS.
bool TestCompositorDragState() {
    HWND window = CreateWindowW(L"STATIC", L"", WS_OVERLAPPEDWINDOW,
        -18000,-18000,400,300,nullptr,nullptr,nullptr,nullptr);
    if (!window || !SetExplorerComposition) return false;
    auto original = SetExplorerComposition;
    forwardComposition = original; compositionAccepted = true;
    SetExplorerComposition = ObserveAccent;
    SetAccentBlurBehind(window, true);
    int idle = observedAccent;
    SendMessageW(window, WM_ENTERSIZEMOVE, 0, 0);
    int moving = observedAccent;
    SendMessageW(window, WM_EXITSIZEMOVE, 0, 0);
    int restored = observedAccent;
    SetAccentBlurBehind(window, false);
    DestroyWindow(window);
    SetExplorerComposition = original; forwardComposition = nullptr;
    printf("Native compositor accepted=%d; requested idle=%d moving=%d restored=%d\n",
        compositionAccepted, idle, moving, restored);
    return compositionAccepted && idle == 4 && moving == 2 && restored == 4;
}

int main() {
    if (!TestDragBackdrop()) return 9;
    if (!TestCompositorDragState()) return 10;
    if (!TestCallbackDrain()) return 8;
    puts("Unadvised callback barrier drains an owned message-only UI thread before release. PASS");
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
