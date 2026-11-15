#include "../../third_party/ExplorerThemeRuntime/licenses/folder-size-cache.hpp"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace MusicWallpaperFolderSizes;
void Check(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int main() {
    try {
        WCHAR temp[MAX_PATH];GetTempPathW(MAX_PATH,temp);
        auto root=std::filesystem::path(temp)/(L"MusicWallpaper-folder-tests-"+std::to_wstring(GetCurrentProcessId()));
        std::filesystem::create_directories(root/L"child");std::filesystem::create_directory(root/L"empty");
        std::ofstream(root/L"a.bin",std::ios::binary)<<std::string(101,'a');
        std::ofstream(root/L"child"/L"b.bin",std::ios::binary)<<std::string(203,'b');
        Check(Calculate(root.wstring(),[]{return false;})==304,"Nested size mismatch");
        Check(Calculate((root/L"empty").wstring(),[]{return false;})==0,"Empty folder is not zero");
        Check(!Calculate(root.wstring(),[]{return true;}),"Cancellation returned partial size");
        Check(!Calculate((root/L"missing").wstring(),[]{return false;}),"Missing folder became zero");
        Check(!LocalPath(L"\\\\server\\share") && !LocalPath(L"shell:Downloads"),"Remote/shell path accepted");
        std::atomic<int> notifications{0},calls{0};std::atomic<bool> entered{false},release{false};
        Cache cache([&](const std::wstring&){notifications++;},[&](const std::wstring&,const std::function<bool()>& cancelled)->std::optional<Size>{
            ++calls;entered=true;while(!release && !cancelled())Sleep(1);if(cancelled())return {};return 304;
        });cache.Start();
        auto start=Clock::now();Check(!cache.Get(root.wstring()),"Uncomputed size returned");
        while(!entered && Clock::now()-start<std::chrono::seconds(2))Sleep(1);
        Check(entered,"Worker did not start");
        start=Clock::now();for(int i=0;i<1000;i++)Check(!cache.Get(root.wstring()),"Pending value is not unknown");
        Check(Clock::now()-start<std::chrono::milliseconds(100),"Property lookup waited for enumeration");
        release=true;start=Clock::now();while(notifications==0 && Clock::now()-start<std::chrono::seconds(2))Sleep(1);
        Check(cache.Get(root.wstring())==304 && calls==1 && notifications==1,"Cache or notification duplicates");
        cache.SetEnabled(false);Check(!cache.Get(root.wstring()),"Disabled cache returned size");
        cache.SetEnabled(true);release=false;entered=false;cache.Get(root.wstring());
        start=Clock::now();while(!entered && Clock::now()-start<std::chrono::seconds(2))Sleep(1);
        cache.SetEnabled(false);cache.Stop();Check(notifications==1,"Cancelled request was published");
        Check(std::filesystem::weakly_canonical(root).parent_path()==std::filesystem::weakly_canonical(temp),"Cleanup escaped its temporary fixture parent");
        std::filesystem::remove_all(root);
        std::cout<<"PASS folder sizes: nested/empty/unknown/cancellation, remote exclusion, nonblocking lookup, deduplication, enable/disable and worker shutdown\n";
        return 0;
    }catch(const std::exception& e){std::cerr<<"FAIL "<<e.what()<<"\n";return 1;}
}
