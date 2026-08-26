# Code signing policy

Koan publishes NuGet packages from the
[`sylin-org/koan-framework`](https://github.com/sylin-org/koan-framework) repository. A package may
carry a publisher signature issued to **SignPath Foundation** before NuGet.org adds its repository
signature. The publisher signature does not change Koan's Apache-2.0 license or transfer project
ownership.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## Signed artifacts

- Primary `.nupkg` files selected by Koan's release plan and built by the GitHub Actions release
  workflow.
- Package IDs are limited to `Sylin.Koan` and `Sylin.Koan.*` components maintained in this
  repository.
- Symbol packages are published through the existing release path and are outside this publisher
  signing policy.

Until the SignPath subscription is active, package publication remains unsigned by the publisher.
Once repository variable `SIGNPATH_ENABLED` is `true`, a release with changed packages fails rather
than publishing an unsigned primary package.

## Team roles

- Author, committer, and reviewer: [Leo Botinelly (`@lbotinelly`)](https://github.com/lbotinelly).
  Contributions from people without commit access require review before they are merged.
- Signing approver: [Leo Botinelly (`@lbotinelly`)](https://github.com/lbotinelly).

Koan is currently a single-maintainer project, so author, reviewer, and approver are the same
person; the role separation SignPath's program normally provides is explicitly NOT present today.
Compensating controls: signing requests are bound to the GitHub trusted-build system, so only
CI builds from this repository's fast-forwarded `main` can be submitted; builds run on
GitHub-hosted runners; `main` accepts fast-forwards only; and every signing request still
requires manual approval in the SignPath portal. Adding a second approver is tracked as
project growth, not assumed.

Everyone assigned a signing role must use multi-factor authentication for both GitHub and
SignPath. Every signing request requires manual approval in SignPath.

## Build and release controls

1. The release plan selects only packages whose independently versioned source changed.
2. GitHub-hosted runners pack those projects from the fast-forwarded `main` commit.
3. The workflow uploads the complete primary-package set as a GitHub Actions artifact and submits
   it to SignPath's GitHub trusted-build connector.
4. SignPath limits signing to Koan package IDs and applies NuGet publisher signatures after manual
   approval.
5. The workflow verifies every signed package, checks that the signed set exactly matches the
   release plan, and replaces the unsigned staging copies.
6. Koan's package-only consumer proof restores, composes, builds, and runs against the finalized
   feed before the separate publication job receives the NuGet credential.

## Metadata restrictions mapping

SignPath's file-metadata restrictions apply to PE/MSI/XML artifacts; `<nupkg-file>` supports none.
The equivalent control for NuGet packages is enforced by composition instead: the artifact
configuration's wildcard admits only `Sylin.Koan.*` package files, the release plan names the
exact set of package IDs and versions allowed in this release, the finalize step verifies the
signed set matches that plan exactly, and publication consumes only the certified artifact. Any
package outside the plan cannot reach nuget.org through this pipeline.

The release workflow and packaging scripts are part of the reviewed source. Signing credentials
are stored as GitHub Actions secrets and are available only to the signing and config-validation
steps of the release workflow.

## Privacy and network behavior

Koan is an application framework. It does not transfer application information to Sylin or to a
Sylin-operated service. Restoring packages contacts the NuGet sources selected by the developer.
Applications built with Koan communicate with databases, identity providers, AI services,
message brokers, and other systems only through the capabilities and providers their developers
reference and configure; those systems retain their own privacy policies.

## System changes and removal

Installing a Koan package changes the consuming project's NuGet dependency graph and build output.
Individual capabilities may create application data or contact configured infrastructure when the
application runs. Their behavior is described in Koan's documentation and in the consuming
application's own operating policy.

Remove a package with `dotnet remove package <PackageId>`, remove the corresponding application
configuration and code, then rebuild. Remove the template package with
`dotnet new uninstall Sylin.Koan.Templates`.

## Verification

Verify a downloaded package with the .NET SDK:

```powershell
dotnet nuget verify --all .\Sylin.Koan.App.<version>.nupkg
```

A publisher-signed package must report a valid author signature whose certificate identifies
SignPath Foundation. A package downloaded from NuGet.org may additionally carry NuGet.org's
repository signature.
