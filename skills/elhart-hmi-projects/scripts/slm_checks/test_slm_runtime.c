#include "demo.c"
#include <assert.h>
static unsigned short words[1024];static unsigned char bits[160];static int injectValvePress=0;
static int callback(const char *cmd,void *params,int *err){
 int *p=(int*)params;void *data=(void*)p[0];const char *alias=(char*)p[2];void *memory;
 *err=0;
 if(!strcmp(alias,"0")){assert(p[3]==0&&p[4]==sizeof words);memory=words;}
 else {assert(!strcmp(alias,"1")&&p[3]>=0&&p[3]<160&&p[4]==1);memory=bits+p[3];}
 if(!strcmp(cmd,"GetMem"))memcpy(data,memory,p[4]);
 else {assert(!strcmp(cmd,"SetMem"));memcpy(memory,data,p[4]);
  if(memory==words&&injectValvePress){bits[VALVETOGGLE]=1;injectValvePress=0;}
 }
 return 1;
}
int main(void){FILE *f;char header[128];
 assert(_macro_main(callback)==0&&words[MAGIC]==17341);
 injectValvePress=1;_macro_main(callback);
 assert(bits[VALVETOGGLE]==1); /* Press arriving during display publication must survive. */
 _macro_main(callback);assert(words[VALVE]==1&&!bits[VALVETOGGLE]);
 bits[VALVETOGGLE]=1;_macro_main(callback);assert(words[VALVE]==0);
 bits[VALVETOGGLE]=1;_macro_main(callback);
 assert(words[VALVE]==1&&bits[101]==1&&!bits[VALVETOGGLE]);
 _macro_main(callback);assert(words[VALVE]==1); /* A timer refresh must not toggle again. */
 bits[VALVETOGGLE]=1;_macro_main(callback);
 assert(words[VALVE]==0&&bits[101]==0&&!bits[VALVETOGGLE]);
 bits[VALVETOGGLE]=1;_macro_main(callback);bits[FAN]=1;_macro_main(callback);
 assert(words[BLOWER]==1&&words[ST]==MANUAL);
 bits[VALVETOGGLE]=1;_macro_main(callback);assert(words[VALVE]==1&&words[NOTICE]==3);
 bits[STOP]=1;_macro_main(callback); /* Purge must remain interlocked. */
 memset(words,0,sizeof words);memset(bits,0,sizeof bits);_macro_main(callback);
 bits[START]=1;_macro_main(callback);assert(words[ST]==CHECK&&!bits[START]);
 bits[STOP]=1;_macro_main(callback);assert(words[ST]==READY&&!bits[STOP]);
 bits[EXPORT]=1;_macro_main(callback);assert(words[NOTICE]==13&&!bits[EXPORT]);
 f=fopen("SLM_demo_events.csv","rb");assert(f);assert(fgets(header,sizeof header,f));fclose(f);
 assert(!strncmp(header,"session_seconds,event_code,severity,source",41));
 remove("SLM_demo_events.csv");
 puts("PASS: HMI callback ABI, native local block read/write, individual bit offsets, queued press preservation, manual valve open/close, blower interlock, immediate commands, command reset, actual CSV export.");
 return 0;
}
