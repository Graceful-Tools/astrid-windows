//! A task field the server omits must apply as unchanged, never as empty — task `e988defa`
//! (AWTD-1251), the Windows half of AITD-499.
//!
//! The guard itself lives in `astrid_core::sync` (`require_only_omitted` + `OMITTED_BY_REQUEST`,
//! core `87d6add`) and has its own unit tests there. This file is not a second copy of those: it
//! locks the guard **at the pin**, through the one surface this repository owns.
//!
//! Why that is worth a test of its own. The guard reaches Windows only because
//! `crates/astrid-ffi/Cargo.toml` names a revision that has it, and a pin is a number in a file
//! that anybody can move. On 2026-10-16 this app was found 20 commits behind core `main` with
//! nobody having noticed for twelve days; the pin went back to a rev without the guard and no test
//! in this repository would have said so. A sync pass driven over a stubbed transport, asked for
//! through `run_json` exactly as the shell asks, fails the moment the pin loses it.
//!
//! `run_json` rather than the `SyncManager` directly: that string is the real FFI boundary — the
//! shell sends `{"kind":"sync"}` and nothing else — so this covers the dispatch and the envelope
//! as well as the guard, and needs no access the shell does not have.

use std::sync::Arc;

use astrid_core::api::StubTransport;
use astrid_core::app::{App, Config};
use astrid_core::model::Task;
use astrid_core::platform::{MemorySecureStore, SystemClock};
use astrid_core::store::Store;
use serde_json::json;

/// The config the shell sends, built the way the shell builds it — from JSON, since `Config` is
/// `Deserialize` and the FFI's `astrid_start` parses it rather than constructing one.
fn app_over(transport: StubTransport) -> App {
    let config: Config = serde_json::from_value(json!({
        "cachePath": ":memory:",
        // Never a real host: an unscripted request must fail as "no stubbed response", not leave
        // the machine to answer it.
        "baseUrl": "https://astrid.invalid",
    }))
    .expect("the shell's config shape");
    App::with_parts(
        &config,
        Arc::new(MemorySecureStore::new()),
        Arc::new(transport),
        Arc::new(SystemClock),
    )
    .expect("an in-memory cache always opens")
}

/// A cached task carrying the text a person typed, with a version older than the page below so
/// that `is_newer` would otherwise let the page win.
fn seed_described_task(store: &Arc<Store>) {
    let cached: Task = serde_json::from_value(json!({
        "id": "t1",
        "title": "Buy milk",
        "description": "The long note the person typed",
        "updatedAt": "2026-09-07T12:00:00Z",
    }))
    .expect("a task");
    store.upsert_task(&cached).expect("stores");
}

fn cached_description(store: &Arc<Store>) -> String {
    store
        .task("t1")
        .expect("reads")
        .expect("the task is still there")
        .description
}

/// Task `e988defa`: a page whose `meta.omitted` names a field this client never asked to drop is
/// refused, and the description already on the device is the one that survives.
///
/// Without the guard `Task::description` is `#[serde(default)]`, so the row above — which has no
/// `description` key at all — decodes as `""`, the merge writes that over the real text, and the
/// next edit pushes the blank to the server. That is the path this closes.
#[tokio::test]
async fn e988defa_a_page_trimmed_of_description_leaves_the_cached_description_alone() {
    let app = app_over(
        StubTransport::new()
            .push_json("/api/v1/lists", 200, json!({ "lists": [] }))
            .push_json(
                "/api/v1/tasks",
                200,
                json!({
                    "tasks": [{
                        "id": "t1",
                        "title": "Buy milk",
                        "updatedAt": "2026-09-07T12:05:00Z"
                    }],
                    "meta": { "total": 1, "omitted": ["description"] }
                }),
            ),
    );
    seed_described_task(app.store());

    let answer = app.run_json(r#"{"kind":"sync"}"#).await;

    assert_eq!(
        cached_description(app.store()),
        "The long note the person typed",
        "the description the server did not send is the one on the device; answer was {answer}"
    );
}

/// The other half of the same rule, and the one that keeps the guard from being "refuse
/// everything": this client really does send `leanListMembers=1`, so a server echoing THAT is
/// doing as it was asked and the page still applies.
///
/// Worth asserting here rather than trusting the core's copy, because the requested set is a
/// property of the request the pinned core sends. A pin that kept the refusal but changed the
/// query would turn every sync into a refused page — an app that silently stops updating, which
/// is harder to notice than one that blanks a description.
#[tokio::test]
async fn e988defa_a_page_that_dropped_only_what_this_client_asked_for_still_applies() {
    let app = app_over(
        StubTransport::new()
            .push_json("/api/v1/lists", 200, json!({ "lists": [] }))
            .push_json(
                "/api/v1/tasks",
                200,
                json!({
                    "tasks": [{
                        "id": "t1",
                        "title": "Buy milk",
                        "description": "The long note the person typed",
                        "updatedAt": "2026-09-07T12:05:00Z"
                    }],
                    "meta": { "total": 1, "omitted": ["listMembers"] }
                }),
            )
            // The pass asks for a second page once the first comes back full-sized; past the end
            // it is empty.
            .fallback(Ok(astrid_core::api::HttpResponse {
                status: 200,
                headers: vec![("content-type".into(), "application/json".into())],
                body: json!({ "tasks": [], "lists": [] }).to_string().into_bytes(),
            })),
    );
    seed_described_task(app.store());

    let answer = app.run_json(r#"{"kind":"sync"}"#).await;

    assert_eq!(
        cached_description(app.store()),
        "The long note the person typed",
        "an allowed omission is not a refusal: the page applied; answer was {answer}"
    );
    assert!(
        app.store()
            .task("t1")
            .expect("reads")
            .expect("present")
            .updated_at
            .is_some(),
        "the page applied, so the cached row carries the server's version: {answer}"
    );
}
