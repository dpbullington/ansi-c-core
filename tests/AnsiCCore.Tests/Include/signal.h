#ifndef _SIGNAL_H
#define _SIGNAL_H

typedef int sig_atomic_t;

#define SIGABRT 6
#define SIGFPE  8
#define SIGILL  4
#define SIGINT  2
#define SIGSEGV 11
#define SIGTERM 15

/* SIG_DFL is the null handler; SIG_IGN/SIG_ERR are runtime sentinel handlers (compared by identity). */
void (*__sig_ign(void))(int);
void (*__sig_err(void))(int);
#define SIG_DFL ((void (*)(int))0)
#define SIG_IGN __sig_ign()
#define SIG_ERR __sig_err()

void (*signal(int sig, void (*handler)(int)))(int);
int raise(int sig);

#endif
