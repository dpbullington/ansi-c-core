#ifndef _STDIO_H
#define _STDIO_H
#include <stddef.h>
#include <stdarg.h>

typedef struct _IO_FILE FILE;

#define stdin  ((FILE *)3)
#define stdout ((FILE *)1)
#define stderr ((FILE *)2)
#define EOF (-1)
#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2
#define BUFSIZ 8192
#define FOPEN_MAX 16
#define FILENAME_MAX 4096
#define TMP_MAX 238328
#define L_tmpnam 256
#define _IOFBF 0
#define _IOLBF 1
#define _IONBF 2

typedef long fpos_t;

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
int vprintf(const char *format, va_list ap);
int vfprintf(FILE *stream, const char *format, va_list ap);
int vsprintf(char *buffer, const char *format, va_list ap);
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
int fflush(FILE *stream);
FILE *freopen(const char *path, const char *mode, FILE *stream);
void setbuf(FILE *stream, char *buf);
int setvbuf(FILE *stream, char *buf, int mode, size_t size);
FILE *tmpfile(void);
char *tmpnam(char *s);
int fgetpos(FILE *stream, fpos_t *pos);
int fsetpos(FILE *stream, const fpos_t *pos);
void perror(const char *s);
#endif
