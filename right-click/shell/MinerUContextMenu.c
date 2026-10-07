#include "ShellAbi.h"

static HMODULE g_module;
static volatile LONG g_objects;
static volatile LONG g_locks;
static const ExplorerCommandVtbl command_vtable;
static const CommandFactoryVtbl factory_vtable;

static int EqualGuid(const GUID* a,const GUID* b) { return memcmp(a,b,sizeof(GUID)) == 0; }
static HRESULT CopyString(const wchar_t* text,wchar_t** result) {
 size_t size;
 if (!result) return E_POINTER;
 *result = NULL;
 size = (wcslen(text)+1)*sizeof(wchar_t);
 *result = CoTaskMemAlloc(size);
 if (!*result) return E_OUTOFMEMORY;
 memcpy(*result,text,size);
 return S_OK;
}
static int SupportedPath(const wchar_t* path) {
 const wchar_t* dot;
 const wchar_t* slash;
 const wchar_t* supported[] = {L".pdf",L".doc",L".docx",L".ppt",L".pptx",L".xls",L".xlsx",L".png",L".jpg",L".jpeg",L".jp2",L".webp",L".gif",L".bmp"};
 size_t i;
 if (!path) return 0;
 dot=wcsrchr(path,L'.'); slash=wcsrchr(path,L'\\');
 if (!dot || (slash && dot < slash)) return 0;
 for(i=0;i<sizeof(supported)/sizeof(supported[0]);++i) if(_wcsicmp(dot,supported[i]) == 0) return 1;
 return 0;
}
static HRESULT SelectedFile(ShellItemArray* items,wchar_t** path) {
 DWORD count=0,attributes=0;
 ShellItem* item=NULL;
 HRESULT hr;
 if(!path) return E_POINTER;
 *path=NULL;
 if(!items) return E_INVALIDARG;
 hr=items->lpVtbl->GetCount(items,&count);
 if(FAILED(hr)) return hr;
 if(count != 1) return E_INVALIDARG;
 hr=items->lpVtbl->GetItemAt(items,0,&item);
 if(FAILED(hr)) return hr;
 hr=item->lpVtbl->GetAttributes(item,SFGAO_FILESYSTEM|SFGAO_FOLDER,&attributes);
 if(SUCCEEDED(hr) && (!(attributes & SFGAO_FILESYSTEM) || (attributes & SFGAO_FOLDER))) hr=E_INVALIDARG;
 if(SUCCEEDED(hr)) hr=item->lpVtbl->GetDisplayName(item,SIGDN_FILESYSPATH,path);
 item->lpVtbl->Release(item);
 if(SUCCEEDED(hr) && !SupportedPath(*path)) { CoTaskMemFree(*path); *path=NULL; hr=E_INVALIDARG; }
 return hr;
}
static HRESULT ApplicationPath(wchar_t* output,DWORD capacity) {
 DWORD length=GetModuleFileNameW(g_module,output,capacity);
 wchar_t* last;
 const wchar_t* exe=L"MinerURightClick.exe";
 if(!length || length>=capacity) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
 last=wcsrchr(output,L'\\');
 if(!last) return E_FAIL;
 *++last=L'\0';
 if(wcslen(output)+wcslen(exe)>=capacity) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
 wcscat(output,exe);
 return S_OK;
}
static wchar_t* QuotedArgument(const wchar_t* value) {
 size_t length=wcslen(value),slashes=0,i,j;
 wchar_t* output=CoTaskMemAlloc((2*length+3)*sizeof(wchar_t));
 wchar_t* cursor=output;
 if(!output) return NULL;
 *cursor++=L'"';
 for(i=0;i<length;++i) {
  if(value[i]==L'\\') { ++slashes; continue; }
  if(value[i]==L'"') { for(j=0;j<2*slashes+1;++j) *cursor++=L'\\'; }
  else { for(j=0;j<slashes;++j) *cursor++=L'\\'; }
  slashes=0; *cursor++=value[i];
 }
 for(j=0;j<2*slashes;++j) *cursor++=L'\\';
 *cursor++=L'"'; *cursor=L'\0';
 return output;
}
static ULONG WINAPI CommandAddRef(ExplorerCommand* self) { return InterlockedIncrement(&self->references); }
static ULONG WINAPI CommandRelease(ExplorerCommand* self) {
 ULONG refs=InterlockedDecrement(&self->references);
 if(!refs) { free(self); InterlockedDecrement(&g_objects); }
 return refs;
}
static HRESULT WINAPI CommandQueryInterface(ExplorerCommand* self,REFIID iid,void** value) {
 if(!value) return E_POINTER;
 *value=NULL;
 if(EqualGuid(iid,&IID_Unknown) || EqualGuid(iid,&IID_Command)) { *value=self; CommandAddRef(self); return S_OK; }
 return E_NOINTERFACE;
}
static HRESULT WINAPI CommandGetTitle(ExplorerCommand* self,ShellItemArray* items,wchar_t** title) {
 return CopyString(L"\u7528 MinerU \u8bc6\u522b",title);
}
static HRESULT WINAPI CommandGetIcon(ExplorerCommand* self,ShellItemArray* items,wchar_t** icon) {
 wchar_t* path;
 HRESULT hr;
 if(!icon) return E_POINTER;
 *icon=NULL;
 path=CoTaskMemAlloc(32768*sizeof(wchar_t));
 if(!path) return E_OUTOFMEMORY;
 hr=ApplicationPath(path,32766);
 if(SUCCEEDED(hr)) { wcscat(path,L",0"); *icon=path; } else CoTaskMemFree(path);
 return hr;
}
static HRESULT WINAPI CommandGetToolTip(ExplorerCommand* self,ShellItemArray* items,wchar_t** tooltip) {
 return CopyString(L"\u8bc6\u522b\u9009\u4e2d\u6587\u4ef6\uff0c\u7ed3\u679c\u4fdd\u5b58\u5230\u539f\u6587\u4ef6\u65c1",tooltip);
}
static HRESULT WINAPI CommandGetCanonicalName(ExplorerCommand* self,GUID* value) {
 if(!value) return E_POINTER;
 *value=CLSID_MinerUCommand; return S_OK;
}
static HRESULT WINAPI CommandGetState(ExplorerCommand* self,ShellItemArray* items,BOOL slow,DWORD* state) {
 wchar_t* path=NULL;
 if(!state) return E_POINTER;
 *state=ECS_HIDDEN;
 if(SUCCEEDED(SelectedFile(items,&path))) { *state=ECS_ENABLED; CoTaskMemFree(path); }
 return S_OK;
}
static HRESULT WINAPI CommandInvoke(ExplorerCommand* self,ShellItemArray* items,void* binding) {
 wchar_t* path=NULL;
 wchar_t* exe=NULL;
 wchar_t* quotedExe=NULL;
 wchar_t* quotedPath=NULL;
 wchar_t* commandLine=NULL;
 HRESULT hr=SelectedFile(items,&path);
 if(FAILED(hr)) return hr;
 exe=CoTaskMemAlloc(32768*sizeof(wchar_t));
 if(!exe) { CoTaskMemFree(path); return E_OUTOFMEMORY; }
 hr=ApplicationPath(exe,32768);
 if(SUCCEEDED(hr)) {
  quotedExe=QuotedArgument(exe); quotedPath=QuotedArgument(path);
  if(!quotedExe || !quotedPath) hr=E_OUTOFMEMORY;
 }
 if(SUCCEEDED(hr)) {
  size_t size=wcslen(quotedExe)+wcslen(quotedPath)+2;
  if(size>32767) hr=HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
  else {
   commandLine=CoTaskMemAlloc(size*sizeof(wchar_t));
   if(!commandLine) hr=E_OUTOFMEMORY;
   else { wcscpy(commandLine,quotedExe); wcscat(commandLine,L" "); wcscat(commandLine,quotedPath); }
  }
 }
 if(SUCCEEDED(hr)) {
  STARTUPINFOW start;
  PROCESS_INFORMATION process;
  memset(&start,0,sizeof(start)); memset(&process,0,sizeof(process)); start.cb=sizeof(start);
  /* Explicit lpApplicationName prevents any executable-path ambiguity. No shell is involved. */
  if(!CreateProcessW(exe,commandLine,NULL,NULL,FALSE,0,NULL,NULL,&start,&process)) hr=HRESULT_FROM_WIN32(GetLastError());
  else { CloseHandle(process.hThread); CloseHandle(process.hProcess); }
 }
 CoTaskMemFree(commandLine); CoTaskMemFree(quotedExe); CoTaskMemFree(quotedPath); CoTaskMemFree(exe); CoTaskMemFree(path);
 return hr;
}
static HRESULT WINAPI CommandGetFlags(ExplorerCommand* self,DWORD* flags) {
 if(!flags) return E_POINTER;
 *flags=ECF_DEFAULT; return S_OK;
}
static HRESULT WINAPI CommandEnumSubCommands(ExplorerCommand* self,void** commands) {
 if(!commands) return E_POINTER;
 *commands=NULL; return E_NOTIMPL;
}
static const ExplorerCommandVtbl command_vtable={CommandQueryInterface,CommandAddRef,CommandRelease,
 CommandGetTitle,CommandGetIcon,CommandGetToolTip,CommandGetCanonicalName,CommandGetState,CommandInvoke,
 CommandGetFlags,CommandEnumSubCommands};
static ULONG WINAPI FactoryAddRef(CommandFactory* self) { return InterlockedIncrement(&self->references); }
static ULONG WINAPI FactoryRelease(CommandFactory* self) {
 ULONG refs=InterlockedDecrement(&self->references);
 if(!refs) { free(self); InterlockedDecrement(&g_objects); }
 return refs;
}
static HRESULT WINAPI FactoryQueryInterface(CommandFactory* self,REFIID iid,void** value) {
 if(!value) return E_POINTER;
 *value=NULL;
 if(EqualGuid(iid,&IID_Unknown) || EqualGuid(iid,&IID_Factory)) { *value=self; FactoryAddRef(self); return S_OK; }
 return E_NOINTERFACE;
}
static HRESULT WINAPI FactoryCreateInstance(CommandFactory* self,void* outer,REFIID iid,void** value) {
 ExplorerCommand* command;
 HRESULT hr;
 if(!value) return E_POINTER;
 *value=NULL;
 if(outer) return CLASS_E_NOAGGREGATION;
 command=calloc(1,sizeof(ExplorerCommand));
 if(!command) return E_OUTOFMEMORY;
 command->lpVtbl=&command_vtable; command->references=1; InterlockedIncrement(&g_objects);
 hr=CommandQueryInterface(command,iid,value); CommandRelease(command); return hr;
}
static HRESULT WINAPI FactoryLockServer(CommandFactory* self,BOOL lock) {
 if(lock) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks); return S_OK;
}
static const CommandFactoryVtbl factory_vtable={FactoryQueryInterface,FactoryAddRef,FactoryRelease,FactoryCreateInstance,FactoryLockServer};
__declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** value) {
 CommandFactory* factory;
 HRESULT hr;
 if(!value) return E_POINTER;
 *value=NULL;
 if(!EqualGuid(clsid,&CLSID_MinerUCommand)) return CLASS_E_CLASSNOTAVAILABLE;
 factory=calloc(1,sizeof(CommandFactory));
 if(!factory) return E_OUTOFMEMORY;
 factory->lpVtbl=&factory_vtable; factory->references=1; InterlockedIncrement(&g_objects);
 hr=FactoryQueryInterface(factory,iid,value); FactoryRelease(factory); return hr;
}
__declspec(dllexport) HRESULT WINAPI DllCanUnloadNow(void) { return g_objects==0 && g_locks==0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved) {
 if(reason==DLL_PROCESS_ATTACH) { g_module=instance; DisableThreadLibraryCalls(instance); }
 return TRUE;
}
