#include "pch.h"
#include "App.h"
#include "SampleWindow.h"

namespace winrt
{
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Storage::Pickers;
    using namespace Windows::UI::Composition;
}

namespace util
{
    using namespace robmikh::common::desktop;
}

HWND FindTargetWindow(wchar_t const* targetProcessName)
{
    struct State
    {
        HWND window = nullptr;
        long long area = 0;
        wchar_t const* targetProcessName = nullptr;
    } state{ nullptr, 0, targetProcessName };

    EnumWindows([](HWND window, LPARAM value)
    {
        auto state = reinterpret_cast<State*>(value);
        wchar_t className[128] = {};
        GetClassNameW(window, className, ARRAYSIZE(className));
        if (wcscmp(className, L"TXGuiFoundation") != 0 || !IsWindowVisible(window))
        {
            return TRUE;
        }

        DWORD processId = 0;
        GetWindowThreadProcessId(window, &processId);
        wil::unique_handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId));
        if (!process)
        {
            return TRUE;
        }
        wchar_t processPath[MAX_PATH] = {};
        DWORD processPathLength = ARRAYSIZE(processPath);
        if (!QueryFullProcessImageNameW(process.get(), 0, processPath, &processPathLength))
        {
            return TRUE;
        }
        auto fileName = wcsrchr(processPath, L'\\');
        fileName = fileName ? fileName + 1 : processPath;
        if (!state->targetProcessName || _wcsicmp(fileName, state->targetProcessName) != 0)
        {
            return TRUE;
        }
        RECT rect = {};
        GetWindowRect(window, &rect);
        auto area = static_cast<long long>(rect.right - rect.left) * (rect.bottom - rect.top);
        if (area > state->area)
        {
            state->window = window;
            state->area = area;
        }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&state));

    return state.window;
}

void PrepareQQMusic(HWND window)
{
    ShowWindow(window, SW_RESTORE);
    ShowWindow(window, SW_MAXIMIZE);
    SetForegroundWindow(window);
    Sleep(400);
    RECT client = {};
    GetClientRect(window, &client);
    auto point = MAKELPARAM(300, std::max(80L, client.bottom - 52));
    PostMessageW(window, WM_LBUTTONDOWN, MK_LBUTTON, point);
    PostMessageW(window, WM_LBUTTONUP, 0, point);
    Sleep(1000);
}

void EnableCaptureRedirection(HWND window)
{
    auto exStyle = GetWindowLongPtrW(window, GWL_EXSTYLE);
    exStyle &= ~(static_cast<LONG_PTR>(WS_EX_NOREDIRECTIONBITMAP) | static_cast<LONG_PTR>(WS_EX_TOOLWINDOW));
    exStyle |= WS_EX_APPWINDOW;
    SetWindowLongPtrW(window, GWL_EXSTYLE, exStyle);
    DwmInvalidateIconicBitmaps(window);
}

int __stdcall WinMain(HINSTANCE, HINSTANCE, PSTR, int)
{
    // Initialize COM
    winrt::init_apartment(winrt::apartment_type::single_threaded);

    // Check to see that capture is supported
    auto isCaptureSupported = winrt::Windows::Graphics::Capture::GraphicsCaptureSession::IsSupported();
    if (!isCaptureSupported) return 1;

    // Create the DispatcherQueue that the compositor needs to run
    auto controller = util::CreateDispatcherQueueControllerForCurrentThread();

    // Initialize Composition
    auto compositor = winrt::Compositor();
    auto root = compositor.CreateContainerVisual();
    root.RelativeSizeAdjustment({ 1.0f, 1.0f });

    // Create the app
    auto app = std::make_shared<App>(root);

    RECT workArea = {};
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &workArea, 0);
    auto width = workArea.right - workArea.left;
    auto height = workArea.bottom - workArea.top;
    auto window = SampleWindow(width, height, app);

    // Hookup the visual tree to the window
    auto target = window.CreateWindowTarget(compositor);
    target.Root(root);
    window.AttachToDesktop();

    wchar_t targetProcessName[MAX_PATH] = L"QQMusic.exe";
    GetEnvironmentVariableW(L"QQMUSIC_WALLPAPER_TEST_PROCESS", targetProcessName, ARRAYSIZE(targetProcessName));
    auto qqMusic = FindTargetWindow(targetProcessName);
    if (!qqMusic)
    {
        return 2;
    }
    if (_wcsicmp(targetProcessName, L"QQMusic.exe") == 0) EnableCaptureRedirection(qqMusic);
    auto item = app->TryStartCaptureFromWindowHandle(qqMusic);
    if (!item)
    {
        return 3;
    }
    app->IsCursorEnabled(false);
    app->IsBorderRequired(false);
    // Include QQ Music's comments, menus and other secondary windows in the
    // captured main-window texture when the current Windows build supports it.
    if (winrt::Windows::Foundation::Metadata::ApiInformation::IsPropertyPresent(
        winrt::name_of<winrt::GraphicsCaptureSession>(), L"IncludeSecondaryWindows"))
    {
        app->IncludeSecondaryWindows(true);
    }
    app->MinUpdateInterval(winrt::Windows::Foundation::TimeSpan{ std::chrono::milliseconds(33) });
    if (_wcsicmp(targetProcessName, L"QQMusic.exe") == 0)
    {
        // QQ Music periodically restores WS_EX_NOREDIRECTIONBITMAP. Keep it
        // redirection-capable long enough for DWM/WGC to establish the stream.
        for (int attempt = 0; attempt < 60; ++attempt)
        {
            EnableCaptureRedirection(qqMusic);
            Sleep(50);
        }
    }
    // QQ Music adds a large invisible non-client margin. Move the source well
    // beyond the primary monitor so that margin cannot cover desktop icons.
    SetWindowPos(qqMusic, HWND_BOTTOM, workArea.left - width - 512, workArea.top, width, height,
        SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);

    // Message pump
    MSG msg = {};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return util::ShutdownDispatcherQueueControllerAndWait(controller, static_cast<int>(msg.wParam));
}
