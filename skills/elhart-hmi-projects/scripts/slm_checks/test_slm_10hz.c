#define gettimeofday demo_gettimeofday
#include "demo.c"
#include <assert.h>
static long test_ms=100000000;
int demo_gettimeofday(struct timeval *tv,void *ignored){tv->tv_sec=test_ms/1000;tv->tv_usec=(test_ms%1000)*1000;return 0;}
static unsigned short words[1024];static unsigned char bits[160];
static int callback(const char *cmd,void *params,int *err){
 int *p=(int*)params;void *data=(void*)p[0];const char *alias=(char*)p[2];void *memory;
 *err=0;
 if(!strcmp(alias,"0")){assert(p[3]==0&&p[4]==sizeof words);memory=words;}
 else {assert(!strcmp(alias,"1")&&p[3]>=0&&p[3]<160&&p[4]==1);memory=bits+p[3];}
 if(!strcmp(cmd,"GetMem"))memcpy(data,memory,p[4]);else memcpy(memory,data,p[4]);return 1;
}
static void call(void){assert(_macro_main(callback)==0);}
static void step(void){test_ms+=100;call();}
int main(void){int i,changes=0,last_o2;unsigned short session;
 call();bits[VALVETOGGLE]=1;call();bits[FAN]=1;call();assert(words[BLOWER]&&words[ST]==MANUAL);
 session=words[SESSION];last_o2=words[O2];
 for(i=1;i<=9;i++){
  step();assert(words[RUN]==0&&words[AGE]==0&&words[SESSION]==session);
  assert(((char*)(words+60))[6]=='0'+i);assert(strlen((char*)(words+60))==7);
  if(last_o2!=words[O2])changes++;last_o2=words[O2];
  call();call();assert(words[RUN]==0); /* Multiple windows/actions cannot accelerate elapsed time. */
 }
 assert(changes>=8);step();assert(words[RUN]==1&&words[AGE]==1&&words[SESSION]==session+1);
 assert(!strcmp((char*)(words+60),"00:01.0"));
 for(i=0;i<20;i++)call();assert(words[RUN]==1&&words[AGE]==1);
 bits[STOP]=1;call();assert(words[ST]==PURGE&&!words[GATE]);
 for(i=0;i<49;i++)step();assert(words[ST]==PURGE);step();assert(words[ST]==DONE&&!words[BLOWER]);
 assert(((char*)(words+60))[6]=='0');
 bits[START]=1;call();assert(words[ST]==CHECK);
 for(i=0;i<19;i++)step();assert(words[ST]==CHECK);step();assert(words[ST]==FLOW);
 for(i=0;i<39;i++)step();assert(words[ST]==FLOW);step();assert(words[ST]==LOAD);
 for(i=0;i<449;i++)step();assert(words[ST]==LOAD);step();assert(words[ST]==PURGE);
 for(i=0;i<49;i++)step();assert(words[ST]==PURGE);step();assert(words[ST]==DONE);
 puts("PASS: 100 ms O2 sampling / tenths display, 10 calls = 1 elapsed second, duplicate callbacks do not speed time, CHECK 2s / FLOW 4s / LOAD 45s / PURGE 5s unchanged.");return 0;
}
