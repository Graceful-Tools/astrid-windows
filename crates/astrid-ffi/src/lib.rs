//! The C ABI the WinUI shell calls `astrid-core` through.
//!
//! Eight functions and one callback type. That is the whole boundary, and keeping it that small is
//! the point: everything the app can *do* is a JSON command through [`astrid_call`], so adding a
//! feature means adding a `Command` variant in the core, not a symbol here, a declaration in a
//! header, a `DllImport` in C# and a marshalling rule for whatever it returns.
//!
//! The eighth is [`astrid_rule`], and it is the one exception the rule above earns: the core's
//! pure half waits on nothing, so asking it a question needs neither a handle nor the runtime,
//! and routing arithmetic through a started app and a callback was machinery for nothing.
//!
//! ## The rules of this file
//!
//! 1. **Nothing unwinds across the boundary.** A panic that crosses a C frame is undefined
//!    behaviour; every entry point catches. A caught panic becomes an error response, so a bug
//!    here is a message on screen rather than a process that vanishes.
//! 2. **Every pointer is checked.** The caller is a garbage-collected runtime whose idea of "still
//!    alive" is not ours.
//! 3. **Strings crossing outward are owned by whoever the signature says.** A callback's string is
//!    borrowed for the duration of the call and must be copied; a returned `char*` is the caller's
//!    to hand back to [`astrid_free_string`].
//!
//! ## Threads
//!
//! The runtime is a tokio pool owned by the handle. [`astrid_call`] returns immediately and the
//! answer arrives on a pool thread, so the UI thread is never blocked on a request — which is the
//! difference between an app that feels instant offline and one that beachballs on a bad network.
//! The callback therefore does **not** arrive on the UI thread, and the shell has to marshal.

pub mod secure_store;

use std::ffi::{c_char, c_void, CStr, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::Arc;

use astrid_core::app::{background, App, Config};
use astrid_core::realtime::Change;
use secure_store::ProtectedFileStore;

/// What the shell is told when something finishes: a JSON string, borrowed for the call.
///
/// `user_data` is whatever was handed in with the callback — a `GCHandle` on the C# side.
pub type Callback = extern "C" fn(user_data: *mut c_void, json: *const c_char);

/// The handle the shell holds. Opaque on the C side.
pub struct AstridApp {
    app: Arc<App>,
    runtime: tokio::runtime::Runtime,
    /// Where change notifications go, if the shell asked for them.
    subscriber: std::sync::Mutex<Option<Arc<Subscriber>>>,
    /// The background loops, stopped by [`astrid_stop`] and on drop.
    ///
    /// Dropping the runtime would stop them anyway, but only at their next await point — which for
    /// a stream sitting on a socket is whenever the server next says something. Asking them to
    /// stop first means shutting the app takes a moment rather than however long a quiet
    /// connection stays quiet.
    loops: background::BackgroundLoops,
}

/// A callback plus its context, marked as safe to move between threads.
///
/// The pointer is opaque to Rust and is only ever handed back to the callback that was registered
/// with it. Keeping the assertion in one named place is better than scattering `unsafe impl`
/// around the call sites that need it.
struct Subscriber {
    callback: Callback,
    user_data: *mut c_void,
}

// SAFETY: `user_data` is never dereferenced here — it is passed back to the callback exactly as it
// was given. The shell is responsible for it being valid until `astrid_stop`, which is the
// contract stated in the header.
unsafe impl Send for Subscriber {}
unsafe impl Sync for Subscriber {}

/// The version of this library, and the revision of the core inside it — `0.1.0 (core a8591a93c022)`.
///
/// Both, because they answer different questions and a crash report needs the second one.
/// Almost every line in this process is the core's, the shell takes it by a pin somebody moves,
/// and this crate's own version is a number nobody bumps: a report saying `0.1.0` could not
/// distinguish a fortnight of core changes (task `ab249b76`). `astrid_core::BUILD_REV` is
/// resolved by the core's `build.rs` and is never empty.
///
/// # Safety
/// The returned pointer is static and must not be freed.
#[no_mangle]
pub extern "C" fn astrid_version() -> *const c_char {
    // Built once, leaked once: the signature promises a pointer good for the life of the process,
    // and the revision is only known at compile time of the *core*, so this cannot be a literal.
    static VERSION: std::sync::OnceLock<CString> = std::sync::OnceLock::new();
    VERSION
        .get_or_init(|| {
            CString::new(format!(
                "{} (core {})",
                env!("CARGO_PKG_VERSION"),
                astrid_core::BUILD_REV
            ))
            // A revision with an interior nul would be a corrupt build; the version is still owed.
            .unwrap_or_else(|_| {
                CString::new(env!("CARGO_PKG_VERSION")).expect("a literal has no nul")
            })
        })
        .as_ptr()
}

/// Answer one pure rule: JSON in, JSON out, no handle and no runtime.
///
/// The stateless half of the core — repeating rollover, permissions, filters, smart parsing, the
/// row projections — is a synchronous function that waits on nothing, and web and the wasm build
/// have called it directly since it existed. This shell could only reach it through
/// [`astrid_call`], which needs a started app, a tokio pool and a callback for a question that is
/// pure arithmetic (task `ab249b76`). The vocabulary is `astrid_core::rules::Rule`, tagged by
/// `kind`, and the answer is the same `{"ok":…}` envelope every other client reads.
///
/// Never null: a request that cannot be read answers a refusal envelope, because a caller that
/// has to tell a bad request from a dead library by checking for null gets it wrong once.
///
/// # Safety
/// `request_json` must be a valid `\0`-terminated string, or null. The returned string is the
/// caller's, to free with [`astrid_free_string`].
#[no_mangle]
pub unsafe extern "C" fn astrid_rule(request_json: *const c_char) -> *mut c_char {
    let answer = catch_unwind(AssertUnwindSafe(|| match read_string(request_json) {
        Some(request) => astrid_core::rules::run_json(&request),
        // The same envelope the core's own refusals use, which is also what `astrid_call` answers.
        None => failure_json("the rule request was not readable text"),
    }))
    .unwrap_or_else(|_| failure_json("the core panicked answering that rule"));
    to_c_string(answer)
}

/// Start the client.
///
/// `config_json` is [`Config`] — a cache path and optionally a base URL. Returns null on failure,
/// with the reason written to `error_out` as a string the caller frees with
/// [`astrid_free_string`].
///
/// # Safety
/// `config_json` must be a valid `\0`-terminated string. `error_out` may be null.
#[no_mangle]
pub unsafe extern "C" fn astrid_start(
    config_json: *const c_char,
    error_out: *mut *mut c_char,
) -> *mut AstridApp {
    let result = catch_unwind(AssertUnwindSafe(|| {
        let config_text = read_string(config_json).ok_or("the config was not readable text")?;
        let config: Config =
            serde_json::from_str(&config_text).map_err(|error| error.to_string())?;

        // A credential file beside the cache: one directory the shell already chose, rather than a
        // second location for the core to have an opinion about.
        let credential_path = std::path::Path::new(&config.cache_path)
            .parent()
            .unwrap_or_else(|| std::path::Path::new("."))
            .join("astrid.credentials");

        let runtime = tokio::runtime::Builder::new_multi_thread()
            .enable_all()
            .thread_name("astrid-core")
            .build()
            .map_err(|error| error.to_string())?;

        let app = Arc::new(
            App::start(&config, Arc::new(ProtectedFileStore::at(credential_path)))
                .map_err(|error| error.to_string())?,
        );

        // The loops that keep the app up to date without being asked: a sixty-second sync pass,
        // the live stream, a half-minute look for a reminder that has come due, and a five-minute
        // mirror of the Google-linked lists.
        // Started here rather than by the shell, because "is the app current?" is not a question a
        // window should have to remember to ask — and *which* loops run is the core's to say, not
        // this file's. Five hand-written spawns lived here, and Apple's five were three, so an
        // account current on one platform was stale on another (task `ab249b76`).
        let loops = app.start_background(runtime.handle(), background::LoopSet::ALL);

        Ok::<_, String>(AstridApp {
            app,
            runtime,
            subscriber: std::sync::Mutex::new(None),
            loops,
        })
    }));

    match result {
        Ok(Ok(handle)) => Box::into_raw(Box::new(handle)),
        Ok(Err(message)) => {
            write_error(error_out, &message);
            std::ptr::null_mut()
        }
        Err(_) => {
            write_error(error_out, "the core panicked while starting");
            std::ptr::null_mut()
        }
    }
}

/// Stop the client and free the handle.
///
/// **The contract, exactly.** Dropping the runtime blocks until its worker threads have finished
/// the poll they are in and parked, so no callback *starts* after this returns — which is what
/// makes it safe for the shell to release its `GCHandle` here. It is not a promise that every
/// in-flight call completes: a command waiting on the network is dropped at its next await point
/// and its callback never fires. A shell that must see an answer has to wait for it before
/// stopping; one that is closing does not care.
///
/// # Safety
/// `handle` must have come from [`astrid_start`] and must not be used again. Safe to call with
/// null.
#[no_mangle]
pub unsafe extern "C" fn astrid_stop(handle: *mut AstridApp) {
    if handle.is_null() {
        return;
    }
    let _ = catch_unwind(AssertUnwindSafe(|| {
        let handle = Box::from_raw(handle);
        // Ask the background loops to stop before the runtime is torn down, so shutting the app
        // takes a moment rather than however long the live stream stays quiet. Dropping the handle
        // would stop them too; saying so here keeps the order explicit, because the runtime's own
        // drop is what blocks.
        handle.loops.stop();
        drop(handle);
    }));
}

/// Run a command. Returns at once; the answer arrives on `callback`, on a pool thread.
///
/// The JSON handed to the callback is borrowed for the duration of that call: copy it.
///
/// # Safety
/// `handle` must be live, `request_json` a valid `\0`-terminated string, and `user_data` valid
/// until the callback has run.
#[no_mangle]
pub unsafe extern "C" fn astrid_call(
    handle: *mut AstridApp,
    request_json: *const c_char,
    callback: Callback,
    user_data: *mut c_void,
) {
    let Some(handle) = handle.as_ref() else {
        deliver(callback, user_data, &failure_json("the handle was null"));
        return;
    };
    let Some(request) = read_string(request_json) else {
        deliver(
            callback,
            user_data,
            &failure_json("the request was not readable text"),
        );
        return;
    };

    let app = handle.app.clone();
    let subscriber = Arc::new(Subscriber {
        callback,
        user_data,
    });
    handle.runtime.spawn(async move {
        // The work runs in a task of its own so a panic inside it becomes a `JoinError` rather
        // than a callback that never fires. A shell awaiting an answer that is never coming is
        // worse than an error: it has no way to find out.
        let worker = tokio::spawn(async move { app.run_json(&request).await });
        let answer = match worker.await {
            Ok(answer) => answer,
            // Cancelled and panicked are different things and the shell can act on the difference:
            // a cancelled call means the core was stopped underneath it, which is what closing the
            // app looks like from here, and is not a bug to report.
            Err(error) if error.is_cancelled() => {
                failure_json("the core stopped before that command finished")
            }
            Err(_) => failure_json("the core panicked running that command"),
        };
        deliver(subscriber.callback, subscriber.user_data, &answer);
    });
}

/// Run a command and wait for it. For the paths that genuinely need an answer before continuing —
/// a first paint, or a test.
///
/// **Not for the UI thread.** A command that needs the network can take as long as the network
/// does. [`astrid_call`] is the one to use from a click handler.
///
/// # Safety
/// As [`astrid_call`]. The returned string is the caller's, to free with [`astrid_free_string`].
#[no_mangle]
pub unsafe extern "C" fn astrid_call_blocking(
    handle: *mut AstridApp,
    request_json: *const c_char,
) -> *mut c_char {
    let Some(handle) = handle.as_ref() else {
        return to_c_string(failure_json("the handle was null"));
    };
    let Some(request) = read_string(request_json) else {
        return to_c_string(failure_json("the request was not readable text"));
    };

    let app = handle.app.clone();
    let answer = catch_unwind(AssertUnwindSafe(|| {
        handle.runtime.block_on(app.run_json(&request))
    }))
    .unwrap_or_else(|_| failure_json("the core panicked running that command"));
    to_c_string(answer)
}

/// Ask to be told when the cache changes underneath the shell — a live update, or a background
/// sync landing.
///
/// The callback receives `{"change":"task","id":"..."}` and the shell refreshes exactly that.
/// Redrawing everything on each change is worse than not subscribing at all.
///
/// # Safety
/// `user_data` must stay valid until [`astrid_stop`].
#[no_mangle]
pub unsafe extern "C" fn astrid_subscribe(
    handle: *mut AstridApp,
    callback: Callback,
    user_data: *mut c_void,
) {
    let Some(handle) = handle.as_ref() else {
        return;
    };
    // One `Arc<Subscriber>`, shared between the closure and the handle. Capturing the callback
    // and the pointer as separate fields would make the closure hold a bare `*mut c_void`, which
    // is not `Send` — and rightly so; the assertion belongs on the named type, where it is
    // documented, not on an anonymous closure.
    let subscriber = Arc::new(Subscriber {
        callback,
        user_data,
    });
    if let Ok(mut slot) = handle.subscriber.lock() {
        *slot = Some(subscriber.clone());
    }
    handle.app.realtime().on_change(move |change: &Change| {
        deliver(subscriber.callback, subscriber.user_data, &change.to_json());
    });
}

/// Free a string this library returned.
///
/// # Safety
/// `text` must have come from this library and must not be used again. Safe to call with null.
#[no_mangle]
pub unsafe extern "C" fn astrid_free_string(text: *mut c_char) {
    if text.is_null() {
        return;
    }
    drop(CString::from_raw(text));
}

// ─── The plumbing ────────────────────────────────────────────────────────────────────────────

/// Write a failure message where [`astrid_start`] promised to put one.
///
/// # Safety
/// `error_out` is either null or a pointer the caller owns.
unsafe fn write_error(error_out: *mut *mut c_char, message: &str) {
    if error_out.is_null() {
        return;
    }
    *error_out = to_c_string(message.to_string());
}

unsafe fn read_string(text: *const c_char) -> Option<String> {
    if text.is_null() {
        return None;
    }
    CStr::from_ptr(text).to_str().ok().map(str::to_string)
}

fn to_c_string(text: String) -> *mut c_char {
    // A NUL inside would truncate the answer at the C boundary. JSON from `serde_json` never
    // contains one — it escapes control characters — so this is a belt-and-braces replacement
    // rather than a case anybody has seen.
    CString::new(text.replace('\0', ""))
        .unwrap_or_else(|_| CString::new("{\"ok\":false}").expect("a literal with no NUL"))
        .into_raw()
}

fn deliver(callback: Callback, user_data: *mut c_void, json: &str) {
    let Ok(text) = CString::new(json.replace('\0', "")) else {
        return;
    };
    // Borrowed for the call: the shell copies. A callback that panics is the shell's bug, and
    // catching it here keeps that bug from becoming undefined behaviour in ours.
    let _ = catch_unwind(AssertUnwindSafe(|| callback(user_data, text.as_ptr())));
}

fn failure_json(message: &str) -> String {
    serde_json::json!({
        "ok": false,
        "error": { "kind": "badRequest", "message": message }
    })
    .to_string()
}

// `change_json` lived here until the core grew `Change::to_json` — the same vocabulary, answered
// once so every shell hears the same words (astrid-core's Apple pass). Keeping a second spelling
// here is how this boundary came to miss a variant: the copy had no arm for `Change::Stream`, and
// `agentTyping` said only `channelId` and `active` while the core's event carries the task and the
// agent's name as well.

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    /// A place for one test's callbacks to land.
    ///
    /// Per test rather than a static: the tests run side by side in one process, and a shared
    /// collector makes each of them see the others' callbacks — which is a flaky test that blames
    /// the wrong code.
    type Heard = Mutex<Vec<String>>;

    extern "C" fn record(user_data: *mut c_void, json: *const c_char) {
        let text = unsafe { CStr::from_ptr(json) }
            .to_string_lossy()
            .into_owned();
        // SAFETY: every caller in these tests passes a pointer to a `Heard` that outlives the call.
        let heard = unsafe { &*(user_data as *const Heard) };
        heard.lock().expect("lock").push(text);
    }

    fn start_in_memory() -> *mut AstridApp {
        let config = CString::new(r#"{"cachePath":":memory:"}"#).expect("a literal");
        let mut error: *mut c_char = std::ptr::null_mut();
        let handle = unsafe { astrid_start(config.as_ptr(), &mut error) };
        assert!(!handle.is_null(), "the core did not start");
        handle
    }

    fn call_blocking(handle: *mut AstridApp, request: &str) -> serde_json::Value {
        let request = CString::new(request).expect("a literal");
        let answer = unsafe { astrid_call_blocking(handle, request.as_ptr()) };
        let text = unsafe { CStr::from_ptr(answer) }
            .to_string_lossy()
            .into_owned();
        unsafe { astrid_free_string(answer) };
        serde_json::from_str(&text).expect("valid JSON")
    }

    #[test]
    fn a_command_goes_in_and_an_answer_comes_back() {
        let handle = start_in_memory();
        let created = call_blocking(handle, r#"{"kind":"createTask","title":"Buy milk"}"#);
        assert_eq!(created["ok"], true);
        assert_eq!(created["value"]["title"], "Buy milk");
        unsafe { astrid_stop(handle) };
    }

    /// A null handle from a shell that stopped the core and then clicked something has to be an
    /// answer, not a crash.
    #[test]
    fn a_null_handle_is_an_error_rather_than_a_crash() {
        let answer = call_blocking(std::ptr::null_mut(), r#"{"kind":"lists"}"#);
        assert_eq!(answer["ok"], false);
        assert!(answer["error"]["message"]
            .as_str()
            .expect("a message")
            .contains("null"));
    }

    #[test]
    fn a_request_that_is_not_json_is_answered_rather_than_ignored() {
        let handle = start_in_memory();
        let answer = call_blocking(handle, "not json");
        assert_eq!(answer["ok"], false);
        assert_eq!(answer["error"]["kind"], "badRequest");
        unsafe { astrid_stop(handle) };
    }

    #[test]
    fn a_config_that_will_not_parse_reports_why_instead_of_returning_a_handle() {
        let config = CString::new("{}").expect("a literal");
        let mut error: *mut c_char = std::ptr::null_mut();
        let handle = unsafe { astrid_start(config.as_ptr(), &mut error) };
        assert!(handle.is_null());
        assert!(!error.is_null(), "a failure has to say why");
        let message = unsafe { CStr::from_ptr(error) }
            .to_string_lossy()
            .into_owned();
        assert!(message.contains("cachePath"), "{message}");
        unsafe { astrid_free_string(error) };
    }

    /// The asynchronous path: the call returns at once and the answer arrives on the callback,
    /// on another thread.
    #[test]
    fn an_asynchronous_call_answers_through_the_callback() {
        let heard: Heard = Mutex::new(Vec::new());
        let handle = start_in_memory();
        let request = CString::new(r#"{"kind":"lists"}"#).expect("a literal");
        let user_data = &heard as *const Heard as *mut c_void;
        unsafe { astrid_call(handle, request.as_ptr(), record, user_data) };

        // Wait for it rather than assuming stopping will flush it — see the contract on
        // `astrid_stop`, which promises only that no callback *starts* after it returns.
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while heard.lock().expect("lock").is_empty() && std::time::Instant::now() < deadline {
            std::thread::sleep(std::time::Duration::from_millis(5));
        }
        unsafe { astrid_stop(handle) };

        let answers = heard.lock().expect("lock").clone();
        assert_eq!(answers.len(), 1, "{answers:?}");
        assert!(answers[0].contains("\"ok\":true"), "{}", answers[0]);
    }

    /// Stopping while a call is in flight must not crash, and must not deliver into a handle the
    /// shell has already released. The guarantee is "no callback starts after this returns".
    #[test]
    fn stopping_with_a_call_in_flight_is_safe() {
        let heard: Heard = Mutex::new(Vec::new());
        let handle = start_in_memory();
        let request = CString::new(r#"{"kind":"sync"}"#).expect("a literal");
        unsafe {
            astrid_call(
                handle,
                request.as_ptr(),
                record,
                &heard as *const Heard as *mut c_void,
            )
        };
        unsafe { astrid_stop(handle) };
    }

    /// The boundary passes the core's own words through, unchanged.
    ///
    /// The vocabulary itself is the core's to specify and to test; what this asserts is that
    /// nothing here rewrites it on the way out, which is what a second spelling in this file used
    /// to do.
    #[test]
    fn a_change_notification_names_only_what_moved() {
        assert_eq!(
            Change::Task("t1".into()).to_json(),
            r#"{"change":"task","id":"t1"}"#
        );
        assert_eq!(
            Change::Comments("t1".into()).to_json(),
            r#"{"change":"comments","taskId":"t1"}"#
        );
        assert_eq!(Change::NeedsSync.to_json(), r#"{"change":"needsSync"}"#);
    }

    /// A crash report has to name the code that crashed, and almost all of it is the core's.
    ///
    /// This used to answer astrid-ffi's own `0.1.0` — a number nobody bumps, describing the
    /// thinnest layer in the process, while the core it was running came from a pin somebody
    /// moved a fortnight ago (task `ab249b76`). Now it carries both: this crate's version for the
    /// shell, and `astrid_core::BUILD_REV` for what it was built against.
    #[test]
    fn task_ab249b76_the_version_names_the_core_revision() {
        let version = unsafe { CStr::from_ptr(astrid_version()) }
            .to_string_lossy()
            .into_owned();

        assert!(
            version.starts_with(env!("CARGO_PKG_VERSION")),
            "{version} does not start with this crate's version"
        );
        assert!(
            version.contains(astrid_core::BUILD_REV),
            "{version} does not name the core revision ({})",
            astrid_core::BUILD_REV
        );
        // Static, so the pointer outlives the call. Asking twice must give the same bytes.
        let again = unsafe { CStr::from_ptr(astrid_version()) }.to_string_lossy();
        assert_eq!(version, again);
    }

    /// The stateless rules, without a handle and without a runtime.
    ///
    /// Web and the wasm build have reached `rules::run_json` since it existed; this shell could
    /// only ask through `astrid_call`, which needs a started app, a tokio pool and a callback for
    /// a question that is pure arithmetic (task `ab249b76`). An unknown rule answers an error
    /// envelope rather than null, because a shell that has to tell a parse failure from a dead
    /// library by checking for null gets it wrong once and then crashes.
    #[test]
    fn task_ab249b76_a_rule_answers_without_a_handle() {
        const REQUEST: &str = r#"{"kind":"ratingKey"}"#;
        let request = CString::new(REQUEST).expect("a literal");
        let text = read_rule_answer(unsafe { astrid_rule(request.as_ptr()) });

        // The core's own answer, passed through unchanged.
        assert_eq!(text, astrid_core::rules::run_json(REQUEST));
        assert!(text.contains(r#""ok":true"#), "{text} is not a success");
    }

    /// Null and nonsense are a shell on a path that already failed. Both answer a refusal
    /// envelope, because a caller that has to tell a bad request from a dead library by checking
    /// for null gets it wrong once and then crashes.
    #[test]
    fn task_ab249b76_a_rule_that_cannot_be_read_still_answers_json() {
        let text = read_rule_answer(unsafe { astrid_rule(std::ptr::null()) });
        assert!(text.contains(r#""ok":false"#), "{text} is not a refusal");

        let garbage = CString::new("not json at all").expect("a literal");
        let text = read_rule_answer(unsafe { astrid_rule(garbage.as_ptr()) });
        assert!(text.contains(r#""ok":false"#), "{text} is not a refusal");
    }

    /// Copy what [`astrid_rule`] returned and hand the allocation back, so a test reads a `String`
    /// and cannot leak.
    fn read_rule_answer(answer: *mut c_char) -> String {
        assert!(!answer.is_null(), "astrid_rule answered null");
        let text = unsafe { CStr::from_ptr(answer) }
            .to_string_lossy()
            .into_owned();
        unsafe { astrid_free_string(answer) };
        text
    }

    /// Freeing null, and stopping null, are both things a shell does on a path that failed
    /// earlier. Neither may crash.
    /// The header is hand-written, so this is what stops it drifting from the library: every
    /// exported symbol has to appear in it. A shell compiled against a header missing a function
    /// fails at link time, which is fine; a header describing a function that no longer exists
    /// fails at load time in front of a user, which is not.
    #[test]
    fn the_header_describes_every_exported_symbol() {
        const HEADER: &str = include_str!("../include/astrid.h");
        const SOURCE: &str = include_str!("lib.rs");

        let exported: Vec<&str> = SOURCE
            .lines()
            .zip(SOURCE.lines().skip(1))
            .filter(|(attribute, _)| attribute.trim() == "#[no_mangle]")
            .filter_map(|(_, signature)| {
                let after = signature.split("fn ").nth(1)?;
                Some(after.split('(').next()?.trim())
            })
            .collect();

        assert!(
            exported.len() >= 8,
            "the scan found {} symbols, so it has stopped working: {exported:?}",
            exported.len()
        );
        for symbol in exported {
            assert!(
                HEADER.contains(symbol),
                "{symbol} is exported but not in include/astrid.h"
            );
        }
    }

    /// And the shell can actually call every one of them.
    ///
    /// The header stops the *C* declaration drifting; nothing stopped the C# one. A function
    /// added to the ABI and the header but not to `NativeMethods.cs` is a door the shell cannot
    /// open, and it is invisible — both sides compile, and only the feature that needed it is
    /// missing. `astrid_rule` was added by task `ab249b76` and is the first such function since
    /// the boundary was written, which is exactly when the guard is worth having.
    #[test]
    fn task_ab249b76_the_shell_binds_every_exported_symbol() {
        const NATIVE_METHODS: &str =
            include_str!("../../../app/Astrid.Core.Bindings/NativeMethods.cs");
        const SOURCE: &str = include_str!("lib.rs");

        let exported: Vec<&str> = SOURCE
            .lines()
            .zip(SOURCE.lines().skip(1))
            .filter(|(attribute, _)| attribute.trim() == "#[no_mangle]")
            .filter_map(|(_, signature)| {
                let after = signature.split("fn ").nth(1)?;
                Some(after.split('(').next()?.trim())
            })
            .collect();

        assert!(
            exported.len() >= 8,
            "the scan found {} symbols, so it has stopped working: {exported:?}",
            exported.len()
        );
        for symbol in exported {
            assert!(
                NATIVE_METHODS.contains(&format!("EntryPoint = \"{symbol}\"")),
                "{symbol} is exported but no LibraryImport in NativeMethods.cs names it"
            );
        }
    }

    #[test]
    fn freeing_and_stopping_nothing_is_harmless() {
        unsafe { astrid_free_string(std::ptr::null_mut()) };
        unsafe { astrid_stop(std::ptr::null_mut()) };
    }
}
