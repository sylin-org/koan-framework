---
id: WEB-0079
slug: the-key-ring-persists-where-the-application-says
domain: Web
status: Accepted
date: 2026-10-29
title: The Data Protection key ring persists where the application says
related:
  - WEB-0071
  - WEB-0078
  - ARCH-0128
---

# WEB-0079: The Data Protection key ring persists where the application says

## Context

Every cookie Koan Web Auth issues, every protected authentication state the OAuth2, OIDC and atproto handlers
round-trip through the browser, and every protected session the atproto connector keeps on disk is encrypted with
the ASP.NET Data Protection key ring. Koan resolved the ring (`AuthSchemeSeeder`, `AtprotoAuthModule`) and the atproto
connector's README required it to be persisted, but nothing in Koan said where. Left to ASP.NET the ring lives in the
process's user profile, or in memory when there is none, and the application's name is derived from its content
root path. A container rebuilt from its image loses the ring: every session cookie stops validating and every
protected atproto session on the persistent volume becomes unreadable, although the volume survived. A second
instance of the same application cannot read what the first protected either, because its name differs.

The name the ring is scoped to must also outlive the application's own naming. `ApplicationIdentitySnapshot.Code` is
a slug of the display name (`Koan:Application:Name`), so a name derived from it changes when the application is
renamed, and every key on the volume would then belong to a name nobody uses.

Measured by a consumer that runs in a container whose data folder is the only persistent state.

## Decision

**One setting, `Koan:Web:KeyRingDirectory`, names the folder the key ring persists in, and the folder remembers whose
keys it holds. Koan.Web owns it; the application's own Data Protection configuration wins over it.**

1. `KoanWebKeyRing` (Koan.Web, a singleton) reads the setting once. A relative value resolves against the content
   root, like the atproto connector's `SessionDirectory`. When it is set, the ring persists in that folder
   (`FileSystemXmlRepository`) under the application name the folder remembers, so every instance over the same
   folder shares the keys and a rebuilt container reads what the previous one protected.
2. **The folder remembers the application name.** The first start over a folder writes the name it uses into
   `koan-application-name.txt` in the folder — `Koan:Web:KeyRingApplicationName` when set, else
   `ApplicationIdentitySnapshot.Code` — and every later start reads it back and uses it, so renaming the application
   never changes it. The file is created exclusively (`FileMode.CreateNew`, exclusive on Windows and Unix alike) and
   holds the name as one line ending in a newline, so of instances starting together over an empty folder exactly
   one writes its name and every other reads that one: a reader shares the file fully and, finding it unreadable,
   empty or unterminated, retries a few short pauses (at most half a second) before deciding; after that a
   non-empty name written by hand without a newline is taken as it is, and an empty file refuses the start.
   `Koan:Web:KeyRingApplicationName` pins the name;
   a pinned name that differs from the one the folder holds refuses the start, naming both, since under it every
   key in the folder would be unreadable.
3. **The application's explicit configuration wins.** Koan supplies defaults, not overrides: the owner, as the
   `IConfigureOptions<KeyManagementOptions>` hook, sets the folder's repository only when no repository is
   configured yet, so an application's `PersistKeysToFileSystem` (or any `PersistKeysTo…`) wins whether it runs before
   or after Koan's hook; and the owner is the registered `IApplicationDiscriminator`, the default ASP.NET's own
   `DataProtectionOptionsSetup` reads, so an application's `SetApplicationName` replaces the remembered name in either
   order. When the setting is unset the owner answers the host's own discriminator, so nothing changes.
4. **The folder is proved usable at start**, not at the first sign-in: `WebModule.Start` creates it (owner-only,
   0700, on Unix, as `LocalFileClassificationKeyProvider` restricts its keys; Windows inherits the parent's ACL),
   writes and removes a probe file whose name is unique to the attempt (a probe that cannot be removed is left
   behind, not a refusal), settles the remembered name, and protects one value with the provider in force; when
   Koan's ring is the one in force, that loads it and writes the first key into the folder, and when the application
   replaced the provider (`UseEphemeralDataProtectionProvider`, or its own `IDataProtectionProvider`) it proves only
   that provider. A folder that cannot be created, written or used refuses the start with a correction naming the
   key, the configured value, the resolved path, the cause and the remedy (a folder this process may create and
   write, such as a persistent volume mounted in the container; or unset the key).
5. **The host states what is in effect** in the composition facts (`koan.web.keyring`), read from the resolved
   `IDataProtectionProvider`, `KeyManagementOptions` and `DataProtectionOptions`, and so do `KoanWebKeyRing.Directory`,
   `ApplicationName` and `IsPersisted`. The configured repository holds the ring only when the provider in force
   protects with a key that repository holds: the owner protects one value and looks for the key id ASP.NET writes
   after its payload header among the repository's keys. The fact states the folder and the application name, and
   whether the folder remembers it or the setting pins it; when the application's own repository or name replaced
   Koan's, the folder Koan was given and what the application put in its place; when the application's own provider
   is in force, that no key it uses persists in the folder (ASP.NET's own start-up may still write an unused key
   there); when Koan's setting is unset, the application's own repository if it configured one, otherwise that
   the ring lives where ASP.NET keeps it by default, which a rebuilt container does not keep. An existing folder whose
   mode is wider than owner-only keeps its mode and is named in the fact. The boot report carries both settings when
   they are set.

Protection of the keys at rest stays the operator's: a file-system ring without an encryptor is written in clear,
as ASP.NET's own `PersistKeysToFileSystem` writes it, and the folder's permissions are its protection. An
application that needs an encryptor (DPAPI, a certificate, a key vault) adds one with ASP.NET's own
`IDataProtectionBuilder` calls; Koan does not choose one.

## Compatibility

- Additive. An application that does not set `Koan:Web:KeyRingDirectory` composes exactly as before, plus one fact.
- `WebModule.Register` calls `AddDataProtection()` and registers the owner as `IApplicationDiscriminator`, delegating
  to the host's own discriminator while the setting is unset. The Web Auth and Auth.Server modules already called
  `AddDataProtection()`; in a host that had neither, the Data Protection services are now registered and stay unused.
- An application that persists the ring itself keeps working and keeps its repository and name, whether or not it
  also sets the key; the fact says so.
- `KoanWebOptions.KeyRingDirectory` and `KeyRingApplicationName`, `KoanWebKeyRing` and the `koan.web.keyring` fact are
  added. No signature changes.

## Evidence

- `KeyRingSpec` (Koan.Web.WellKnown.Tests): with the key pointed at a temp folder, `key-*.xml` exists there after
  start and no probe is left, the fact names the folder, the key and the remembered name, and a value the first host
  protected is unprotected by a second host over the same folder; a host renamed from `Alpha Site` to `Beta Site`
  keeps `alpha-site` and unprotects the first host's value; a pinned name is written to the folder and kept by an
  unpinned host, and a different pin refuses the start naming both names; an application's `PersistKeysToFileSystem`
  and `SetApplicationName`, registered before or after `AddKoan()`, hold the ring, and `Directory`, `ApplicationName`
  and the fact report them beside Koan's folder; `UseEphemeralDataProtectionProvider`, before or after, is reported as
  the application's provider with nothing persisted, and a value it protected cannot be read by a later host over the
  folder; a relative value resolves against the content root; a file where the
  folder should be refuses the start with "could not be created", a folder denied writes (a deny rule on Windows,
  mode 0500 on Unix, skipped when the process's privileges bypass it) with "cannot be written", and a folder holding
  a key file that is not XML with "cannot hold the ring", each naming the key, the path and the remedy; on Unix a new
  folder is 0700 and an existing 0755 folder keeps its mode and is named in the fact (skipped on Windows); unset, the
  owner reports no folder and the fact says the ring is not persisted by Koan.
- Two owners with different names settling one empty folder at once, through the real `Remember`, agreed on the
  name the file holds in 10,000 of 10,000 runs on Windows (NTFS) and 20,000 of 20,000 in a Linux container, with no
  refusal; the same race over a temporary file and a non-overwriting move had disagreed or refused on both.
- `BaseUrlPathBaseSpec` and `BaseUrlPostureSpec` (Koan.Web.WellKnown.Tests) still pass through the same host, so the
  start hook adds nothing to a host that names no folder.
