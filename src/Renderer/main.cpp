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

void RuntimeLog(wchar_t const* message)
{
    wchar_t localAppData[MAX_PATH] = {};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, ARRAYSIZE(localAppData))) return;
    std::wstring directory = std::wstring(localAppData) + L"\\Piaoklxt-MusicWallpaper";
    CreateDirectoryW(directory.c_str(), nullptr);
    std::wstring path = directory + L"\\renderer.log";
    wil::unique_hfile file(CreateFileW(path.c_str(), FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!file) return;
    SYSTEMTIME time = {};
    GetLocalTime(&time);
    wchar_t line[1024] = {};
    swprintf_s(line, ARRAYSIZE(line),
        L"%04u-%02u-%02u %02u:%02u:%02u.%03u pid=%lu %s\r\n",
        time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute,
        time.wSecond, time.wMilliseconds, GetCurrentProcessId(), message);
    DWORD written = 0;
    WriteFile(file.get(), line, static_cast<DWORD>(wcslen(line) * sizeof(wchar_t)), &written, nullptr);
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
    RuntimeLog(L"renderer entry");
    // Initialize COM
    winrt::init_apartment(winrt::apartment_type::single_threaded);

    // Check to see that capture is supported
    auto isCaptureSupported = winrt::Windows::Graphics::Capture::GraphicsCaptureSession::IsSupported();
    if (!isCaptureSupported)
    {
        RuntimeLog(L"exit=1 graphics capture unsupported");
        return 1;
    }

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

    wchar_t targetProcessName[MAX_PATH] = L"QQMusic.exe";
    GetEnvironmentVariableW(L"QQMUSIC_WALLPAPER_TEST_PROCESS", targetProcessName, ARRAYSIZE(targetProcessName));
    HWND qqMusic = nullptr;
    wchar_t targetHwndText[32] = {};
    if (GetEnvironmentVariableW(L"QQMUSIC_WALLPAPER_TARGET_HWND", targetHwndText,
        ARRAYSIZE(targetHwndText)) > 0)
    {
        auto value = _wcstoui64(targetHwndText, nullptr, 10);
        auto exactWindow = reinterpret_cast<HWND>(static_cast<ULONG_PTR>(value));
        if (IsWindow(exactWindow)) qqMusic = exactWindow;
    }
    if (!qqMusic) qqMusic = FindTargetWindow(targetProcessName);
    if (!qqMusic)
    {
        RuntimeLog(L"exit=2 QQ Music window not found");
        return 2;
    }
    if (_wcsicmp(targetProcessName, L"QQMusic.exe") == 0) EnableCaptureRedirection(qqMusic);
    wil::unique_handle firstFrame(CreateEventW(nullptr, TRUE, FALSE,
        L"Local\\PiaoklxtMusicWallpaperFirstFrame"));
    if (firstFrame) ResetEvent(firstFrame.get());
    auto item = app->TryStartCaptureFromWindowHandle(qqMusic);
    if (!item)
    {
        RuntimeLog(L"exit=3 capture item creation failed");
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
        // Keep the source redirection-capable without blocking this window's
        // message pump for three seconds.
        std::thread([qqMusic]()
        {
            for (int attempt = 0; attempt < 60 && IsWindow(qqMusic); ++attempt)
            {
                EnableCaptureRedirection(qqMusic);
                Sleep(50);
            }
        }).detach();
    }
    DWORD frameWait = firstFrame ? WaitForSingleObject(firstFrame.get(), 6000) : WAIT_FAILED;
    if (frameWait != WAIT_OBJECT_0)
    {
        RuntimeLog(L"exit=4 first captured frame timed out");
        return 4;
    }
    if (!window.AttachToDesktop())
    {
        RuntimeLog(L"exit=5 desktop attachment failed");
        return 5;
    }
    wil::unique_handle controllerReady(OpenEventW(EVENT_MODIFY_STATE, FALSE,
        L"Local\\PiaoklxtMusicWallpaperCaptureReady"));
    if (controllerReady)
    {
        SetEvent(controllerReady.get());
    }
    // Message pump
    MSG msg = {};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    auto result = util::ShutdownDispatcherQueueControllerAndWait(controller, static_cast<int>(msg.wParam));
    RuntimeLog(L"renderer message loop ended");
    return result;
}
