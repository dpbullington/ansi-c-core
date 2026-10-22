#ifndef _STDIO_H
#define _STDIO_H
#include <stddef.h>

typedef struct _IO_FILE FILE;

#define stdin  ((FILE *)3)
#define stdout ((FILE *)1)
#define stderr ((FILE *)2)
#define EOF (-1)
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2

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
int sscanf(const char *s, const char *format, ...);
int getc(FILE *stream);
int putc(int c, FILE *stream);
int ungetc(int c, FILE *stream);
int feof(FILE *stream);
int ferror(FILE *stream);
void clearerr(FILE *stream);
int remove(const char *path);
int rename(const char *from, const char *to);
FILE *fopen(const char *path, const char *mode);
int fclose(FILE *stream);
size_t fread(void *ptr, size_t size, size_t count, FILE *stream);
size_t fwrite(const void *ptr, size_t size, size_t count, FILE *stream);
int fseek(FILE *stream, long offset, int whence);
long ftell(FILE *stream);
void rewind(FILE *stream);
#endif
