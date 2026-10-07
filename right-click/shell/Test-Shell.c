#include "ShellAbi.h"
#include <stdio.h>
__declspec(dllimport) int WINAPI MultiByteToWideChar(UINT,DWORD,const char*,int,wchar_t*,int);
#define CP_ACP 0
__declspec(dllimport) HRESULT WINAPI CoInitializeEx(void*,DWORD);
__declspec(dllimport) void WINAPI CoUninitialize(void);
__declspec(dllimport) HRESULT WINAPI CoCreateGuid(GUID*);
__declspec(dllimport) int WINAPI StringFromGUID2(REFGUID,wchar_t*,int);
__declspec(dllimport) HRESULT WINAPI CoCreateInstance(REFCLSID,void*,DWORD,REFIID,void**);
__declspec(dllimport) HRESULT WINAPI SHCreateItemFromParsingName(const wchar_t*,void*,REFIID,void**);
__declspec(dllimport) HRESULT WINAPI SHCreateShellItemArrayFromShellItem(ShellItem*,REFIID,void**);
__declspec(dllimport) HRESULT WINAPI SHParseDisplayName(const wchar_t*,void*,void**,DWORD,DWORD*);
__declspec(dllimport) HRESULT WINAPI SHCreateShellItemArrayFromIDLists(UINT,const void**,ShellItemArray**);
static int failures;
static void Check(int condition,const char* name) { printf("%s: %s\n",condition ? "PASS" : "FAIL",name); if(!condition) ++failures; }
static DWORD FileState(ExplorerCommand* command,const wchar_t* path) {
 ShellItem* item=NULL;
 ShellItemArray* array=NULL;
 DWORD state=ECS_HIDDEN;
 HRESULT hr=SHCreateItemFromParsingName(path,NULL,&IID_Item,(void**)&item);
 if(SUCCEEDED(hr)) hr=SHCreateShellItemArrayFromShellItem(item,&IID_ItemArray,(void**)&array);
 if(SUCCEEDED(hr)) hr=command->lpVtbl->GetState(command,array,FALSE,&state);
 Check(SUCCEEDED(hr),"real shell item creation and state HRESULT");
 if(array) array->lpVtbl->Release(array);
 if(item) item->lpVtbl->Release(item);
 return state;
}
int main(int argc,char** argv) {
 HMODULE library;
 typedef HRESULT (WINAPI *GetClassFn)(REFCLSID,REFIID,void**);
 typedef HRESULT (WINAPI *CanUnloadFn)(void);
 GetClassFn getClass;
 CanUnloadFn canUnload;
 CommandFactory* factory=NULL;
 ExplorerCommand* command=NULL;
 HRESULT hr;
 wchar_t* text=NULL;
 DWORD state,flags;
 GUID canonical,testId;
 void* subcommands=NULL;
 wchar_t temporaryPath[MAX_PATH],testDirectory[MAX_PATH],testIdText[40],path[MAX_PATH],path1[MAX_PATH],path2[MAX_PATH];
 wchar_t libraryPath[32768];
 const wchar_t* extensions[]={L"pdf",L"doc",L"docx",L"ppt",L"pptx",L"xls",L"xlsx",L"png",L"jpg",L"jpeg",L"jp2",L"webp",L"gif",L"bmp",L"PDF",L"JpEg"};
 size_t i;
 HANDLE file;
 void* pidls[2]={NULL,NULL};
 ShellItemArray* multiple=NULL;
 if(argc != 2) return 2;
 if(strcmp(argv[1],"--registered")==0) {
  CoInitializeEx(NULL,2);
  hr=CoCreateInstance(&CLSID_MinerUCommand,NULL,5,&IID_Command,(void**)&command);
  Check(SUCCEEDED(hr) && command,"registered Appx COM activation");
  if(FAILED(hr) || !command) { printf("Activation HRESULT: 0x%08lx\n",hr); return 1; }
  hr=command->lpVtbl->GetTitle(command,NULL,&text);
  Check(SUCCEEDED(hr) && text && wcscmp(text,L"\u7528 MinerU \u8bc6\u522b")==0,"registered Chinese menu title");
  CoTaskMemFree(text);
  state=ECS_ENABLED;
  Check(command->lpVtbl->GetState(command,NULL,FALSE,&state)==S_OK && state==ECS_HIDDEN,"registered no selection hidden");
  flags=1;
  Check(command->lpVtbl->GetFlags(command,&flags)==S_OK && flags==ECF_DEFAULT,"registered single menu command");
  command->lpVtbl->Release(command); CoUninitialize();
  printf("Failures: %d\n",failures); return failures ? 1 : 0;
 }
 /* The test runner passes an ASCII short-path for the local DLL to avoid CRT code-page conversions. */
 MultiByteToWideChar(CP_ACP,0,argv[1],-1,libraryPath,32768);
 CoInitializeEx(NULL,2);
 library=LoadLibraryW(libraryPath);
 Check(library != NULL,"native DLL load without external runtime");
 if(!library) { printf("Win32 load error: %lu\n",GetLastError()); return 1; }
 getClass=(GetClassFn)GetProcAddress(library,"DllGetClassObject");
 canUnload=(CanUnloadFn)GetProcAddress(library,"DllCanUnloadNow");
 Check(getClass && canUnload,"COM exports present");
 if(!getClass || !canUnload) return 1;
 Check(canUnload()==S_OK,"DLL initially unloadable");
 hr=getClass(&CLSID_MinerUCommand,&IID_Factory,(void**)&factory);
 Check(SUCCEEDED(hr) && factory,"factory creation");
 Check(canUnload()==S_FALSE,"factory pins DLL");
 if(!factory) return 1;
 hr=factory->lpVtbl->CreateInstance(factory,NULL,&IID_Command,(void**)&command);
 Check(SUCCEEDED(hr) && command,"IExplorerCommand creation");
 if(!command) return 1;
 hr=command->lpVtbl->GetTitle(command,NULL,&text);
 Check(SUCCEEDED(hr) && text && wcscmp(text,L"\u7528 MinerU \u8bc6\u522b")==0,"Chinese menu title");
 CoTaskMemFree(text); text=NULL;
 hr=command->lpVtbl->GetIcon(command,NULL,&text);
 Check(SUCCEEDED(hr) && text && wcsstr(text,L"MinerURightClick.exe,0"),"icon comes from adjacent application");
 CoTaskMemFree(text);
 flags=1;
 Check(command->lpVtbl->GetFlags(command,&flags)==S_OK && flags==ECF_DEFAULT,"single menu command");
 state=ECS_ENABLED;
 Check(command->lpVtbl->GetState(command,NULL,FALSE,&state)==S_OK && state==ECS_HIDDEN,"no selection hidden");
 Check(command->lpVtbl->GetState(command,NULL,FALSE,NULL)==E_POINTER,"invalid state output rejected");
 Check(command->lpVtbl->GetCanonicalName(command,&canonical)==S_OK && memcmp(&canonical,&CLSID_MinerUCommand,sizeof(GUID))==0,"canonical command GUID");
 Check(command->lpVtbl->EnumSubCommands(command,&subcommands)==E_NOTIMPL && !subcommands,"no redundant submenu");
 GetTempPathW(MAX_PATH,temporaryPath);
 CoCreateGuid(&testId); StringFromGUID2(&testId,testIdText,40);
 swprintf(testDirectory,L"%sMinerUMenuTest-%s",temporaryPath,testIdText);
 hr=CreateDirectoryW(testDirectory,NULL);
 Check(hr != 0,"create isolated test directory");
 if(!hr) return 1;
 for(i=0;i<sizeof(extensions)/sizeof(extensions[0]);++i) {
  swprintf(path,L"%s\\sample.%s",testDirectory,extensions[i]);
  file=CreateFileW(path,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);
  if(file != INVALID_HANDLE_VALUE) CloseHandle(file);
  Check(FileState(command,path)==ECS_ENABLED,"supported file type enabled (case insensitive)");
  DeleteFileW(path);
 }
 swprintf(path,L"%s\\sample.txt",testDirectory);
 file=CreateFileW(path,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);
 if(file != INVALID_HANDLE_VALUE) CloseHandle(file);
 Check(FileState(command,path)==ECS_HIDDEN,"unsupported file type hidden"); DeleteFileW(path);
 swprintf(path,L"%s\\folder.pdf",testDirectory); CreateDirectoryW(path,NULL);
 Check(FileState(command,path)==ECS_HIDDEN,"directory named PDF hidden"); RemoveDirectoryW(path);
 swprintf(path1,L"%s\\one.pdf",testDirectory); swprintf(path2,L"%s\\two.pdf",testDirectory);
 file=CreateFileW(path1,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL); if(file != INVALID_HANDLE_VALUE) CloseHandle(file);
 file=CreateFileW(path2,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL); if(file != INVALID_HANDLE_VALUE) CloseHandle(file);
 SHParseDisplayName(path1,NULL,&pidls[0],0,NULL); SHParseDisplayName(path2,NULL,&pidls[1],0,NULL);
 hr=SHCreateShellItemArrayFromIDLists(2,(const void**)pidls,&multiple);
 Check(SUCCEEDED(hr) && multiple,"multiple selection construction");
 state=ECS_ENABLED;
 if(multiple) {
  Check(command->lpVtbl->GetState(command,multiple,FALSE,&state)==S_OK && state==ECS_HIDDEN,"multiple selection hidden");
  Check(command->lpVtbl->Invoke(command,multiple,NULL)==E_INVALIDARG,"multiple invoke rejected without launching");
  multiple->lpVtbl->Release(multiple);
 }
 CoTaskMemFree(pidls[0]); CoTaskMemFree(pidls[1]);
 DeleteFileW(path1); DeleteFileW(path2); RemoveDirectoryW(testDirectory);
 command->lpVtbl->Release(command); factory->lpVtbl->Release(factory);
 Check(canUnload()==S_OK,"all COM objects released and DLL unloadable");
 FreeLibrary(library); CoUninitialize(); printf("Failures: %d\n",failures);
 return failures ? 1 : 0;
}
