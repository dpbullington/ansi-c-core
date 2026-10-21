#ifndef _STDARG_H
#define _STDARG_H

/* A va_list is just a cursor into the hidden promoted-argument region the caller builds. */
typedef void *va_list;

/* Intrinsics the transpiler recognizes: __va_start() yields the current frame's argument base
   (resolved by the emitter), __va_arg advances the cursor and returns the current slot. */
void *__va_start(void);
void *__va_arg(void **__ap);

/* Fixed-arity function-like macros (C89 forbids variadic macros, so none are used here). The
   `type` token rides inside an ordinary cast, so no special parsing is required. */
#define va_start(ap, last) ((ap) = (va_list)__va_start())
#define va_arg(ap, type)   (*(type *)__va_arg((void **)&(ap)))
#define va_end(ap)         ((ap) = (va_list)0)

#endif
