#pragma once
#include <windows.h>
#include <string>
#include <vector>

namespace UserDataCleanup {
struct Context {std::wstring sid,local,temp;};
bool CurrentContext(Context& context);
std::wstring Encode(const Context& context,bool purge);
bool DecodeForUser(const std::wstring& data,const Context& current,bool& purge);
bool OwnCredential(const std::wstring& target);
bool RemoveOwnedDirectory(const std::wstring& parent,const std::wstring& leaf,std::vector<std::wstring>& errors);
int Ask(HWND owner);
}
