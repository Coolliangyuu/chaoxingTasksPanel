#include <windows.h>
#ifndef OS_BUILD
#define OS_BUILD 19045
#endif
#ifndef OS_MAJOR
#define OS_MAJOR 10
#endif
#ifndef RTL_STATUS
#define RTL_STATUS 0
#endif
static VOID WINAPI FakeVersion(DWORD* major,DWORD* minor,DWORD* build) {
    *major=RTL_STATUS<0?0:OS_MAJOR;*minor=0;*build=RTL_STATUS<0?0:(OS_BUILD|0xf0000000);
}
static FARPROC WINAPI FakeLookup(HMODULE,const char*) {
#ifdef MISSING_RTL
    return nullptr;
#else
    return reinterpret_cast<FARPROC>(FakeVersion);
#endif
}
static int WINAPI FakeMessage(HWND,const wchar_t* message,const wchar_t*,UINT) {
    DWORD written;
    WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),"BLOCKED_BEFORE_APP\n",19,&written,nullptr);
    return message&&*message?IDOK:IDCANCEL;
}
extern "C" {
__declspec(dllexport) decltype(&GetProcAddress) guard_getproc=FakeLookup;
__declspec(dllexport) decltype(&MessageBoxW) guard_message=FakeMessage;
}
int main() {
    if(!GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"ExitProcess"))return 99;
    DWORD written;
    WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),"APP_ENTRY\n",10,&written,nullptr);
    ExitProcess(0);
}
