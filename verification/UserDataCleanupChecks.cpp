#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include "../InstallerUI/UserDataCleanup.h"
#include <fstream>
#include <iostream>
#include <stdexcept>

void Check(bool ok,const char* label) {
    if(!ok)throw std::runtime_error(label);
    std::cout<<"PASS: "<<label<<"\n";
}
bool Exists(const std::wstring& path){return GetFileAttributesW(path.c_str())!=INVALID_FILE_ATTRIBUTES;}
int wmain(int argc,wchar_t** argv) {
    if(argc!=2)return 2;
    std::wstring root=argv[1];
    // The runner creates a new directory and marker; refuse all other roots.
    if(root.find(L"\\cleanup-fixture-")==std::wstring::npos||!Exists(root+L"\\.codex-cleanup-tests"))return 3;
    try {
        UserDataCleanup::Context real;
        Check(UserDataCleanup::CurrentContext(real),"Original user identity and LocalAppData resolve");
        bool purge=false;
        Check(UserDataCleanup::DecodeForUser(UserDataCleanup::Encode(real,true),real,purge)&&purge,"Delete choice round-trips for the original user");
        Check(UserDataCleanup::DecodeForUser(UserDataCleanup::Encode(real,false),real,purge)&&!purge,"Keep choice round-trips");
        auto another=real;another.sid+=L"-1";
        Check(!UserDataCleanup::DecodeForUser(UserDataCleanup::Encode(another,true),real,purge),"Different Windows user refused");
        another=real;another.local=root;
        Check(!UserDataCleanup::DecodeForUser(UserDataCleanup::Encode(another,true),real,purge),"Redirected data path refused");
        Check(!UserDataCleanup::DecodeForUser(L"1|"+real.sid+L"|",real,purge),"Empty data path refused");
        Check(!UserDataCleanup::DecodeForUser(L"1|"+real.sid+L"|"+real.local+L"|extra",real,purge),"Malformed data refused");
        Check(UserDataCleanup::OwnCredential(L"ChaoxingStudyTasks/AutoLogin/account-1")&&
            !UserDataCleanup::OwnCredential(L"ChaoxingStudyTasks/AutoLogin/")&&
            !UserDataCleanup::OwnCredential(L"OtherApp/AutoLogin/account-1")&&
            !UserDataCleanup::OwnCredential(L"ChaoxingStudyTasks/AutoLoginOther/account-1"),"Credential filter is limited to this application's account prefix");
        std::vector<std::wstring> errors;
        Check(!UserDataCleanup::RemoveOwnedDirectory(root,L"..",errors)&&Exists(root+L"\\outside\\keep.txt"),"Parent traversal refused");
        Check(!UserDataCleanup::RemoveOwnedDirectory(L"",L"ChaoxingStudyTasks",errors),"Empty parent refused");
        Check(!UserDataCleanup::RemoveOwnedDirectory(root,L"outside",errors)&&Exists(root+L"\\outside\\keep.txt"),"Unowned leaf refused");
        bool normalRemoved=UserDataCleanup::RemoveOwnedDirectory(root+L"\\normal",L"ChaoxingStudyTasks",errors);
        for(auto error:errors)std::wcerr<<L"Cleanup error: "<<error<<L"\n";
        Check(normalRemoved&&
            !Exists(root+L"\\normal\\ChaoxingStudyTasks")&&errors.empty(),"Nested data, Chinese paths and readonly cache removed");
        Check(Exists(root+L"\\outside\\keep.txt"),"Nested junction target survives");
        Check(UserDataCleanup::RemoveOwnedDirectory(root+L"\\linked-leaf",L"ChaoxingStudyTasks",errors)&&
            !Exists(root+L"\\linked-leaf\\ChaoxingStudyTasks")&&Exists(root+L"\\outside\\keep.txt"),"Root junction unlinked without following its target");
        Check(!UserDataCleanup::RemoveOwnedDirectory(root+L"\\linked-parent",L"ChaoxingStudyTasks",errors)&&
            Exists(root+L"\\outside\\ChaoxingStudyTasks\\keep.txt"),"Redirected parent refused");
        errors.clear();
        auto locked=root+L"\\locked\\ChaoxingStudyTasks\\busy.txt";
        HANDLE file=CreateFileW(locked.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);
        Check(file!=INVALID_HANDLE_VALUE,"Locked-file fixture opened");
        bool removed=UserDataCleanup::RemoveOwnedDirectory(root+L"\\locked",L"ChaoxingStudyTasks",errors);
        CloseHandle(file);
        Check(!removed&&!errors.empty()&&Exists(locked),"Locked data yields a partial-cleanup report");
        errors.clear();
        Check(UserDataCleanup::RemoveOwnedDirectory(root+L"\\locked",L"ChaoxingStudyTasks",errors),"Cleanup succeeds after the file is closed");
        Check(UserDataCleanup::RemoveOwnedDirectory(root+L"\\missing",L"ChaoxingStudyTasks",errors),"Missing data is harmless");
        Check(Exists(root+L"\\outside\\keep.txt")&&Exists(root+L"\\outside\\ChaoxingStudyTasks\\keep.txt"),"External markers remain intact after all deletion checks");
    }catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
    return 0;
}
