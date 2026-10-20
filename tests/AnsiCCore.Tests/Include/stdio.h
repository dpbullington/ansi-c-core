#ifndef _STDIO_H
#define _STDIO_H
#include <stddef.h>

typedef struct _IO_FILE FILE;

#define stdin  ((FILE *)3)
#define stdout ((FILE *)1)
#define stderr ((FILE *)2)
#define EOF (-1)

int printf(const char *format, ...);
int fprintf(FILE *stream, const char *format, ...);
int sprintf(char *buffer, const char *format, ...);
int putchar(int c);
int puts(const char *s);
int fputc(int c, FILE *stream);
int fputs(const char *s, FILE *stream);
int getchar(void);
int fgetc(FILE *stream);
char *fgets(char *buffer, int size, FILE *stream);
int scanf(const char *format, ...);
int fscanf(FILE *stream, const char *format, ...);
FILE *fopen(const char *path, const char *mode);
int fclose(FILE *stream);
#endif
