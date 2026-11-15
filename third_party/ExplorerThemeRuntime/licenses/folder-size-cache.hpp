// MusicWallpaper asynchronous folder-size adapter. GPL-3.0-or-later.
#pragma once
#include <windows.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <functional>
#include <map>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

namespace MusicWallpaperFolderSizes {
using Size = unsigned long long;
using Clock = std::chrono::steady_clock;

inline bool LocalPath(const std::wstring& path) {
    return path.size() >= 3 && ((path[0] >= L'A' && path[0] <= L'Z') ||
        (path[0] >= L'a' && path[0] <= L'z')) && path[1] == L':' && path[2] == L'\\';
}

// Enumerate metadata only, never open file contents, follow junctions, hydrate
// cloud placeholders, or traverse network/removable drives. Incomplete scans
// remain unknown instead of reporting a misleading partial byte count.
inline std::optional<Size> Calculate(const std::wstring& root, const std::function<bool()>& cancelled) {
    if (!LocalPath(root) || GetDriveTypeW(root.substr(0,3).c_str()) != DRIVE_FIXED) return {};
    auto attrs = GetFileAttributesW(root.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES || !(attrs & FILE_ATTRIBUTE_DIRECTORY) ||
        (attrs & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_OFFLINE))) return {};
    std::vector<std::wstring> pending{root}; Size bytes = 0; unsigned entries = 0;
    auto deadline = Clock::now() + std::chrono::minutes(5);
    while (!pending.empty()) {
        if (cancelled() || Clock::now() > deadline) return {};
        auto folder = std::move(pending.back()); pending.pop_back();
        WIN32_FIND_DATAW data{};
        HANDLE find = FindFirstFileExW((L"\\\\?\\" + folder + L"\\*").c_str(), FindExInfoBasic,
            &data, FindExSearchNameMatch, nullptr, FIND_FIRST_EX_LARGE_FETCH);
        if (find == INVALID_HANDLE_VALUE) {
            if (GetLastError() == ERROR_FILE_NOT_FOUND) continue;
            return {};
        }
        bool complete = true;
        do {
            if (cancelled() || Clock::now() > deadline || ++entries > 2000000) { complete = false; break; }
            if (data.cFileName[0] == L'.' && (!data.cFileName[1] ||
                (data.cFileName[1] == L'.' && !data.cFileName[2]))) continue;
            if (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                if (data.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) continue;
                if (data.dwFileAttributes & FILE_ATTRIBUTE_OFFLINE || pending.size() >= 100000) { complete=false; break; }
                pending.push_back(folder + L"\\" + data.cFileName);
            } else {
                Size length = (static_cast<Size>(data.nFileSizeHigh) << 32) | data.nFileSizeLow;
                if (~Size(0)-bytes < length) { complete=false; break; }
                bytes += length;
            }
            if ((entries & 255) == 0) Sleep(1);
        } while (FindNextFileW(find, &data));
        DWORD last = GetLastError(); FindClose(find);
        if (!complete || last != ERROR_NO_MORE_FILES) return {};
    }
    return bytes;
}

class Cache {
    struct Entry { std::optional<Size> value; Clock::time_point expires{}; bool pending=false; };
    std::mutex mutex;
    std::condition_variable changed;
    std::map<std::wstring, Entry> entries;
    std::deque<std::wstring> queue;
    std::thread worker;
    std::atomic<bool> stopping{false}, enabled{true};
    std::atomic<unsigned> epoch{0};
    std::function<void(const std::wstring&)> notify;
    std::function<std::optional<Size>(const std::wstring&,const std::function<bool()>&)> calculate;
    void Run() {
        SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
        for (;;) {
            std::wstring path; unsigned generation;
            {
                std::unique_lock lock(mutex);
                changed.wait(lock,[&]{return stopping || !queue.empty();});
                if (stopping) break;
                path=std::move(queue.front());queue.pop_front();generation=epoch;
            }
            std::optional<Size> value;
            try { value=calculate(path,[&]{return stopping || !enabled || epoch!=generation;}); }
            catch (...) { value.reset(); }
            bool publish=false;
            {
                std::lock_guard lock(mutex);
                auto found=entries.find(path);
                if (!stopping && enabled && epoch==generation && found!=entries.end()) {
                    found->second={value,Clock::now()+std::chrono::seconds(value ? 120 : 60),false};
                    publish=value.has_value();
                }
            }
            if (publish && !stopping && enabled) { try { notify(path); } catch (...) {} }
        }
        SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END);
    }
public:
    explicit Cache(std::function<void(const std::wstring&)> notify,
        decltype(calculate) calculate=Calculate) : notify(std::move(notify)),calculate(std::move(calculate)) {}
    ~Cache() { Stop(); }
    void Start() { std::lock_guard lock(mutex); if (!worker.joinable()) {stopping=false;worker=std::thread([this]{Run();});} }
    void SetEnabled(bool value) {
        std::lock_guard lock(mutex);enabled=value;
        if (!value) {++epoch;queue.clear();entries.clear();}
    }
    void Stop() {
        { std::lock_guard lock(mutex);stopping=true;++epoch;queue.clear(); }
        changed.notify_all();if (worker.joinable()) worker.join();
    }
    std::optional<Size> Get(const std::wstring& path) {
        if (!enabled || !LocalPath(path)) return {};
        std::lock_guard lock(mutex);
        if (stopping || !enabled) return {};
        auto found=entries.find(path);
        if (found!=entries.end() && (found->second.pending || Clock::now()<found->second.expires)) return found->second.value;
        if (queue.size()>=128) return {};
        if (found==entries.end() && entries.size()>=512) {
            auto victim=entries.end();
            for(auto it=entries.begin();it!=entries.end();++it)
                if(!it->second.pending && (victim==entries.end() || it->second.expires<victim->second.expires)) victim=it;
            if(victim==entries.end()) return {};
            entries.erase(victim);
        }
        auto& entry=entries[path];entry.pending=true;queue.push_back(path);changed.notify_one();
        return entry.value;
    }
};
}
