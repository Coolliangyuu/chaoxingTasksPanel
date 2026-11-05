#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define _WIN32_WINNT 0x0601
#include "UserDataCleanup.h"
#include <shlobj.h>
#include <shobjidl.h>
#include <propsys.h>
#include <sddl.h>
#include <wincred.h>
#include <msi.h>
#include <msiquery.h>
#include <roapi.h>
#include <winstring.h>
#include <inspectable.h>
#include <algorithm>

namespace UserDataCleanup {
namespace {
const wchar_t* AppId=L"CoolLiangYu.chaoxingTaskPanel";
const wchar_t* CredentialPrefix=L"ChaoxingStudyTasks/AutoLogin/";
// The two stable WinRT ABI interfaces needed for per-app history removal.
// Keeping these declarations local avoids unrelated MinGW generated generic
// IReference<boolean>/IReference<BYTE> collisions in windows.foundation.h.
struct ToastHistory : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE RemoveGroup(HSTRING)=0;
    virtual HRESULT STDMETHODCALLTYPE RemoveGroupWithId(HSTRING,HSTRING)=0;
    virtual HRESULT STDMETHODCALLTYPE RemoveGroupedTagWithId(HSTRING,HSTRING,HSTRING)=0;
    virtual HRESULT STDMETHODCALLTYPE RemoveGroupedTag(HSTRING,HSTRING)=0;
    virtual HRESULT STDMETHODCALLTYPE Remove(HSTRING)=0;
    virtual HRESULT STDMETHODCALLTYPE Clear()=0;
    virtual HRESULT STDMETHODCALLTYPE ClearWithId(HSTRING)=0;
};
struct ToastManager2 : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_History(ToastHistory**)=0;
};
const IID ToastManager2Id={0x7ab93c52,0x0e48,0x4750,{0xba,0x9d,0x1a,0x41,0x13,0x98,0x18,0x47}};
std::wstring Full(const std::wstring& path) {
    if(path.empty())return L"";
    DWORD n=GetFullPathNameW(path.c_str(),0,nullptr,nullptr);
    if(!n)return L"";std::wstring result(n,L'\0');
    DWORD length=GetFullPathNameW(path.c_str(),n,result.data(),nullptr);
    if(!length||length>=n)return L"";result.resize(length);
    while(result.size()>3&&result.back()==L'\\')result.pop_back();return result;
}
bool Same(const std::wstring& a,const std::wstring& b) {
    return CompareStringOrdinal(a.c_str(),-1,b.c_str(),-1,TRUE)==CSTR_EQUAL;
}
std::wstring Native(const std::wstring& path) {
    if(path.rfind(L"\\\\?\\",0)==0)return path;
    return path.rfind(L"\\\\",0)==0?L"\\\\?\\UNC\\"+path.substr(2):L"\\\\?\\"+path;
}
std::wstring Folder(REFKNOWNFOLDERID id,HANDLE token) {
    PWSTR text=nullptr;std::wstring result;
    if(SUCCEEDED(SHGetKnownFolderPath(id,0,token,&text)))result=text;
    CoTaskMemFree(text);return Full(result);
}
HANDLE UserToken() {
    HANDLE token=nullptr;
    if(!OpenThreadToken(GetCurrentThread(),TOKEN_QUERY|TOKEN_IMPERSONATE|TOKEN_DUPLICATE,TRUE,&token)) {
        if(GetLastError()!=ERROR_NO_TOKEN)return nullptr;
        if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY|TOKEN_DUPLICATE,&token))return nullptr;
    }return token;
}
bool RemoveNode(const std::wstring& path,std::vector<std::wstring>& errors) {
    const auto native=Native(path);DWORD attributes=GetFileAttributesW(native.c_str());
    if(attributes==INVALID_FILE_ATTRIBUTES) {
        DWORD error=GetLastError();if(error==ERROR_FILE_NOT_FOUND||error==ERROR_PATH_NOT_FOUND)return true;
        errors.push_back(path);return false;
    }
    if(!(attributes&FILE_ATTRIBUTE_DIRECTORY)) {
        // Never change a link's target attributes. Deleting a link only unlinks it.
        if(!(attributes&FILE_ATTRIBUTE_REPARSE_POINT)&&(attributes&FILE_ATTRIBUTE_READONLY)) {
            HANDLE file=CreateFileW(native.c_str(),FILE_READ_ATTRIBUTES|FILE_WRITE_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
            BY_HANDLE_FILE_INFORMATION info{};
            if(file!=INVALID_HANDLE_VALUE){
                FILE_BASIC_INFO basic{};
                if(GetFileInformationByHandle(file,&info)&&info.nNumberOfLinks==1&&!(info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)&&
                    GetFileInformationByHandleEx(file,FileBasicInfo,&basic,sizeof(basic))) {
                    basic.FileAttributes&=~FILE_ATTRIBUTE_READONLY;
                    if(!basic.FileAttributes)basic.FileAttributes=FILE_ATTRIBUTE_NORMAL;
                    SetFileInformationByHandle(file,FileBasicInfo,&basic,sizeof(basic));
                }CloseHandle(file);
            }
        }
        if(DeleteFileW(native.c_str()))return true;errors.push_back(path);return false;
    }
    // Lock each directory against rename/reparse changes while traversing it.
    HANDLE lock=CreateFileW(native.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ,nullptr,OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
    if(lock==INVALID_HANDLE_VALUE){errors.push_back(path);return false;}
    BY_HANDLE_FILE_INFORMATION info{};
    if(!GetFileInformationByHandle(lock,&info)){CloseHandle(lock);errors.push_back(path);return false;}
    if(!(info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)) {
        WIN32_FIND_DATAW item{};HANDLE search=FindFirstFileW((native+L"\\*").c_str(),&item);
        if(search!=INVALID_HANDLE_VALUE) {
            do {if(wcscmp(item.cFileName,L".")&&wcscmp(item.cFileName,L".."))RemoveNode(path+L"\\"+item.cFileName,errors);}
            while(FindNextFileW(search,&item));FindClose(search);
        }else if(GetLastError()!=ERROR_FILE_NOT_FOUND){CloseHandle(lock);errors.push_back(path);return false;}
    }
    CloseHandle(lock);
    if(RemoveDirectoryW(native.c_str()))return true;errors.push_back(path);return false;
}
void RemoveStartup(std::vector<std::wstring>& errors) {
    HKEY user=nullptr,key=nullptr;
    if(RegOpenCurrentUser(KEY_READ|KEY_WRITE,&user)!=ERROR_SUCCESS){errors.push_back(L"开机启动设置");return;}
    LONG opened=RegOpenKeyExW(user,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,KEY_SET_VALUE,&key);
    if(opened==ERROR_SUCCESS) {
        LONG result=RegDeleteValueW(key,L"chaoxingTaskPanel");
        if(result!=ERROR_SUCCESS&&result!=ERROR_FILE_NOT_FOUND)errors.push_back(L"开机启动设置");RegCloseKey(key);
    }else if(opened!=ERROR_FILE_NOT_FOUND&&opened!=ERROR_PATH_NOT_FOUND)errors.push_back(L"开机启动设置");
    RegCloseKey(user);
}
void RemoveCredentials(std::vector<std::wstring>& errors) {
    DWORD count=0;PCREDENTIALW* credentials=nullptr;
    if(!CredEnumerateW(L"ChaoxingStudyTasks/AutoLogin/*",0,&count,&credentials)) {
        if(GetLastError()!=ERROR_NOT_FOUND)errors.push_back(L"保存的登录凭据");return;
    }
    for(DWORD i=0;i<count;i++)if(credentials[i]->Type==CRED_TYPE_GENERIC&&OwnCredential(credentials[i]->TargetName)) {
        if(!CredDeleteW(credentials[i]->TargetName,CRED_TYPE_GENERIC,0)&&GetLastError()!=ERROR_NOT_FOUND)
            errors.push_back(L"保存的登录凭据");
    }CredFree(credentials);
}
void RemoveNotificationSettings(std::vector<std::wstring>& errors) {
    HKEY user=nullptr;if(RegOpenCurrentUser(KEY_READ|KEY_WRITE,&user)!=ERROR_SUCCESS){errors.push_back(L"本应用通知设置");return;}
    LONG result=RegDeleteTreeW(user,L"Software\\Microsoft\\Windows\\CurrentVersion\\Notifications\\Settings\\CoolLiangYu.chaoxingTaskPanel");
    if(result!=ERROR_SUCCESS&&result!=ERROR_FILE_NOT_FOUND&&result!=ERROR_PATH_NOT_FOUND)errors.push_back(L"本应用通知设置");
    RegCloseKey(user);
}
void ClearNotificationHistory(std::vector<std::wstring>& errors) {
    HRESULT initialized=RoInitialize(RO_INIT_MULTITHREADED);
    HSTRING name=nullptr,app=nullptr;
    ToastManager2* factory=nullptr;
    ToastHistory* history=nullptr;
    const wchar_t* type=L"Windows.UI.Notifications.ToastNotificationManager";
    HRESULT result=WindowsCreateString(type,static_cast<UINT32>(wcslen(type)),&name);
    if(SUCCEEDED(result))result=RoGetActivationFactory(name,ToastManager2Id,reinterpret_cast<void**>(&factory));
    if(SUCCEEDED(result))result=factory->get_History(&history);
    if(SUCCEEDED(result))result=WindowsCreateString(AppId,static_cast<UINT32>(wcslen(AppId)),&app);
    if(SUCCEEDED(result))result=history->ClearWithId(app);
    if(history)history->Release();if(factory)factory->Release();WindowsDeleteString(app);WindowsDeleteString(name);
    if(SUCCEEDED(initialized))RoUninitialize();
    if(FAILED(result)&&result!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND))errors.push_back(L"Windows 通知中心中的本应用通知");
}
bool OwnShortcut(const std::wstring& path) {
    if(GetFileAttributesW(path.c_str())==INVALID_FILE_ATTRIBUTES)return false;
    IShellLinkW* link=nullptr;IPersistFile* file=nullptr;IPropertyStore* store=nullptr;bool own=false;
    if(SUCCEEDED(CoCreateInstance(CLSID_ShellLink,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&link)))) {
        if(SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&file)))&&SUCCEEDED(file->Load(path.c_str(),STGM_READ))&&
            SUCCEEDED(link->QueryInterface(IID_PPV_ARGS(&store)))) {
            const PROPERTYKEY key={{0x9f4c2855,0x9f79,0x4b39,{0xa8,0xd0,0xe1,0xd4,0x2d,0xe1,0xd5,0xf3}},5};
            PROPVARIANT value{};
            if(SUCCEEDED(store->GetValue(key,&value))&&value.vt==VT_LPWSTR&&value.pwszVal)own=Same(value.pwszVal,AppId);
            PropVariantClear(&value);
        }
    }
    if(store)store->Release();if(file)file->Release();if(link)link->Release();return own;
}
void RemoveLegacyShortcuts(std::vector<std::wstring>& errors) {
    HANDLE token=UserToken();if(!token){errors.push_back(L"旧快捷方式");return;}
    auto programs=Folder(FOLDERID_Programs,token),desktop=Folder(FOLDERID_Desktop,token);CloseHandle(token);
    HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    for(auto name:{L"学习通作业看板",L"chaoxingTasksPanel"}) {
        std::vector<std::wstring> paths;
        if(!programs.empty())paths.push_back(programs+L"\\"+name+L"\\"+name+L".lnk");
        if(!desktop.empty())paths.push_back(desktop+L"\\"+name+L".lnk");
        for(auto path:paths)
            if(OwnShortcut(path)&&!DeleteFileW(path.c_str()))errors.push_back(L"旧快捷方式");
        if(!programs.empty())RemoveDirectoryW((programs+L"\\"+name).c_str()); // Empty only; never recursively remove shortcut folders.
    }if(SUCCEEDED(initialized))CoUninitialize();
}
}

bool CurrentContext(Context& context) {
    context={};
    HANDLE token=UserToken();if(!token)return false;
    DWORD bytes=0;GetTokenInformation(token,TokenUser,nullptr,0,&bytes);std::vector<BYTE> buffer(bytes);
    bool ok=bytes&&GetTokenInformation(token,TokenUser,buffer.data(),bytes,&bytes);LPWSTR sid=nullptr;
    if(ok&&ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid,&sid))context.sid=sid;
    LocalFree(sid);context.local=Folder(FOLDERID_LocalAppData,token);CloseHandle(token);
    context.temp=context.local+L"\\Temp";
    return !context.sid.empty()&&context.local.size()>3;
}
std::wstring Encode(const Context& context,bool purge) {
    return (purge?L"1|":L"0|")+context.sid+L"|"+context.local;
}
bool DecodeForUser(const std::wstring& data,const Context& current,bool& purge) {
    if(current.sid.empty()||Full(current.local).size()<=3)return false;
    if(data.size()<4||data[1]!=L'|'||(data[0]!=L'0'&&data[0]!=L'1'))return false;
    auto split=data.find(L'|',2);if(split==std::wstring::npos||data.find(L'|',split+1)!=std::wstring::npos)return false;
    if(data.substr(2,split-2)!=current.sid||!Same(Full(data.substr(split+1)),Full(current.local)))return false;
    purge=data[0]==L'1';return true;
}
bool OwnCredential(const std::wstring& target) {
    size_t length=wcslen(CredentialPrefix);
    return target.size()>length&&CompareStringOrdinal(target.c_str(),static_cast<int>(length),CredentialPrefix,static_cast<int>(length),TRUE)==CSTR_EQUAL;
}
bool RemoveOwnedDirectory(const std::wstring& parent,const std::wstring& leaf,std::vector<std::wstring>& errors) {
    if(leaf!=L"ChaoxingStudyTasks"&&leaf!=L"chaoxingTaskPanel"&&leaf!=L"chaoxingTaskPanel-portable")return false;
    auto base=Full(parent);if(base.size()<=3)return false;
    DWORD attributes=GetFileAttributesW(Native(base).c_str());
    if(attributes==INVALID_FILE_ATTRIBUTES)return GetLastError()==ERROR_FILE_NOT_FOUND||GetLastError()==ERROR_PATH_NOT_FOUND;
    if(!(attributes&FILE_ATTRIBUTE_DIRECTORY)||(attributes&FILE_ATTRIBUTE_REPARSE_POINT)){errors.push_back(base);return false;}
    HANDLE lock=CreateFileW(Native(base).c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ,nullptr,OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
    if(lock==INVALID_HANDLE_VALUE){errors.push_back(base);return false;}
    BY_HANDLE_FILE_INFORMATION info{};
    if(!GetFileInformationByHandle(lock,&info)||!(info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)||
        (info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)){CloseHandle(lock);errors.push_back(base);return false;}
    bool result=RemoveNode(base+L"\\"+leaf,errors);CloseHandle(lock);return result;
}
int Ask(HWND owner) {
    return MessageBoxW(owner,L"是否同时删除当前 Windows 用户下本软件的所有用户数据？\n\n包括：账号与登录会话、保存的登录凭据、设置、提醒记录、本地缓存和本应用通知记录。删除后无法恢复。\n\n“是”：卸载成功后删除这些数据。\n“否”：仅卸载程序，保留数据以便以后重装。\n“取消”：停止卸载。\n\n您另行下载或保存到其他位置的作业附件不会删除。",
        L"卸载学习通作业看板 · 用户数据",MB_YESNOCANCEL|MB_ICONQUESTION|MB_DEFBUTTON2|MB_SETFOREGROUND);
}

std::vector<std::wstring> Clean(const Context& context,bool purge) {
    std::vector<std::wstring> errors;RemoveStartup(errors);
    if(!purge)return errors;
    RemoveCredentials(errors);ClearNotificationHistory(errors);RemoveNotificationSettings(errors);RemoveLegacyShortcuts(errors);
    RemoveOwnedDirectory(context.local,L"ChaoxingStudyTasks",errors);
    // Hold LocalAppData and Temp against redirects while inspecting the bundle cache.
    // A junction used as the owned leaf is unlinked; a redirected parent is refused.
    std::vector<HANDLE> locks;
    bool safe=true;
    for(auto path:{context.local,context.temp}) {
        HANDLE lock=CreateFileW(Native(path).c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ,nullptr,OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
        BY_HANDLE_FILE_INFORMATION info{};
        if(lock==INVALID_HANDLE_VALUE) {
            DWORD code=GetLastError();
            if(code!=ERROR_FILE_NOT_FOUND&&code!=ERROR_PATH_NOT_FOUND)errors.push_back(L"本应用运行缓存");
            safe=false;break;
        }
        locks.push_back(lock);
        if(!GetFileInformationByHandle(lock,&info)||(info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)) {
            errors.push_back(L"本应用运行缓存的父目录被重定向，已跳过");safe=false;break;
        }
    }
    if(safe) {
        auto cache=context.temp+L"\\.net";
        RemoveOwnedDirectory(cache,L"chaoxingTaskPanel",errors);RemoveOwnedDirectory(cache,L"chaoxingTaskPanel-portable",errors);
    }
    for(auto lock:locks)CloseHandle(lock);
    return errors;
}
}

namespace {
std::wstring Property(MSIHANDLE session,const wchar_t* name) {
    DWORD length=0;wchar_t empty=0;MsiGetPropertyW(session,name,&empty,&length);
    std::wstring result(length+1,L'\0');DWORD count=length+1;
    if(MsiGetPropertyW(session,name,result.data(),&count)!=ERROR_SUCCESS)return L"";result.resize(count);return result;
}
bool Removing(MSIHANDLE session) {return Property(session,L"REMOVE")==L"ALL"&&Property(session,L"UPGRADINGPRODUCTCODE").empty();}
}
extern "C" __declspec(dllexport) UINT WINAPI PromptUninstallData(MSIHANDLE session) {
    if(!Removing(session)||!Property(session,L"DATACHOICE_MADE").empty())return ERROR_SUCCESS;
    int level=_wtoi(Property(session,L"UILevel").c_str());
    if(level<INSTALLUILEVEL_BASIC)return ERROR_SUCCESS; // Silent removal preserves data unless explicitly requested.
    MSIHANDLE record=MsiCreateRecord(0);
    MsiRecordSetStringW(record,0,L"是否同时删除当前 Windows 用户下学习通作业看板的所有账号、登录会话、保存的凭据、设置、提醒记录和缓存？删除后无法恢复。选择“否”保留数据，选择“取消”停止卸载。");
    int result=MsiProcessMessage(session,static_cast<INSTALLMESSAGE>(INSTALLMESSAGE_USER|MB_YESNOCANCEL|MB_ICONQUESTION|MB_DEFBUTTON2),record);
    MsiCloseHandle(record);if(result==IDCANCEL)return ERROR_INSTALL_USEREXIT;
    MsiSetPropertyW(session,L"DELETEUSERDATA",result==IDYES?L"1":L"0");
    return MsiSetPropertyW(session,L"DATACHOICE_MADE",L"1");
}
extern "C" __declspec(dllexport) UINT WINAPI PrepareUninstallCleanup(MSIHANDLE session) {
    if(!Removing(session))return ERROR_SUCCESS;
    UserDataCleanup::Context context;
    if(!UserDataCleanup::CurrentContext(context))return ERROR_INSTALL_FAILURE;
    auto sid=Property(session,L"UserSID");if(!sid.empty()&&sid!=context.sid)return ERROR_INSTALL_FAILURE;
    auto data=UserDataCleanup::Encode(context,Property(session,L"DELETEUSERDATA")==L"1");
    return MsiSetPropertyW(session,L"CleanupStartup",data.c_str());
}
extern "C" __declspec(dllexport) UINT WINAPI CommitUninstallCleanup(MSIHANDLE session) {
    UserDataCleanup::Context context;bool purge=false;
    std::vector<std::wstring> errors;
    if(!UserDataCleanup::CurrentContext(context)||!UserDataCleanup::DecodeForUser(Property(session,L"CustomActionData"),context,purge))
        errors.push_back(L"无法核实卸载用户的数据目录，未执行清理。");
    else errors=UserDataCleanup::Clean(context,purge);
    if(!errors.empty()) {
        std::wstring message=L"程序已卸载，但部分用户数据或开机启动设置未能清理。请确认软件及网页子进程已关闭后手动检查：\n";
        for(size_t i=0;i<std::min<size_t>(errors.size(),8);i++)message+=L"\n"+errors[i];
        MSIHANDLE record=MsiCreateRecord(2);MsiRecordSetStringW(record,0,L"[2]");
        MsiRecordSetStringW(record,1,L"PANEL_CLEANUP_WARNING");MsiRecordSetStringW(record,2,message.c_str());
        MsiProcessMessage(session,INSTALLMESSAGE_WARNING,record);MsiCloseHandle(record);
    }return ERROR_SUCCESS; // The uninstall transaction is already committed.
}
