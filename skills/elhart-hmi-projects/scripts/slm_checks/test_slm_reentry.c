#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>
typedef int (*MacroOper)(const char*,void*,int*);
typedef int (*TestCall)(MacroOper,long);
static unsigned short words[1024];static unsigned char bits[160];
static long test_ms=100000000;
static int callback(const char *cmd,void *params,int *err) {
 int *p=(int*)params;void *data=(void*)p[0];void *memory;
 *err=0;
 if(!strcmp((char*)p[2],"0")){assert(p[3]==0&&p[4]==sizeof words);memory=words;}
 else {assert(!strcmp((char*)p[2],"1")&&p[3]>=0&&p[3]<160&&p[4]==1);memory=bits+p[3];}
 if(!strcmp(cmd,"GetMem"))memcpy(data,memory,p[4]);
 else {assert(!strcmp(cmd,"SetMem"));memcpy(memory,data,p[4]);}
 return 1;
}
static void call(void) {
 HMODULE module=LoadLibraryA("reentry_module.dll");TestCall entry;
 if(!module){fprintf(stderr,"LoadLibrary failed: %lu\n",GetLastError());exit(2);}
 entry=(TestCall)GetProcAddress(module,"demo_test_call");assert(entry);
 assert(entry(callback,test_ms)==0);assert(FreeLibrary(module));
}
static void step(void) {test_ms+=100;call();}
int main(void) {
 int i;unsigned short session;
 call();bits[4]=1;call();bits[3]=1;call(); /* Open printer gate; start blower. */
 assert(words[9]&&words[1]==8);session=words[20];
 for(i=1;i<=9;i++) {
  step();assert(words[3]==0&&words[2]==0&&words[20]==session);
  call();call();assert(words[3]==0); /* Recreated contexts share one clock. */
 }
 step();assert(words[3]==1&&words[2]==1&&words[20]==session+1);
 assert(!strcmp((char*)(words+60),"00:01.0"));
 for(i=0;i<20;i++)call();assert(words[3]==1);
 bits[1]=1;call();assert(words[1]==4&&!words[8]);
 for(i=0;i<49;i++)step();assert(words[1]==4);step();assert(words[1]==5&&!words[9]);
 bits[0]=1;call();assert(words[1]==1);
 for(i=0;i<19;i++)step();assert(words[1]==1);step();assert(words[1]==2);
 for(i=0;i<39;i++)step();assert(words[1]==2);step();assert(words[1]==3);
 for(i=0;i<449;i++)step();assert(words[1]==3);step();assert(words[1]==4);
 for(i=0;i<49;i++)step();assert(words[1]==4);step();assert(words[1]==5&&!words[9]&&!words[8]);
 puts("PASS: DLL reloaded every invocation; 10 x 100 ms = 1 second; duplicate contexts do not accelerate time; full cycle 56 seconds.");
 return 0;
}
