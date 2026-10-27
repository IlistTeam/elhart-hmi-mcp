#define DEMO_TEST
#include "demo.c"
#include <assert.h>
static unsigned short w[1024];static unsigned char b[160];
static void resetdemo(void){memset(w,0,sizeof w);memset(b,0,sizeof b);logic(w,b,0);}
static void press(int command){b[command]=1;logic(w,b,0);}
static void ticks(int n){while(n--)logic(w,b,1);}
int main(void){
 resetdemo();assert(w[O2]==18&&w[SAVED+1]==35&&b[108]);
 press(START);assert(w[ST]==CHECK&&w[VALVE]);ticks(2);assert(w[ST]==FLOW&&w[BLOWER]);
 ticks(4);assert(w[ST]==LOAD&&w[GATE]==35&&w[PT]==8);
 assert(w[80]==2&&w[81]==2&&w[82]==1&&w[83]==0);
 assert(strlen((char*)(w+72))==10);
 assert(strlen((char*)(w+DISPLAY))==8);
 ticks(3);press(CLEAN);assert(w[ST]==LOAD&&w[NOTICE]==5);
 press(STOP);assert(w[ST]==PURGE&&w[GATE]==0&&w[BLOWER]);ticks(5);
 assert(w[ST]==DONE&&!w[BLOWER]&&!w[VALVE]&&w[LOGCOUNT]>=7);
 assert(w[80]==2&&w[81]==2&&w[82]==2&&w[83]==2);
 press(CLEAN);assert(w[ST]==CLEANING&&w[FILTER]);ticks(3);assert(w[ST]==READY&&!w[FILTER]);
 press(CLEAN);press(STOP);assert(w[ST]==READY&&!w[FILTER]);
 resetdemo();press(PLUS);assert(w[GATE]==0&&w[NOTICE]==4);
 press(FAN);assert(!w[BLOWER]&&w[NOTICE]==2);
 press(VALVETOGGLE);press(FAN);assert(w[ST]==MANUAL&&w[BLOWER]);
 press(PLUS);assert(w[GATE]==0);ticks(3);press(PLUS);assert(w[GATE]==5);
 press(VALVETOGGLE);assert(w[VALVE]&&w[NOTICE]==3);press(MINUS);assert(w[GATE]==0);
 press(STOP);ticks(5);assert(!w[BLOWER]&&!w[VALVE]);
 w[DRAFT+1]=55;press(CANCELCFG);assert(w[DRAFT+1]==35);
 w[DRAFT+1]=65;w[DRAFT+3]=90;press(SAVECFG);assert(w[SAVED+1]==65&&w[SAVED+3]==50);
 press(MATERIALCHANGE);press(GASCHANGE);assert(w[DRAFT+6]&&w[DRAFT]==0);
 press(CALOPEN);press(CALNEXT);press(CALNEXT);press(CALNEXT);press(CALNEXT);assert(w[CALSTEP]==5&&w[CALRESULT]);
 {unsigned short saved_log[96];memcpy(saved_log,w+RING,sizeof saved_log);
  press(CALNEXT);press(CALNEXT);press(CALNEXT);
  assert(w[CALSTEP]==5&&w[CALRESULT]&&!memcmp(saved_log,w+RING,sizeof saved_log));}
 resetdemo();press(START);ticks(6);press(DEMOFAULT);assert(w[ST]==TRIPPED&&!w[BLOWER]&&!w[GATE]);
 press(ACK);assert(w[FAULT]&&w[ST]==TRIPPED);press(START);assert(w[ST]==TRIPPED);
 press(DEMOFAULT);ticks(1);press(START);assert(w[ST]==CHECK);
 press(EMERGENCY);assert(!w[ESTOP]&&!w[BLOWER]&&!w[VALVE]);press(ACK);assert(!w[ESTOP]);
 press(EMERGENCY);assert(w[ESTOP]);press(WARN);assert(w[FILTERSEL]==1);
 resetdemo();press(START);ticks(56);assert(w[ST]==DONE&&!w[BLOWER]&&w[GATE]==0);
 resetdemo();w[SAVED+2]=20;press(START);ticks(14);assert(w[ST]==LOAD&&w[PT]>=20);
 printf("PASS: auto cycle, STOP purge, manual interlocks, cleaning, settings limits/cancel, titanium gas, calibration, O2 fault, emergency, acknowledgement, journal filter.\n");
 return 0;
}
