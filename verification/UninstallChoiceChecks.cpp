#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <msi.h>
#include <msiquery.h>
#include <string>
#include <iostream>
#include <stdexcept>
int answer=IDNO,calls=0;
int CALLBACK Message(void*,UINT type,MSIHANDLE) {
    if((type&0xff000000)==INSTALLMESSAGE_USER){calls++;return answer;}
    return IDOK;
}
std::wstring Property(MSIHANDLE session,const wchar_t* name) {
    DWORD length=0;wchar_t empty=0;MsiGetPropertyW(session,name,&empty,&length);
    std::wstring value(length+1,L'\0');DWORD count=length+1;
    MsiGetPropertyW(session,name,value.data(),&count);value.resize(count);return value;
}
void Check(bool ok,const char* label){if(!ok)throw std::runtime_error(label);std::cout<<"PASS: "<<label<<"\n";}
int wmain(int argc,wchar_t** argv) {
    if(argc!=2)return 2;
    MSIHANDLE session=0;
    try {
        INSTALLUI_HANDLER_RECORD old=nullptr;
        MsiSetExternalUIRecord(Message,INSTALLLOGMODE_USER|INSTALLLOGMODE_ERROR|INSTALLLOGMODE_WARNING,nullptr,&old);
        for(auto level:{INSTALLUILEVEL_FULL,INSTALLUILEVEL_BASIC,INSTALLUILEVEL_NONE}) {
            MsiSetInternalUI(level,nullptr);
            Check(MsiOpenPackageExW(argv[1],0,&session)==ERROR_SUCCESS,"Open test session without running INSTALL");
            MsiSetPropertyW(session,L"REMOVE",L"ALL");MsiSetPropertyW(session,L"UPGRADINGPRODUCTCODE",L"");
            for(auto choice:{IDNO,IDYES,IDCANCEL}) {
                answer=choice;calls=0;
                MsiSetPropertyW(session,L"DELETEUSERDATA",L"0");MsiSetPropertyW(session,L"DATACHOICE_MADE",L"");
                UINT result=MsiDoActionW(session,L"PromptUninstallData");
                std::wcout<<L"Prompt: UI="<<Property(session,L"UILevel")<<L" result="<<result<<L" calls="<<calls<<L" delete="<<Property(session,L"DELETEUSERDATA")<<L" choice="<<Property(session,L"DATACHOICE_MADE")<<L"\n";
                if(level==INSTALLUILEVEL_NONE) {
                    Check(result==ERROR_SUCCESS&&calls==0&&Property(session,L"DELETEUSERDATA")==L"0","Silent removal preserves data and never prompts");continue;
                }
                if(choice==IDCANCEL)Check(result==ERROR_INSTALL_USEREXIT&&calls==1,"Cancel data choice stops removal before transaction");
                else {
                    Check(result==ERROR_SUCCESS&&calls==1&&Property(session,L"DELETEUSERDATA")== (choice==IDYES?L"1":L"0"),"Full/basic removal records the explicit Yes/No answer");
                    Check(MsiDoActionW(session,L"PromptUninstallData")==ERROR_SUCCESS&&calls==1,"Choice is not asked twice");
                }
            }
            MsiSetPropertyW(session,L"DATACHOICE_MADE",L"");MsiSetPropertyW(session,L"UPGRADINGPRODUCTCODE",L"{TEST-UPGRADE}");calls=0;
            Check(MsiDoActionW(session,L"PromptUninstallData")==ERROR_SUCCESS&&calls==0,"Upgrade removal skips the data question");
            MsiSetPropertyW(session,L"UPGRADINGPRODUCTCODE",L"");MsiSetPropertyW(session,L"DELETEUSERDATA",L"1");
            Check(MsiDoActionW(session,L"PrepareUninstallCleanup")==ERROR_SUCCESS&&Property(session,L"CleanupStartup").rfind(L"1|S-1-",0)==0,"Prepare passes a user-bound deletion flag to commit without deleting anything");
            MsiCloseHandle(session);session=0;
        }
        // Never call CleanupStartup / CommitUninstallCleanup: real user data
        // must not be touched by these MSI protocol tests.
        MsiSetExternalUIRecord(nullptr,0,nullptr,&old);
    }catch(const std::exception& e){if(session)MsiCloseHandle(session);std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
    return 0;
}
