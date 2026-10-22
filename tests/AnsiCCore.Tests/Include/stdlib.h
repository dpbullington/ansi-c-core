#ifndef _STDLIB_H
#define _STDLIB_H
#include <stddef.h>
void *malloc(size_t size);
void *calloc(size_t count, size_t size);
void *realloc(void *ptr, size_t size);
void free(void *ptr);
typedef struct { int quot; int rem; } div_t;
typedef struct { long quot; long rem; } ldiv_t;

int abs(int n);
long labs(long n);
div_t div(int numer, int denom);
ldiv_t ldiv(long numer, long denom);
char *getenv(const char *name);
int system(const char *command);
int atoi(const char *s);
long atol(const char *s);
double atof(const char *s);
long strtol(const char *nptr, char **endptr, int base);
unsigned long strtoul(const char *nptr, char **endptr, int base);
double strtod(const char *nptr, char **endptr);

int rand(void);
void srand(unsigned int seed);
#define RAND_MAX 32767

void exit(int status);
void abort(void);
int atexit(void (*func)(void));
#define EXIT_SUCCESS 0
#define EXIT_FAILURE 1
#endif
