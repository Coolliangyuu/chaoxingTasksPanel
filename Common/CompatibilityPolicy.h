// Generated from compatibility-policy.json. Run Common/prepare-policy.py after editing.
#pragma once
#include <windows.h>
namespace Compatibility {
inline constexpr DWORD MinimumMajor=10;
inline constexpr DWORD MinimumBuild=19045;
inline constexpr const wchar_t* Message=L"此版本需要 Windows 10 22H2（内部版本 19045）或更新的 64 位 Windows，包含 Windows 11。\n\n当前系统不满足要求，无法安装或运行此版本。请升级 Windows 后重试。";
inline constexpr bool Allows(DWORD major,DWORD build,bool is64=true) {
    return is64 && (major>MinimumMajor || (major==MinimumMajor && build>=MinimumBuild));
}
inline bool CurrentSupported() {
    using KernelVersion=VOID(WINAPI*)(DWORD*,DWORD*,DWORD*);
    auto query=reinterpret_cast<KernelVersion>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"RtlGetNtVersionNumbers"));
    DWORD major=0,minor=0,build=0;if(query)query(&major,&minor,&build);
    SYSTEM_INFO info{};GetNativeSystemInfo(&info);
    bool is64=info.wProcessorArchitecture==PROCESSOR_ARCHITECTURE_AMD64 || info.wProcessorArchitecture==PROCESSOR_ARCHITECTURE_ARM64;
    return query && Allows(major,build&0x0fffffff,is64);
}
}
