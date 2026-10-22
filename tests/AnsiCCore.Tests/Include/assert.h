/* <assert.h> deliberately has no macro include guard: the assert macro is redefined on every
   inclusion according to NDEBUG at that point (C89 §7.2). */

#undef assert

#ifdef NDEBUG
#define assert(ignore) ((void)0)
#else
#define assert(e) ((void)((e) ? 0 : __assert_fail(#e, __FILE__, __LINE__)))
#endif

#ifndef _ASSERT_H_DECLS
#define _ASSERT_H_DECLS
int __assert_fail(const char *expr, const char *file, int line);
#endif
