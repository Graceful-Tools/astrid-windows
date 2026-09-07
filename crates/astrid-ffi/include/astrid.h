/*
 * astrid.h — the whole boundary between the WinUI shell and the Rust core.
 *
 * Seven functions and one callback type. Everything the app can do goes through astrid_call as a
 * JSON command, so adding a feature means adding a Command variant in astrid-core rather than a
 * symbol here, a DllImport in C# and a marshalling rule for whatever it returns.
 *
 * Hand-written rather than generated, because it is short enough to read in one sitting and a
 * generated header is one more thing to keep in step. A test in src/lib.rs checks that every
 * exported symbol appears here, so it cannot drift.
 *
 * Ownership, in one place:
 *   - A char* RETURNED by this library is yours; free it with astrid_free_string.
 *   - A char* PASSED TO A CALLBACK is borrowed for the duration of that call. Copy it.
 *   - A const char* you pass in is borrowed for the duration of the call. This library never
 *     keeps it.
 *
 * Threads:
 *   - astrid_call returns immediately and the answer arrives on a pool thread. It is NOT the UI
 *     thread; marshal.
 *   - astrid_call_blocking waits. Never call it from the UI thread: a command that needs the
 *     network takes as long as the network does.
 *   - After astrid_stop returns, no callback starts. It is not a promise that every in-flight
 *     call finishes — one waiting on the network is dropped and its callback never fires.
 */

#ifndef ASTRID_H
#define ASTRID_H

#ifdef __cplusplus
extern "C" {
#endif

/* An opaque handle to a running client. */
typedef struct AstridApp AstridApp;

/*
 * Called when something finishes, or when the cache changes underneath the shell.
 *
 * `user_data` is whatever was registered with the callback — a GCHandle, on the C# side. `json`
 * is borrowed for the duration of this call.
 */
typedef void (*AstridCallback)(void *user_data, const char *json);

/*
 * The version of the core this library was built from.
 *
 * The returned pointer is static: do not free it.
 */
const char *astrid_version(void);

/*
 * Start the client.
 *
 * `config_json` is {"cachePath": "...", "baseUrl": "..."} — baseUrl is optional and defaults to
 * production. Returns NULL on failure, writing the reason to *error_out (which you then free with
 * astrid_free_string). error_out may be NULL if you do not want the reason.
 */
AstridApp *astrid_start(const char *config_json, char **error_out);

/* Stop the client and free the handle. Safe to call with NULL. */
void astrid_stop(AstridApp *handle);

/*
 * Run a command. Returns at once; the answer arrives on `callback`.
 *
 * `user_data` must stay valid until the callback has run.
 */
void astrid_call(AstridApp *handle, const char *request_json, AstridCallback callback,
                 void *user_data);

/*
 * Run a command and wait for the answer. The returned string is yours to free.
 *
 * For a first paint or a test. Not for the UI thread.
 */
char *astrid_call_blocking(AstridApp *handle, const char *request_json);

/*
 * Ask to be told when the cache changes — a live update arriving, or a background sync landing.
 *
 * The callback receives {"change":"task","id":"..."} and the shell refreshes exactly that.
 * `user_data` must stay valid until astrid_stop.
 */
void astrid_subscribe(AstridApp *handle, AstridCallback callback, void *user_data);

/* Free a string this library returned. Safe to call with NULL. */
void astrid_free_string(char *text);

#ifdef __cplusplus
}
#endif

#endif /* ASTRID_H */
