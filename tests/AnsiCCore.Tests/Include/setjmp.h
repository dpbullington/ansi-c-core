#ifndef _SETJMP_H
#define _SETJMP_H

/* jmp_buf holds a single token identifying the setjmp activation. */
typedef long jmp_buf[1];

/* setjmp is recognized by the transpiler (it must "return twice"); it is supported as the
   controlling expression of an if or switch statement. longjmp is an ordinary runtime call. */
int setjmp(jmp_buf env);
void longjmp(jmp_buf env, int val);

#endif
