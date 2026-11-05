import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
policy = json.loads((root / "compatibility-policy.json").read_text(encoding="utf-8-sig"))
message = json.dumps(policy["message"], ensure_ascii=False)
header = f'''// Generated from compatibility-policy.json. Run Common/prepare-policy.py after editing.
#pragma once
#include <windows.h>
namespace Compatibility {{
inline constexpr DWORD MinimumMajor={int(policy["minimumMajor"])};
inline constexpr DWORD MinimumBuild={int(policy["minimumBuild"])};
inline constexpr const wchar_t* Message=L{message};
inline constexpr bool Allows(DWORD major,DWORD build,bool is64=true) {{
    return is64 && (major>MinimumMajor || (major==MinimumMajor && build>=MinimumBuild));
}}
inline bool CurrentSupported() {{
    using KernelVersion=VOID(WINAPI*)(DWORD*,DWORD*,DWORD*);
    auto query=reinterpret_cast<KernelVersion>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"RtlGetNtVersionNumbers"));
    DWORD major=0,minor=0,build=0;if(query)query(&major,&minor,&build);
    SYSTEM_INFO info{{}};GetNativeSystemInfo(&info);
    bool is64=info.wProcessorArchitecture==PROCESSOR_ARCHITECTURE_AMD64 || info.wProcessorArchitecture==PROCESSOR_ARCHITECTURE_ARM64;
    return query && Allows(major,build&0x0fffffff,is64);
}}
}}
'''
(root / "Common" / "CompatibilityPolicy.h").write_text(header, encoding="utf-8")
