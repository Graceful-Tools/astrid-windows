//! The session credential at rest.
//!
//! Implements [`astrid_core::platform::SecureStore`] over a file the operating system encrypts for
//! the current user. On Windows that is DPAPI (`CryptProtectData`), which ties the ciphertext to
//! the Windows account, so another user on the same machine — or a copy of the file taken off it —
//! cannot read it.
//!
//! ## Why not the Credential Locker
//!
//! `PasswordVault` is where a Windows app is expected to put a credential, and it is what
//! `docs/M0_NOTES.md` names as the intent. It is also a WinRT API with a documented apartment
//! sensitivity, called here from whichever pool thread the runtime picks — which is precisely the
//! open spike in that file. DPAPI has neither problem: it is a flat Win32 call, thread-agnostic,
//! and it is the fallback that spike already nominates.
//!
//! The trait is what makes this a decision rather than a commitment. When the spike is settled the
//! shell can hand in a Credential Locker implementation and nothing above it changes.
//!
//! On anything that is not Windows this stores plaintext, and says so loudly: the crate builds and
//! tests on macOS and Linux to keep the platform boundary honest, and nobody signs into production
//! from there.

use std::path::{Path, PathBuf};
use std::sync::Mutex;

use astrid_core::platform::{PlatformError, SecureStore};
use async_trait::async_trait;

/// Credentials in a file only this Windows account can decrypt.
pub struct ProtectedFileStore {
    path: PathBuf,
    /// Serialises read-modify-write. The file holds every key, so two concurrent writes without
    /// this would lose one of them — and the one most likely to be lost is the session cookie,
    /// written on every renewal.
    guard: Mutex<()>,
}

impl ProtectedFileStore {
    pub fn at(path: impl AsRef<Path>) -> Self {
        ProtectedFileStore {
            path: path.as_ref().to_path_buf(),
            guard: Mutex::new(()),
        }
    }

    fn read_all(&self) -> serde_json::Map<String, serde_json::Value> {
        let Ok(bytes) = std::fs::read(&self.path) else {
            return serde_json::Map::new();
        };
        let Some(plain) = unprotect(&bytes) else {
            // Unreadable means unusable: a file written by another Windows account, or one that a
            // half-finished write left truncated. Starting from empty signs the user in again,
            // which is recoverable; refusing to start is not.
            tracing::warn!("the stored credential could not be decrypted; starting signed out");
            return serde_json::Map::new();
        };
        serde_json::from_slice(&plain).unwrap_or_default()
    }

    fn write_all(
        &self,
        entries: &serde_json::Map<String, serde_json::Value>,
    ) -> Result<(), PlatformError> {
        let plain = serde_json::to_vec(entries)
            .map_err(|error| PlatformError::Denied(error.to_string()))?;
        let sealed =
            protect(&plain).ok_or_else(|| PlatformError::Denied("could not protect".into()))?;

        if let Some(parent) = self.path.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|error| PlatformError::Unavailable(error.to_string()))?;
        }
        // Write beside and rename, so a process that dies mid-write leaves the previous credential
        // intact rather than a truncated file that reads as signed out.
        let temporary = self.path.with_extension("tmp");
        std::fs::write(&temporary, &sealed)
            .map_err(|error| PlatformError::Unavailable(error.to_string()))?;
        std::fs::rename(&temporary, &self.path)
            .map_err(|error| PlatformError::Unavailable(error.to_string()))?;
        Ok(())
    }
}

#[async_trait]
impl SecureStore for ProtectedFileStore {
    async fn get(&self, key: &str) -> Option<String> {
        let _guard = self.guard.lock().ok()?;
        self.read_all()
            .get(key)
            .and_then(serde_json::Value::as_str)
            .map(str::to_string)
    }

    async fn set(&self, key: &str, value: &str) -> Result<(), PlatformError> {
        let _guard = self
            .guard
            .lock()
            .map_err(|_| PlatformError::Unavailable("the store lock is poisoned".into()))?;
        let mut entries = self.read_all();
        entries.insert(
            key.to_string(),
            serde_json::Value::String(value.to_string()),
        );
        self.write_all(&entries)
    }

    async fn delete(&self, key: &str) -> Result<(), PlatformError> {
        let _guard = self
            .guard
            .lock()
            .map_err(|_| PlatformError::Unavailable("the store lock is poisoned".into()))?;
        let mut entries = self.read_all();
        entries.remove(key);
        self.write_all(&entries)
    }
}

#[cfg(windows)]
fn protect(plain: &[u8]) -> Option<Vec<u8>> {
    windows_dpapi::protect(plain)
}

#[cfg(windows)]
fn unprotect(sealed: &[u8]) -> Option<Vec<u8>> {
    windows_dpapi::unprotect(sealed)
}

/// Off Windows there is no user-scoped encryption to lean on, and inventing one here would be a
/// key stored beside the thing it protects. The credential is written in the clear, and this is a
/// development convenience only — see the module note.
#[cfg(not(windows))]
fn protect(plain: &[u8]) -> Option<Vec<u8>> {
    Some(plain.to_vec())
}

#[cfg(not(windows))]
fn unprotect(sealed: &[u8]) -> Option<Vec<u8>> {
    Some(sealed.to_vec())
}

#[cfg(windows)]
mod windows_dpapi {
    use windows_sys::Win32::Foundation::LocalFree;
    use windows_sys::Win32::Security::Cryptography::{
        CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB,
    };

    /// The description DPAPI stores beside the ciphertext. Visible in a few Windows tools, so it
    /// says what the blob is rather than nothing.
    const DESCRIPTION: &[u16] = &[
        b'A' as u16,
        b's' as u16,
        b't' as u16,
        b'r' as u16,
        b'i' as u16,
        b'd' as u16,
        0,
    ];

    pub fn protect(plain: &[u8]) -> Option<Vec<u8>> {
        // SAFETY: `input` points at `plain`, which outlives the call. DPAPI writes a blob whose
        // pointer we own and free with `LocalFree`, exactly as documented.
        unsafe {
            let input = CRYPT_INTEGER_BLOB {
                cbData: plain.len() as u32,
                pbData: plain.as_ptr() as *mut u8,
            };
            let mut output = CRYPT_INTEGER_BLOB {
                cbData: 0,
                pbData: std::ptr::null_mut(),
            };
            let ok = CryptProtectData(
                &input,
                DESCRIPTION.as_ptr(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                0,
                &mut output,
            );
            if ok == 0 {
                return None;
            }
            let sealed = std::slice::from_raw_parts(output.pbData, output.cbData as usize).to_vec();
            LocalFree(output.pbData as _);
            Some(sealed)
        }
    }

    pub fn unprotect(sealed: &[u8]) -> Option<Vec<u8>> {
        // SAFETY: as above, in the other direction.
        unsafe {
            let input = CRYPT_INTEGER_BLOB {
                cbData: sealed.len() as u32,
                pbData: sealed.as_ptr() as *mut u8,
            };
            let mut output = CRYPT_INTEGER_BLOB {
                cbData: 0,
                pbData: std::ptr::null_mut(),
            };
            let ok = CryptUnprotectData(
                &input,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                0,
                &mut output,
            );
            if ok == 0 {
                return None;
            }
            let plain = std::slice::from_raw_parts(output.pbData, output.cbData as usize).to_vec();
            LocalFree(output.pbData as _);
            Some(plain)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use astrid_core::platform::SESSION_COOKIE_KEY;

    fn temporary_path(name: &str) -> PathBuf {
        let mut path = std::env::temp_dir();
        path.push(format!(
            "astrid-secure-{name}-{}",
            std::process::id() as u64 * 31 + name.len() as u64
        ));
        let _ = std::fs::remove_file(&path);
        path
    }

    #[tokio::test]
    async fn a_credential_survives_being_written_and_read_back() {
        let path = temporary_path("roundtrip");
        let store = ProtectedFileStore::at(&path);

        assert_eq!(store.get(SESSION_COOKIE_KEY).await, None);
        store
            .set(SESSION_COOKIE_KEY, "next-auth.session-token=abc")
            .await
            .expect("stores");

        // A second store over the same file reads it — which is the case that matters, because it
        // is what a relaunch is.
        let reopened = ProtectedFileStore::at(&path);
        assert_eq!(
            reopened.get(SESSION_COOKIE_KEY).await.as_deref(),
            Some("next-auth.session-token=abc")
        );

        reopened.delete(SESSION_COOKIE_KEY).await.expect("deletes");
        assert_eq!(reopened.get(SESSION_COOKIE_KEY).await, None);
        let _ = std::fs::remove_file(&path);
    }

    /// Several keys share the file, and writing one must not lose the others — the session cookie
    /// and the user id are written by different code paths moments apart.
    #[tokio::test]
    async fn keys_do_not_overwrite_one_another() {
        let path = temporary_path("multi");
        let store = ProtectedFileStore::at(&path);
        store.set("a", "1").await.expect("stores");
        store.set("b", "2").await.expect("stores");
        assert_eq!(store.get("a").await.as_deref(), Some("1"));
        assert_eq!(store.get("b").await.as_deref(), Some("2"));
        let _ = std::fs::remove_file(&path);
    }

    /// A file from another Windows account, or one a half-finished write truncated, is unreadable.
    /// Signing the user in again is recoverable; refusing to start is not.
    #[tokio::test]
    async fn an_unreadable_file_reads_as_signed_out_rather_than_failing() {
        let path = temporary_path("corrupt");
        std::fs::write(&path, b"not a protected blob at all").expect("writes");
        let store = ProtectedFileStore::at(&path);
        assert_eq!(store.get(SESSION_COOKIE_KEY).await, None);

        // And it can be written over.
        store
            .set(SESSION_COOKIE_KEY, "fresh")
            .await
            .expect("stores");
        assert_eq!(
            store.get(SESSION_COOKIE_KEY).await.as_deref(),
            Some("fresh")
        );
        let _ = std::fs::remove_file(&path);
    }

    /// The bytes on disk are not the credential. On Windows DPAPI does this; the test states the
    /// expectation so a change that drops the protection fails here rather than in an audit.
    #[cfg(windows)]
    #[tokio::test]
    async fn the_credential_is_not_readable_in_the_file() {
        let path = temporary_path("encrypted");
        let store = ProtectedFileStore::at(&path);
        store
            .set(SESSION_COOKIE_KEY, "next-auth.session-token=secret-value")
            .await
            .expect("stores");

        let raw = std::fs::read(&path).expect("reads");
        let text = String::from_utf8_lossy(&raw);
        assert!(
            !text.contains("secret-value"),
            "the token is in the file in the clear"
        );
        let _ = std::fs::remove_file(&path);
    }
}
