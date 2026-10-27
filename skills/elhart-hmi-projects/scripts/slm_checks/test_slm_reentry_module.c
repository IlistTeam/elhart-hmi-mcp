/* Model a runtime that reloads the macro image between invocations. */
#define gettimeofday demo_gettimeofday
#include "demo.c"
static long test_ms;
int demo_gettimeofday(struct timeval *tv,void *unused) {
 tv->tv_sec=test_ms/1000;tv->tv_usec=(test_ms%1000)*1000;return 0;
}
__declspec(dllexport) int demo_test_call(MacroOper callback,long milliseconds) {
 test_ms=milliseconds;return _macro_main(callback);
}
