#ifndef _TIME_H
#define _TIME_H
typedef long time_t;
typedef long clock_t;
#define CLOCKS_PER_SEC 1000
time_t time(time_t *t);
clock_t clock(void);
double difftime(time_t end, time_t start);
#endif
