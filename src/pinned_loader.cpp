#include <windows.h>
#include <bcrypt.h>
#include <string>
#include <cstdio>
#include <vector>
static_assert(sizeof(void*)==8, "x64 required");
static std::wstring directory(){
 HMODULE self=nullptr; wchar_t path[32768]{};
 GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)&directory,&self);
 DWORD n=GetModuleFileNameW(self,path,32768); if(!n||n>=32768)return {};
 std::wstring p(path,n); return p.substr(0,p.find_last_of(L"\\")+1);
}
static void report(const wchar_t* path,DWORD error,bool verified){
 auto file=directory()+L"30HX-loader.log"; HANDLE h=CreateFileW(file.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(h==INVALID_HANDLE_VALUE)return; SYSTEMTIME t{};GetLocalTime(&t);char b[256];
 int n=snprintf(b,sizeof b,"%04u-%02u-%02u %02u:%02u:%02u pid=%lu pinnedSHA256=%s loadError=0x%08lx\r\n",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,GetCurrentProcessId(),verified?"match":"rejected",error);
 DWORD written;WriteFile(h,b,n,&written,nullptr);CloseHandle(h);
}
extern "C" __declspec(dllexport) HMODULE LoadPinnedCore(const wchar_t* path,DWORD flags,BOOL){
 const auto expectedPath=directory()+L"_nvngx.dll"; wchar_t absolute[32768]{};
 if(!path||!GetFullPathNameW(path,32768,absolute,nullptr)||_wcsicmp(absolute,expectedPath.c_str())){report(path,ERROR_ACCESS_DENIED,false);SetLastError(ERROR_ACCESS_DENIED);return nullptr;}
 HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(file==INVALID_HANDLE_VALUE)return nullptr;
 BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;unsigned char digest[32]{};bool ok=false;
 if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0&&BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0){
  std::vector<unsigned char> all; unsigned char buffer[65536];DWORD got=0;bool readOk=true;
  for(;;){if(!ReadFile(file,buffer,sizeof buffer,&got,nullptr)){readOk=false;break;}if(!got)break;all.insert(all.end(),buffer,buffer+got);if(BCryptHashData(hash,buffer,got,0)<0){readOk=false;break;}}
  // Accept a patched NGX core from any driver generation. The patcher emits one
  // of these two signatures for every hardware rejection branch.
  int signatures=0;
  for(size_t i=0;i+8<=all.size();i++){
   bool legacy=all[i]==0x84&&all[i+1]==0xc0&&all[i+2]==0xe9&&all[i+7]==0x90;
   bool modern=all[i]==0x84&&all[i+1]==0xc0&&all[i+2]==0x90&&all[i+3]==0x90&&all[i+7]==0x90;
   if(legacy||modern)signatures++;
  }
  ok=readOk&&BCryptFinishHash(hash,digest,32,0)>=0&&signatures>=3;
 }
 if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);
 HMODULE result=nullptr;DWORD error=ERROR_INVALID_IMAGE_HASH;
 if(ok){SetLastError(0);result=LoadLibraryExW(path,nullptr,flags&0xffffe8f7);error=result?0:GetLastError();}
 CloseHandle(file);report(path,error,ok);SetLastError(error);return result;
}
