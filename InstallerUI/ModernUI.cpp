#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <msi.h>
#include <msiquery.h>
#include <uxtheme.h>
#include <string>
#include <algorithm>
#include <cstdio>
#include "../Common/CompatibilityPolicy.h"
#include "UserDataCleanup.h"

namespace {
constexpr COLORREF Ink=RGB(24,40,57), Muted=RGB(100,116,133), Accent=RGB(13,113,129);
constexpr COLORREF Side=RGB(15,44,59), Pale=RGB(243,247,250), Border=RGB(218,227,234);
constexpr UINT StatusMessage=WM_APP+1, ProgressMessage=WM_APP+2, FinishMessage=WM_APP+3;
constexpr int Next=101,Back=102,Cancel=103,Browse=104,Path=105,Remove=106;
// INSTALLUILEVEL_UACONLY is 0x200 in the Windows SDK; older MinGW msi.h omits it.
constexpr DWORD UacOnly=0x200;
enum class Mode { Install,Upgrade,Repair,Uninstall };
struct UI {
    HINSTANCE module=nullptr;
    HWND window=nullptr,title=nullptr,description=nullptr,edit=nullptr,browse=nullptr;
    HWND next=nullptr,back=nullptr,cancel=nullptr,remove=nullptr,note=nullptr,status=nullptr;
    HANDLE ready=nullptr,chosen=nullptr,thread=nullptr;
    HFONT body=nullptr,heading=nullptr,small=nullptr,brand=nullptr;
    HBRUSH white=nullptr,pale=nullptr;
    HICON icon=nullptr;
    std::wstring path,version=L"1.5.0";
    Mode mode=Mode::Install;
    bool installed=false,upgrade=false,removeRequested=false;
    bool deleteData=false,dataChoiceMade=false,cleanupWarning=false,ownerSet=false;
    HWND previousOwner=nullptr;
    int page=0,percent=0;
    UINT outcome=ERROR_INSTALL_FAILURE;
    bool ended=false;
    volatile LONG canceled=0,hadError=0;
    UINT dpi=96;
} ui;

int S(int x) { return MulDiv(x,ui.dpi,96); }
std::wstring ReadProperty(MSIHANDLE session,const wchar_t* name) {
    DWORD size=0; wchar_t empty=0; MsiGetPropertyW(session,name,&empty,&size);
    std::wstring value(size+1,L'\0'); DWORD capacity=size+1;
    if(MsiGetPropertyW(session,name,value.data(),&capacity)!=ERROR_SUCCESS) return L"";
    value.resize(capacity); return value;
}
std::wstring RecordString(MSIHANDLE record,UINT field) {
    DWORD size=0; wchar_t empty=0; MsiRecordGetStringW(record,field,&empty,&size);
    std::wstring text(size+1,L'\0'); DWORD capacity=size+1;
    MsiRecordGetStringW(record,field,text.data(),&capacity); text.resize(capacity); return text;
}
std::wstring ProductInfo(const wchar_t* product,const wchar_t* property) {
    DWORD length=0;wchar_t empty=0;
    if(MsiGetProductInfoW(product,property,&empty,&length)!=ERROR_MORE_DATA)return L"";
    std::wstring value(length+1,L'\0');DWORD capacity=length+1;
    if(MsiGetProductInfoW(product,property,value.data(),&capacity)!=ERROR_SUCCESS)return L"";
    value.resize(capacity);return value;
}
int CompareVersions(const std::wstring& left,const std::wstring& right) {
    unsigned a[3]={},b[3]={};
    swscanf(left.c_str(),L"%u.%u.%u",a,a+1,a+2);swscanf(right.c_str(),L"%u.%u.%u",b,b+1,b+2);
    for(int i=0;i<3;i++)if(a[i]!=b[i])return a[i]<b[i]?-1:1;
    return 0;
}
unsigned long long CachedBuild(const std::wstring& product) {
    auto package=ProductInfo(product.c_str(),INSTALLPROPERTY_LOCALPACKAGE);
    MSIHANDLE database=0,view=0,record=0;unsigned long long result=0;
    if(!package.empty()&&MsiOpenDatabaseW(package.c_str(),MSIDBOPEN_READONLY,&database)==ERROR_SUCCESS) {
        if(MsiDatabaseOpenViewW(database,L"SELECT `Value` FROM `Property` WHERE `Property`='INSTALLER_BUILD'",&view)==ERROR_SUCCESS) {
            if(MsiViewExecute(view,0)==ERROR_SUCCESS&&MsiViewFetch(view,&record)==ERROR_SUCCESS) {
                auto value=RecordString(record,1);result=wcstoull(value.c_str(),nullptr,10);MsiCloseHandle(record);
            }MsiCloseHandle(view);
        }MsiCloseHandle(database);
    }return result;
}
void Status(const std::wstring& text) {
    auto* copy=new std::wstring(text);
    if(!ui.window || !PostMessageW(ui.window,StatusMessage,0,reinterpret_cast<LPARAM>(copy))) delete copy;
}
void Text(HDC dc,const std::wstring& text,RECT r,HFONT font,COLORREF color,UINT flags=DT_LEFT|DT_WORDBREAK) {
    auto old=SelectObject(dc,font); SetTextColor(dc,color); SetBkMode(dc,TRANSPARENT);
    DrawTextW(dc,text.c_str(),-1,&r,flags); SelectObject(dc,old);
}
void Rounded(HDC dc,RECT r,COLORREF fill,COLORREF stroke,int radius=12) {
    auto brush=CreateSolidBrush(fill); auto pen=CreatePen(PS_SOLID,1,stroke);
    auto oldBrush=SelectObject(dc,brush),oldPen=SelectObject(dc,pen);
    RoundRect(dc,r.left,r.top,r.right,r.bottom,S(radius),S(radius));
    SelectObject(dc,oldBrush); SelectObject(dc,oldPen); DeleteObject(brush); DeleteObject(pen);
}
RECT R(int x,int y,int width,int height) {return {S(x),S(y),S(x+width),S(y+height)};}
void Fonts() {
    for(auto font:{ui.body,ui.heading,ui.small,ui.brand}) if(font) DeleteObject(font);
    auto make=[](int size,int weight) {return CreateFontW(-S(size),0,0,0,weight,FALSE,FALSE,FALSE,
        DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Microsoft YaHei UI");};
    ui.body=make(14,FW_NORMAL); ui.heading=make(27,FW_BOLD); ui.small=make(12,FW_NORMAL); ui.brand=make(18,FW_BOLD);
    for(auto child:{ui.description,ui.edit,ui.note,ui.status,ui.next,ui.back,ui.cancel,ui.browse,ui.remove})
        if(child) SendMessageW(child,WM_SETFONT,reinterpret_cast<WPARAM>(ui.body),TRUE);
    if(ui.title) SendMessageW(ui.title,WM_SETFONT,reinterpret_cast<WPARAM>(ui.heading),TRUE);
}
void Move(HWND control,int x,int y,int w,int h) { MoveWindow(control,S(x),S(y),S(w),S(h),TRUE); }
std::wstring ReadEdit() {
    int n=GetWindowTextLengthW(ui.edit); std::wstring value(n+1,L'\0');
    GetWindowTextW(ui.edit,value.data(),n+1); value.resize(n); return value;
}
void SetPage(int page) {
    ui.page=page;
    bool location=page==1,summary=page==2||page==5,progress=page==3,finish=page==4;
    ShowWindow(ui.edit,location?SW_SHOW:SW_HIDE);
    ShowWindow(ui.browse,location?SW_SHOW:SW_HIDE);
    ShowWindow(ui.remove,page==5&&!ui.removeRequested?SW_SHOW:SW_HIDE);
    ShowWindow(ui.status,progress?SW_SHOW:SW_HIDE);
    ShowWindow(ui.back,(page==1||page==2)?SW_SHOW:SW_HIDE);
    ShowWindow(ui.next,progress?SW_HIDE:SW_SHOW);
    ShowWindow(ui.cancel,finish?SW_HIDE:SW_SHOW);
    ShowWindow(ui.note,location||summary?SW_SHOW:SW_HIDE);
    const wchar_t* title=L"欢迎安装";
    const wchar_t* description=L"学习通作业看板，让作业状态与截止时间一目了然。\n适用于 Windows 10 22H2 / Windows 11（64 位）。";
    const wchar_t* button=L"开始设置";
    if(ui.upgrade) {title=L"升级已安装的应用";description=L"已检测到旧版或之前的修订。升级将替换程序文件，保留账号、登录状态和个人设置。";}
    if(location) {title=L"选择安装位置";description=L"使用推荐位置，或选择你自己的文件夹。";button=L"下一步";}
    if(page==2) {title=L"准备就绪";description=L"确认下面的设置，然后开始安装。";button=L"开始安装";}
    if(page==2&&ui.upgrade) {title=L"准备升级";description=L"将沿用下方位置并替换旧程序。升级前请从托盘退出面板。";button=L"开始升级";}
    if(page==5) {title=L"管理已安装的应用";description=L"修复程序文件，或卸载学习通作业看板。卸载时可选择是否删除用户数据。";
        button=ui.removeRequested?L"卸载应用":L"修复安装";}
    if(progress) {title=ui.mode==Mode::Uninstall?L"正在卸载":ui.mode==Mode::Repair?L"正在修复":ui.mode==Mode::Upgrade?L"正在升级":L"正在安装";
        description=L"请稍候，Windows 正在完成所选操作。";}
    if(finish) {
        bool success=ui.outcome==ERROR_SUCCESS||ui.outcome==ERROR_SUCCESS_REBOOT_REQUIRED;
        title=success?L"已完成":(ui.outcome==ERROR_INSTALL_USEREXIT?L"操作已取消":L"操作未能完成");
        description=success?(ui.mode==Mode::Uninstall?(ui.cleanupWarning?L"应用已卸载。部分数据未能清理，请按刚才的提示检查。":
            ui.deleteData?L"应用已卸载，当前 Windows 用户下的本应用数据已清理。":L"应用已卸载，个人账号和设置已保留。"):
            L"学习通作业看板已就绪。可以从桌面或开始菜单打开。"):
            (ui.outcome==ERROR_INSTALL_USEREXIT?L"Windows 已结束本次操作。可以稍后重新运行安装包。":L"请检查安装权限与磁盘空间，然后重新运行安装包。可保存 MSI 安装日志进一步排查。" );
        if(ui.outcome==ERROR_SUCCESS_REBOOT_REQUIRED) description=L"操作已完成。请重新启动 Windows，以完成剩余更改。";
        button=L"完成";
    }
    SetWindowTextW(ui.title,title);SetWindowTextW(ui.description,description);SetWindowTextW(ui.next,button);
    SetWindowTextW(ui.note,location?(ui.upgrade?L"已沿用原安装位置，可在此调整。":L"升级时会自动沿用这里的位置。"):
        ui.upgrade?L"个人数据与提醒设置保留\n自动替换旧版，无需手动卸载":L"桌面与开始菜单快捷方式\n支持在 Windows 设置中卸载");
    Move(ui.note,216,location?326:320,510,52);
    if(ui.window) {InvalidateRect(ui.window,nullptr,FALSE); if(!progress) SetFocus(location?ui.edit:ui.next);}
}
void Layout() {
    Move(ui.title,216,76,510,46);Move(ui.description,216,132,510,57);
    Move(ui.edit,234,236,348,26);Move(ui.browse,598,223,112,42);
    Move(ui.status,216,210,510,56);Move(ui.back,216,450,100,40);
    Move(ui.cancel,482,450,100,40);Move(ui.next,598,450,128,40);
    Move(ui.remove,216,383,128,38);
    SetPage(ui.page);
}
void ChooseFolder() {
    IFileOpenDialog* dialog=nullptr;
    HRESULT hr=CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&dialog));
    if(FAILED(hr)) {MessageBoxW(ui.window,L"无法打开文件夹选择器，请直接输入安装路径。",L"选择安装位置",MB_OK|MB_ICONWARNING);return;}
    DWORD options=0;dialog->GetOptions(&options);
    dialog->SetOptions(options|FOS_PICKFOLDERS|FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_NOCHANGEDIR);
    dialog->SetTitle(L"选择安装文件夹");dialog->SetOkButtonLabel(L"选择此文件夹");
    std::wstring current=ReadEdit(); IShellItem* initial=nullptr;
    while(current.size()>3&&GetFileAttributesW(current.c_str())==INVALID_FILE_ATTRIBUTES) {
        auto slash=current.find_last_of(L"\\/");if(slash==std::wstring::npos)break;
        current.resize(slash==2?3:slash);
    }
    if(SUCCEEDED(SHCreateItemFromParsingName(current.c_str(),nullptr,IID_PPV_ARGS(&initial)))) {dialog->SetFolder(initial);initial->Release();}
    if(SUCCEEDED(dialog->Show(ui.window))) {
        IShellItem* item=nullptr;PWSTR path=nullptr;
        if(SUCCEEDED(dialog->GetResult(&item))) {
            if(SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH,&path))) {SetWindowTextW(ui.edit,path);CoTaskMemFree(path);}
            item->Release();
        }
    }
    dialog->Release();
}
bool ValidatePath() {
    auto value=ReadEdit();auto a=value.find_first_not_of(L" \t\r\n");auto b=value.find_last_not_of(L" \t\r\n");
    if(a!=std::wstring::npos)value=value.substr(a,b-a+1);else value.clear();
    bool drive=value.size()>3&&iswalpha(value[0])&&value[1]==L':'&&value[2]==L'\\';
    bool unc=value.size()>4&&value.rfind(L"\\\\",0)==0;
    bool valid=(drive||unc)&&value.find_first_of(L"\"<>|?*\r\n")==std::wstring::npos;
    if(value.find(L':',drive?2:0)!=std::wstring::npos)valid=false;
    if(valid) {
        DWORD size=GetFullPathNameW(value.c_str(),0,nullptr,nullptr);
        std::wstring normalized(size,L'\0');
        DWORD length=GetFullPathNameW(value.c_str(),size,normalized.data(),nullptr);
        if(length>0&&length<size) {normalized.resize(length);value=normalized;}else valid=false;
        if(value.size()<=3)valid=false;
    }
    DWORD attrs=GetFileAttributesW(value.c_str());
    if(attrs!=INVALID_FILE_ATTRIBUTES&&!(attrs&FILE_ATTRIBUTE_DIRECTORY)) valid=false;
    if(!valid) {MessageBoxW(ui.window,L"请输入完整的文件夹路径，例如 C:\\Apps\\chaoxingTasksPanel。请勿使用磁盘根目录或文件名。",L"检查安装位置",MB_OK|MB_ICONWARNING);SetFocus(ui.edit);return false;}
    ui.path=value;SetWindowTextW(ui.edit,value.c_str());return true;
}
void RequestCancel() {
    if(ui.page==4) {DestroyWindow(ui.window);return;}
    if(ui.page==3) {
        if(MessageBoxW(ui.window,L"确定取消当前操作？Windows 将尝试回滚已经执行的更改。",L"取消操作",MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2)!=IDYES)return;
        InterlockedExchange(&ui.canceled,1);EnableWindow(ui.cancel,FALSE);SetWindowTextW(ui.status,L"正在取消，请等待 Windows 完成回滚…");return;
    }
    InterlockedExchange(&ui.canceled,1);SetEvent(ui.chosen);DestroyWindow(ui.window);
}
void BeginOperation() {
    if(ui.mode==Mode::Uninstall&&!ui.dataChoiceMade) {
        int choice=UserDataCleanup::Ask(ui.window);
        if(choice==IDCANCEL) {
            if(ui.removeRequested)RequestCancel();
            return;
        }
        ui.deleteData=choice==IDYES;ui.dataChoiceMade=true;
    }
    // Keep the user's active installer as the elevation owner. Windows still
    // controls whether and how the secure-desktop consent dialog is displayed.
    SetForegroundWindow(ui.window);SetActiveWindow(ui.window);
    AllowSetForegroundWindow(ASFW_ANY);
    SetPage(3);SetWindowTextW(ui.status,L"正在准备 Windows 安装服务…");SetEvent(ui.chosen);
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    switch(message) {
    case WM_CREATE: {
        ui.window=window;
        auto label=[&](const wchar_t* text){return CreateWindowExW(0,L"STATIC",text,WS_CHILD|WS_VISIBLE,0,0,1,1,window,nullptr,ui.module,nullptr);};
        auto button=[&](int id,const wchar_t* text){return CreateWindowExW(0,L"BUTTON",text,WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,0,0,1,1,window,reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),ui.module,nullptr);};
        ui.title=label(L"");ui.description=label(L"");ui.note=label(L"");ui.status=label(L"");
        ui.edit=CreateWindowExW(0,L"EDIT",ui.path.c_str(),WS_CHILD|WS_TABSTOP|ES_AUTOHSCROLL,0,0,1,1,window,reinterpret_cast<HMENU>(Path),ui.module,nullptr);
        SendMessageW(ui.edit,EM_SETLIMITTEXT,30000,0);
        ui.browse=button(Browse,L"浏览文件夹");ui.back=button(Back,L"上一步");ui.cancel=button(Cancel,L"取消");
        ui.next=button(Next,L"开始设置");ui.remove=button(Remove,L"卸载应用");
        Fonts();Layout();return 0;
    }
    case WM_CTLCOLORSTATIC: {
        SetBkMode(reinterpret_cast<HDC>(w),TRANSPARENT);
        SetTextColor(reinterpret_cast<HDC>(w),reinterpret_cast<HWND>(l)==ui.title?Ink:Muted);
        return reinterpret_cast<LRESULT>(ui.white);
    }
    case WM_CTLCOLOREDIT:
        SetTextColor(reinterpret_cast<HDC>(w),Ink);SetBkColor(reinterpret_cast<HDC>(w),Pale);return reinterpret_cast<LRESULT>(ui.pale);
    case WM_ERASEBKGND: return 1;
    case WM_PAINT: {
        PAINTSTRUCT ps;HDC target=BeginPaint(window,&ps);RECT client;GetClientRect(window,&client);
        HDC dc=CreateCompatibleDC(target);HBITMAP bitmap=CreateCompatibleBitmap(target,client.right,client.bottom);auto old=SelectObject(dc,bitmap);
        FillRect(dc,&client,ui.white);auto side=CreateSolidBrush(Side);RECT bar=R(0,0,180,520);FillRect(dc,&bar,side);DeleteObject(side);
        if(ui.icon)DrawIconEx(dc,S(24),S(32),ui.icon,S(44),S(44),0,nullptr,DI_NORMAL);
        Text(dc,L"学习通\n作业看板",R(24,92,138,60),ui.brand,RGB(244,249,253));
        Text(dc,L"WINDOWS  ·  v"+ui.version,R(24,164,145,22),ui.small,RGB(155,186,200));
        const wchar_t* steps[]={L"欢迎",L"安装位置",ui.upgrade?L"开始升级":L"开始安装",L"完成"};
        int active=ui.page<=2?ui.page:ui.page==3?2:3;if(ui.page==5)active=1;
        for(int i=0;i<4;i++) {
            int y=226+i*43;
            if(i==active)Rounded(dc,R(16,y-4,148,35),RGB(30,72,86),RGB(30,72,86),10);
            Text(dc,std::to_wstring(i+1),R(28,y+2,20,22),ui.body,i==active?RGB(102,222,216):RGB(134,164,179));
            Text(dc,steps[i],R(56,y+2,99,22),ui.body,i==active?RGB(255,255,255):RGB(164,190,202));
        }
        Text(dc,L"chaoxingTaskPanel",R(24,477,145,20),ui.small,RGB(135,168,184));
        if(ui.page==0) {
            Rounded(dc,R(216,216,510,144),Pale,Pale);
            Text(dc,L"作业、状态与截止时间",R(238,236,450,27),ui.brand,Ink);
            Text(dc,L"查看待办和提交状态\n接收作业截止提醒\n为多个账号保留独立登录",R(238,278,450,74),ui.body,Muted);
        }
        if(ui.page==1||ui.page==2||ui.page==5) {
            Rounded(dc,R(216,206,510,98),Pale,Border);
            Text(dc,L"安装文件夹",R(234,217,340,19),ui.small,Muted);
            if(ui.page!=1) Text(dc,ui.path,R(234,246,470,48),ui.body,Ink,DT_LEFT|DT_WORDBREAK|DT_NOPREFIX);
        }
        if(ui.page==3) {
            Rounded(dc,R(216,286,510,10),RGB(226,237,242),RGB(226,237,242),10);
            if(ui.percent>0)Rounded(dc,R(216,286,std::max(5,510*ui.percent/100),10),Accent,Accent,10);
            Text(dc,std::to_wstring(ui.percent)+L"%",R(216,316,510,24),ui.body,Muted);
        }
        if(ui.page==4) {
            bool success=ui.outcome==ERROR_SUCCESS||ui.outcome==ERROR_SUCCESS_REBOOT_REQUIRED;
            Rounded(dc,R(216,220,510,94),success?RGB(232,247,241):Pale,success?RGB(204,232,221):Border);
            Text(dc,success?L"所选操作已成功完成":L"可以稍后重新运行安装包",R(236,246,460,32),ui.brand,success?RGB(27,111,81):Ink);
        }
        BitBlt(target,0,0,client.right,client.bottom,dc,0,0,SRCCOPY);
        SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);EndPaint(window,&ps);return 0;
    }
    case WM_DRAWITEM: {
        auto d=reinterpret_cast<DRAWITEMSTRUCT*>(l);bool primary=d->CtlID==Next,pressed=(d->itemState&ODS_SELECTED)!=0,disabled=(d->itemState&ODS_DISABLED)!=0;
        COLORREF fill=primary?(pressed?RGB(7,86,100):Accent):RGB(255,255,255);
        COLORREF edge=primary?fill:Border; if(disabled)fill=RGB(221,229,234);
        Rounded(d->hDC,d->rcItem,fill,edge,10);
        wchar_t text[128];GetWindowTextW(d->hwndItem,text,128);
        Text(d->hDC,text,d->rcItem,ui.body,disabled?Muted:primary?RGB(255,255,255):Ink,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
        if(d->itemState&ODS_FOCUS){RECT r=d->rcItem;InflateRect(&r,-S(4),-S(4));DrawFocusRect(d->hDC,&r);} return TRUE;
    }
    case WM_COMMAND: {
        if(HIWORD(w)!=BN_CLICKED)return 0;
        switch(LOWORD(w)) {
        case Cancel:RequestCancel();break;
        case Browse:ChooseFolder();break;
        case Back:if(ui.page==1)SetPage(0);else if(ui.page==2)SetPage(1);break;
        case Remove:ui.mode=Mode::Uninstall;BeginOperation();break;
        case Next:
            if(ui.page==0)SetPage(1);
            else if(ui.page==1&&ValidatePath())SetPage(2);
            else if(ui.page==2)BeginOperation();
            else if(ui.page==5){ui.mode=ui.removeRequested?Mode::Uninstall:Mode::Repair;BeginOperation();}
            else if(ui.page==4)DestroyWindow(window);
            break;
        } return 0;
    }
    case StatusMessage: {auto text=reinterpret_cast<std::wstring*>(l);if(ui.page==3&&!ui.canceled)SetWindowTextW(ui.status,text->c_str());delete text;return 0;}
    case ProgressMessage:ui.percent=std::clamp(static_cast<int>(w),0,100);InvalidateRect(window,nullptr,FALSE);return 0;
    case FinishMessage:ui.outcome=static_cast<UINT>(w);SetPage(4);return 0;
    case WM_CLOSE:RequestCancel();return 0;
    case WM_DPICHANGED: {
        ui.dpi=HIWORD(w);auto suggested=reinterpret_cast<RECT*>(l);
        SetWindowPos(window,nullptr,suggested->left,suggested->top,suggested->right-suggested->left,suggested->bottom-suggested->top,SWP_NOZORDER|SWP_NOACTIVATE);
        Fonts();Layout();return 0;
    }
    case WM_DESTROY:ui.window=nullptr;SetEvent(ui.chosen);PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(window,message,w,l);
}
DWORD WINAPI UIThread(void*) {
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    using SetDpi=HANDLE(WINAPI*)(HANDLE);auto setDpi=reinterpret_cast<SetDpi>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"SetThreadDpiAwarenessContext"));
    HANDLE oldDpi=setDpi?setDpi(reinterpret_cast<HANDLE>(-4)):nullptr;
    using GetDpi=UINT(WINAPI*)();auto getDpi=reinterpret_cast<GetDpi>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"GetDpiForSystem"));
    ui.dpi=getDpi?getDpi():96;ui.white=CreateSolidBrush(RGB(255,255,255));ui.pale=CreateSolidBrush(Pale);
    ui.icon=reinterpret_cast<HICON>(LoadImageW(ui.module,MAKEINTRESOURCEW(101),IMAGE_ICON,S(48),S(48),LR_DEFAULTCOLOR));
    WNDCLASSW wc{};wc.hInstance=ui.module;wc.lpfnWndProc=WindowProc;wc.lpszClassName=L"ChaoxingModernInstaller";wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hIcon=ui.icon;
    RegisterClassW(&wc);
    RECT bounds={0,0,S(760),S(520)};
    using Adjust=BOOL(WINAPI*)(LPRECT,DWORD,BOOL,DWORD,UINT);auto adjust=reinterpret_cast<Adjust>(GetProcAddress(GetModuleHandleW(L"user32.dll"),"AdjustWindowRectExForDpi"));
    DWORD style=WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX|WS_CLIPCHILDREN;
    if(adjust)adjust(&bounds,style,FALSE,WS_EX_CONTROLPARENT,ui.dpi);else AdjustWindowRectEx(&bounds,style,FALSE,WS_EX_CONTROLPARENT);
    RECT work;SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);int width=bounds.right-bounds.left,height=bounds.bottom-bounds.top;
    ui.page=ui.installed||ui.removeRequested?5:0;
    HWND window=CreateWindowExW(WS_EX_CONTROLPARENT,wc.lpszClassName,L"学习通作业看板 · 安装向导",style,
        work.left+(work.right-work.left-width)/2,work.top+(work.bottom-work.top-height)/2,width,height,nullptr,nullptr,ui.module,nullptr);
    SetEvent(ui.ready);
    if(window) {
        ShowWindow(window,SW_SHOWNORMAL);UpdateWindow(window);SetForegroundWindow(window);
        if(ui.removeRequested)PostMessageW(window,WM_COMMAND,Next,0);
        MSG message;
        while(GetMessageW(&message,nullptr,0,0)>0) {
            if(message.message==WM_KEYDOWN&&message.wParam=='A'&&(GetKeyState(VK_CONTROL)&0x8000)&&GetFocus()==ui.edit){SendMessageW(ui.edit,EM_SETSEL,0,-1);continue;}
            if(message.message==WM_KEYDOWN&&message.wParam==VK_ESCAPE){RequestCancel();continue;}
            if(message.message==WM_KEYDOWN&&message.wParam==VK_RETURN&&ui.page!=3){SendMessageW(window,WM_COMMAND,Next,0);continue;}
            if(!IsDialogMessageW(window,&message)){TranslateMessage(&message);DispatchMessageW(&message);}
        }
    }else {InterlockedExchange(&ui.canceled,1);SetEvent(ui.chosen);}
    MSG pending;while(PeekMessageW(&pending,nullptr,StatusMessage,StatusMessage,PM_REMOVE))delete reinterpret_cast<std::wstring*>(pending.lParam);
    for(auto font:{ui.body,ui.heading,ui.small,ui.brand})if(font)DeleteObject(font);
    if(ui.icon)DestroyIcon(ui.icon);DeleteObject(ui.white);DeleteObject(ui.pale);
    UnregisterClassW(wc.lpszClassName,ui.module);
    if(setDpi)setDpi(oldDpi);CoUninitialize();return 0;
}
void CloseThread() {
    if(ui.thread){WaitForSingleObject(ui.thread,INFINITE);CloseHandle(ui.thread);ui.thread=nullptr;}
    if(ui.ready){CloseHandle(ui.ready);ui.ready=nullptr;}if(ui.chosen){CloseHandle(ui.chosen);ui.chosen=nullptr;}
}
}

extern "C" __declspec(dllexport) UINT WINAPI InitializeEmbeddedUI(MSIHANDLE session,LPCWSTR,LPDWORD level) {
    if(!Compatibility::CurrentSupported())return INSTALLUILEVEL_FULL;
    ui.removeRequested=ReadProperty(session,L"REMOVE")==L"ALL";
    if((*level&7)<INSTALLUILEVEL_FULL&&(!ui.removeRequested||(*level&7)<INSTALLUILEVEL_BASIC))return *level&7;
    ui.mode=Mode::Install;
    ui.deleteData=false;ui.dataChoiceMade=false;ui.cleanupWarning=false;
    ui.canceled=0;ui.hadError=0;ui.ended=false;ui.outcome=ERROR_INSTALL_FAILURE;
    ui.path=ReadProperty(session,L"INSTALLDIR");ui.version=ReadProperty(session,L"ProductVersion");
    if(ui.path.empty()) {
        HKEY key=nullptr;wchar_t path[32768];DWORD size=sizeof(path),type=0;
        if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"Software\\CoolLiangYu\\chaoxingTasksPanel",0,KEY_READ|KEY_WOW64_64KEY,&key)==ERROR_SUCCESS) {
            if(RegQueryValueExW(key,L"InstallDir",nullptr,&type,reinterpret_cast<BYTE*>(path),&size)==ERROR_SUCCESS&&type==REG_SZ&&size>=sizeof(wchar_t)){
                path[32767]=0;ui.path=path;
            }RegCloseKey(key);
        }
    }
    if(ui.path.empty()) {PWSTR programFiles=nullptr;SHGetKnownFolderPath(FOLDERID_ProgramFiles,0,nullptr,&programFiles);
        ui.path=programFiles?programFiles:L"C:\\Program Files";CoTaskMemFree(programFiles);ui.path+=L"\\chaoxingTasksPanel";}
    std::wstring product=ReadProperty(session,L"ProductCode");
    ui.installed=!ReadProperty(session,L"Installed").empty()||MsiQueryProductStateW(product.c_str())==INSTALLSTATE_DEFAULT;
    ui.upgrade=false;
    auto upgradeCode=ReadProperty(session,L"UpgradeCode");
    if(!ui.installed&&!upgradeCode.empty()) {
        wchar_t related[39];
        for(DWORD index=0;MsiEnumRelatedProductsW(upgradeCode.c_str(),0,index,related)==ERROR_SUCCESS;index++) {
            if(product==related||MsiQueryProductStateW(related)!=INSTALLSTATE_DEFAULT)continue;
            auto version=ProductInfo(related,INSTALLPROPERTY_VERSIONSTRING);
            if(!version.empty()&&CompareVersions(version,ui.version)<=0){ui.upgrade=true;ui.mode=Mode::Upgrade;break;}
        }
    }
    ui.ready=CreateEventW(nullptr,TRUE,FALSE,nullptr);ui.chosen=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!ui.ready||!ui.chosen){CloseThread();return INSTALLUILEVEL_FULL;}
    ui.thread=CreateThread(nullptr,0,UIThread,nullptr,0,nullptr);
    if(!ui.thread){CloseThread();return INSTALLUILEVEL_FULL;}
    WaitForSingleObject(ui.ready,INFINITE);
    if(ui.window) {
        HWND owner=ui.window;
        MsiSetInternalUI(INSTALLUILEVEL_NOCHANGE,&owner);
        ui.previousOwner=owner;ui.ownerSet=true;
    }
    WaitForSingleObject(ui.chosen,INFINITE);
    if(ui.canceled){
        if(ui.ownerSet){HWND owner=ui.previousOwner;MsiSetInternalUI(INSTALLUILEVEL_NOCHANGE,&owner);ui.ownerSet=false;}
        CloseThread();
        MsiSetPropertyW(session,L"MODERN_UI_CANCELED",L"1");
        // The client UI sequence returns a normal user-exit before contacting
        // the elevated execute sequence or displaying any fallback dialog.
        *level=INSTALLUILEVEL_FULL;
        return ERROR_SUCCESS;
    }
    MsiSetPropertyW(session,L"INSTALLDIR",ui.path.c_str());
    if(ui.mode==Mode::Uninstall) {
        MsiSetPropertyW(session,L"REMOVE",L"ALL");
        MsiSetPropertyW(session,L"DELETEUSERDATA",ui.deleteData?L"1":L"0");
        MsiSetPropertyW(session,L"DATACHOICE_MADE",L"1");
    }
    if(ui.mode==Mode::Repair){MsiSetPropertyW(session,L"REINSTALL",L"ALL");MsiSetPropertyW(session,L"REINSTALLMODE",L"amus");}
    *level=INSTALLUILEVEL_NONE|INSTALLUILEVEL_SOURCERESONLY|UacOnly;
    return ERROR_SUCCESS;
}

// An immediate action before any costing or transaction turns a canceled setup
// into MSI's normal user-exit status. Returning 1602 from InitializeEmbeddedUI
// itself is treated as an initialization failure by Windows Installer.
extern "C" __declspec(dllexport) UINT WINAPI CancelBeforeInstall(MSIHANDLE) {
    return ERROR_INSTALL_USEREXIT;
}

extern "C" __declspec(dllexport) UINT WINAPI CheckWindowsSupport(MSIHANDLE session) {
    using VersionQuery=VOID(WINAPI*)(DWORD*,DWORD*,DWORD*);
    auto query=reinterpret_cast<VersionQuery>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"RtlGetNtVersionNumbers"));
    DWORD major=0,minor=0,build=0;if(query)query(&major,&minor,&build);
    SYSTEM_INFO info{};GetNativeSystemInfo(&info);
    wchar_t details[160];swprintf(details,160,L"%lu.%lu.%lu; native-arch=%u",major,minor,build&0x0fffffff,info.wProcessorArchitecture);
    MsiSetPropertyW(session,L"OS_DETECTED",details);
    return MsiSetPropertyW(session,L"OS_COMPATIBLE",Compatibility::CurrentSupported()?L"1":L"");
}

extern "C" __declspec(dllexport) UINT WINAPI CheckUpgradeOrder(MSIHANDLE session) {
    bool compatible=true;
    auto build=ReadProperty(session,L"INSTALLER_BUILD"),version=ReadProperty(session,L"ProductVersion");
    auto products=ReadProperty(session,L"OLDPRODUCTS");
    auto current=wcstoull(build.c_str(),nullptr,10);
    if(ReadProperty(session,L"Installed").empty()&&ReadProperty(session,L"REMOVE")!=L"ALL") {
        size_t start=0;
        while(start<products.size()) {
            size_t end=products.find(L';',start);auto product=products.substr(start,end-start);
            auto previousVersion=ProductInfo(product.c_str(),INSTALLPROPERTY_VERSIONSTRING);
            if(CompareVersions(previousVersion,version)==0&&CachedBuild(product)>current){compatible=false;break;}
            if(end==std::wstring::npos)break;start=end+1;
        }
    }
    return MsiSetPropertyW(session,L"BUILD_COMPATIBLE",compatible?L"1":L"");
}

extern "C" __declspec(dllexport) INT WINAPI EmbeddedUIHandler(UINT type,MSIHANDLE record) {
    UINT message=type&0xff000000;
    static long long total=0,current=0,ticksPerAction=0;static bool backward=false,actionTicks=false;
    if(message==INSTALLMESSAGE_PROGRESS) {
        int event=MsiRecordGetInteger(record,1);long long value=MsiRecordGetInteger(record,2);
        if(event==0){total=value;backward=MsiRecordGetInteger(record,3)!=0;current=backward?total:0;actionTicks=false;}
        else if(event==1){ticksPerAction=value;actionTicks=MsiRecordGetInteger(record,3)!=0;}
        else if(event==2)current+=backward?-value:value;
        else if(event==3)total+=value;
        if(ui.window&&total>0)PostMessageW(ui.window,ProgressMessage,static_cast<WPARAM>(100*std::clamp(current,0LL,total)/total),0);
    }
    if(message==INSTALLMESSAGE_ACTIONDATA&&actionTicks) {
        current+=backward?-ticksPerAction:ticksPerAction;
        if(ui.window&&total>0)PostMessageW(ui.window,ProgressMessage,static_cast<WPARAM>(100*std::clamp(current,0LL,total)/total),0);
    }
    if(message==INSTALLMESSAGE_ACTIONSTART){auto description=RecordString(record,2);if(!description.empty())Status(description);}
    if(message==INSTALLMESSAGE_INSTALLEND) {
        ui.ended=true;
        int result=MsiRecordGetInteger(record,3);
        // INSTALLEND carries MSI action return values (1,2,3,4), not
        // the Win32 error returned by MsiInstallProduct (0,1602,1603,1604).
        switch(result) {
        case 0:case 1:ui.outcome=ERROR_SUCCESS;break;
        case 2:ui.outcome=ERROR_INSTALL_USEREXIT;break;
        case 3:ui.outcome=ERROR_INSTALL_FAILURE;break;
        case 4:ui.outcome=ERROR_INSTALL_SUSPEND;break;
        default:ui.outcome=result==MSI_NULL_INTEGER?ERROR_INSTALL_FAILURE:static_cast<UINT>(result);break;
        }
    }
    if(message==INSTALLMESSAGE_ERROR||message==INSTALLMESSAGE_WARNING||message==INSTALLMESSAGE_USER||message==INSTALLMESSAGE_FATALEXIT) {
        if(message==INSTALLMESSAGE_WARNING&&RecordString(record,1)==L"PANEL_CLEANUP_WARNING")ui.cleanupWarning=true;
        if(message==INSTALLMESSAGE_ERROR||message==INSTALLMESSAGE_FATALEXIT)InterlockedExchange(&ui.hadError,1);
        DWORD count=0;wchar_t empty=0;MsiFormatRecordW(0,record,&empty,&count);std::wstring text(count+1,L'\0');DWORD size=count+1;
        MsiFormatRecordW(0,record,text.data(),&size);text.resize(size);
#ifdef ENGINE_TEST_EXE
        std::fwprintf(stderr,L"MSI message: %ls\n",text.c_str());
        return IDOK;
#endif
        if(text.empty())text=L"Windows Installer 报告了安装问题。请查看安装日志。";
        UINT flags=type&0x00ffffff;
        return MessageBoxW(ui.window,text.c_str(),L"学习通作业看板 · Windows Installer",flags|MB_SETFOREGROUND);
    }
    if(message==INSTALLMESSAGE_FILESINUSE||message==INSTALLMESSAGE_RMFILESINUSE)
        return MessageBoxW(ui.window,L"部分文件正在使用中。请右键托盘图标，选择“退出面板”并确认退出，然后重试。关闭主窗口只会隐藏到托盘。",L"文件正在使用",MB_RETRYCANCEL|MB_ICONWARNING);
    if(ui.canceled&&(message==INSTALLMESSAGE_PROGRESS||message==INSTALLMESSAGE_ACTIONSTART||message==INSTALLMESSAGE_ACTIONDATA))return IDCANCEL;
    return 0;
}

extern "C" __declspec(dllexport) VOID WINAPI ShutdownEmbeddedUI() {
    if(ui.ownerSet){HWND owner=ui.previousOwner;MsiSetInternalUI(INSTALLUILEVEL_NOCHANGE,&owner);ui.ownerSet=false;}
    UINT outcome=ui.outcome;
    if(ui.canceled&&outcome!=ERROR_SUCCESS&&outcome!=ERROR_SUCCESS_REBOOT_REQUIRED)outcome=ERROR_INSTALL_USEREXIT;
    if(ui.window)PostMessageW(ui.window,FinishMessage,outcome,0);
    CloseThread();
}

BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){ui.module=instance;DisableThreadLibraryCalls(instance);}return TRUE;
}

#ifdef PREVIEW_EXE
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int) {
    ui.module=instance;
    wchar_t filename[32768];GetModuleFileNameW(nullptr,filename,32768);std::wstring folder=filename;folder=folder.substr(0,folder.find_last_of(L"\\")+1);
    wchar_t target[32768];GetPrivateProfileStringW(L"Preview",L"Package",L"",target,32768,(folder+L"preview.ini").c_str());
    MSIHANDLE session=0;MsiSetInternalUI(INSTALLUILEVEL_NONE,nullptr);
    if(MsiOpenPackageExW(target,MSIOPENPACKAGEFLAGS_IGNOREMACHINESTATE,&session)!=ERROR_SUCCESS)return 1;
    DWORD level=INSTALLUILEVEL_FULL;
    if(InitializeEmbeddedUI(session,L"",&level)==ERROR_SUCCESS) {
        // Visual preview only: no INSTALL, ExecuteAction, registration, or file copying.
        for(int i=0;i<=100;i+=10){PostMessageW(ui.window,ProgressMessage,i,0);Status(L"正在预览安装进度…");Sleep(120);}
        ui.outcome=ERROR_SUCCESS;ShutdownEmbeddedUI();
    }
    MsiCloseHandle(session);return 0;
}
#endif

#ifdef ENGINE_TEST_EXE
int wmain(int argc,wchar_t** argv) {
    if(argc<2||argc>3)return 2;
    UINT expected=argc==3&&std::wstring(argv[2])==L"cancel"?ERROR_INSTALL_USEREXIT:
        argc==3&&std::wstring(argv[2])==L"failure"?ERROR_INSTALL_FAILURE:ERROR_SUCCESS;
    // Only run with a disposable fixture whose execute sequence has no product,
    // file, registry, shortcut, upgrade, or removal actions (New-UIFixture.ps1).
    MsiSetInternalUI(INSTALLUILEVEL_NONE,nullptr);
    MSIHANDLE database=0,view=0,record=0;
    if(MsiOpenDatabaseW(argv[1],MSIDBOPEN_READONLY,&database)!=ERROR_SUCCESS)return 3;
    MsiDatabaseOpenViewW(database,L"SELECT `Action` FROM `InstallExecuteSequence`",&view);
    MsiViewExecute(view,0);
    bool safe=true;
    while(MsiViewFetch(view,&record)==ERROR_SUCCESS) {
        auto action=RecordString(record,1);MsiCloseHandle(record);
        if(action!=L"CostInitialize"&&action!=L"FileCost"&&action!=L"CostFinalize"&&
            action!=L"InstallInitialize"&&action!=L"InstallFinalize"&&
            action!=L"CancelModernUI"&&action!=L"LaunchConditions")safe=false;
    }
    MsiViewClose(view);MsiCloseHandle(view);
    if(!safe){MsiCloseHandle(database);std::fwprintf(stderr,L"Refusing a package with real installation actions. Use New-UIFixture.ps1.\n");return 4;}
    MsiDatabaseOpenViewW(database,L"SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'",&view);
    MsiViewExecute(view,0);MsiViewFetch(view,&record);std::wstring product=RecordString(record,1);
    MsiCloseHandle(record);MsiViewClose(view);MsiCloseHandle(view);MsiCloseHandle(database);
    auto callback=[](LPVOID,UINT type,MSIHANDLE value)->INT {return EmbeddedUIHandler(type,value);};
    INSTALLUI_HANDLER_RECORD previous=nullptr;
    MsiSetExternalUIRecord(callback,0x0E003FFF,nullptr,&previous);
    std::wstring log=std::wstring(argv[1])+L".engine.log";
    MsiEnableLogW(0x3fff,log.c_str(),0);
    UINT result=MsiInstallProductW(argv[1],L"");
    MsiSetExternalUIRecord(nullptr,0,nullptr,&previous);
    auto state=MsiQueryProductStateW(product.c_str());
    std::wprintf(L"MSI result: %u; completion callback: %d; callback result: %u; product state: %d\n",result,ui.ended,ui.outcome,state);
    return result==expected&&ui.ended&&ui.outcome==expected&&state==INSTALLSTATE_UNKNOWN?0:1;
}
#endif
