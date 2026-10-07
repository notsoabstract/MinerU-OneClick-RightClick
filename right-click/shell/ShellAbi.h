#ifndef MINERU_SHELL_ABI_H
#define MINERU_SHELL_ABI_H
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

/* Public Win32 COM ABI, verified against Microsoft's ShObjIdl_core.h.
   Only methods needed by this small command are declared; unused vtable
   slots remain pointers, preserving their documented position and size. */
typedef struct ShellItem ShellItem;
typedef struct ShellItemArray ShellItemArray;
typedef struct ExplorerCommand ExplorerCommand;
typedef struct CommandFactory CommandFactory;
typedef struct ShellItemVtbl {
 HRESULT (WINAPI *QueryInterface)(ShellItem*,REFIID,void**);
 ULONG (WINAPI *AddRef)(ShellItem*);
 ULONG (WINAPI *Release)(ShellItem*);
 void* BindToHandler;
 void* GetParent;
 HRESULT (WINAPI *GetDisplayName)(ShellItem*,DWORD,wchar_t**);
 HRESULT (WINAPI *GetAttributes)(ShellItem*,DWORD,DWORD*);
 void* Compare;
} ShellItemVtbl;
struct ShellItem { const ShellItemVtbl* lpVtbl; };
typedef struct ShellItemArrayVtbl {
 HRESULT (WINAPI *QueryInterface)(ShellItemArray*,REFIID,void**);
 ULONG (WINAPI *AddRef)(ShellItemArray*);
 ULONG (WINAPI *Release)(ShellItemArray*);
 void* BindToHandler;
 void* GetPropertyStore;
 void* GetPropertyDescriptionList;
 void* GetAttributes;
 HRESULT (WINAPI *GetCount)(ShellItemArray*,DWORD*);
 HRESULT (WINAPI *GetItemAt)(ShellItemArray*,DWORD,ShellItem**);
 void* EnumItems;
} ShellItemArrayVtbl;
struct ShellItemArray { const ShellItemArrayVtbl* lpVtbl; };
typedef struct ExplorerCommandVtbl {
 HRESULT (WINAPI *QueryInterface)(ExplorerCommand*,REFIID,void**);
 ULONG (WINAPI *AddRef)(ExplorerCommand*);
 ULONG (WINAPI *Release)(ExplorerCommand*);
 HRESULT (WINAPI *GetTitle)(ExplorerCommand*,ShellItemArray*,wchar_t**);
 HRESULT (WINAPI *GetIcon)(ExplorerCommand*,ShellItemArray*,wchar_t**);
 HRESULT (WINAPI *GetToolTip)(ExplorerCommand*,ShellItemArray*,wchar_t**);
 HRESULT (WINAPI *GetCanonicalName)(ExplorerCommand*,GUID*);
 HRESULT (WINAPI *GetState)(ExplorerCommand*,ShellItemArray*,BOOL,DWORD*);
 HRESULT (WINAPI *Invoke)(ExplorerCommand*,ShellItemArray*,void*);
 HRESULT (WINAPI *GetFlags)(ExplorerCommand*,DWORD*);
 HRESULT (WINAPI *EnumSubCommands)(ExplorerCommand*,void**);
} ExplorerCommandVtbl;
struct ExplorerCommand { const ExplorerCommandVtbl* lpVtbl; volatile LONG references; };
typedef struct CommandFactoryVtbl {
 HRESULT (WINAPI *QueryInterface)(CommandFactory*,REFIID,void**);
 ULONG (WINAPI *AddRef)(CommandFactory*);
 ULONG (WINAPI *Release)(CommandFactory*);
 HRESULT (WINAPI *CreateInstance)(CommandFactory*,void*,REFIID,void**);
 HRESULT (WINAPI *LockServer)(CommandFactory*,BOOL);
} CommandFactoryVtbl;
struct CommandFactory { const CommandFactoryVtbl* lpVtbl; volatile LONG references; };

#define ECS_ENABLED 0
#define ECS_HIDDEN 2
#define ECF_DEFAULT 0
#define SFGAO_FILESYSTEM 0x40000000
#define SFGAO_FOLDER 0x20000000
#define SIGDN_FILESYSPATH 0x80058000
static const GUID CLSID_MinerUCommand = {0xa118b38a,0x7d91,0x4db1,{0xa0,0xa5,0x87,0xc6,0x24,0xf1,0x1b,0x79}};
static const GUID IID_Unknown = {0,0,0,{0xc0,0,0,0,0,0,0,0x46}};
static const GUID IID_Factory = {1,0,0,{0xc0,0,0,0,0,0,0,0x46}};
static const GUID IID_Command = {0xa08ce4d0,0xfa25,0x44ab,{0xb5,0x7c,0xc7,0xb1,0xc3,0x23,0xe0,0xb9}};
static const GUID IID_Item = {0x43826d1e,0xe718,0x42ee,{0xbc,0x55,0xa1,0xe2,0x61,0xc3,0x7b,0xfe}};
static const GUID IID_ItemArray = {0xb63ea76d,0x1f85,0x456f,{0xa1,0x9c,0x48,0x15,0x9e,0xfa,0x85,0x8b}};

__declspec(dllimport) void* WINAPI CoTaskMemAlloc(SIZE_T);
__declspec(dllimport) void WINAPI CoTaskMemFree(void*);
#endif
