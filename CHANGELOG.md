<!--
SPDX-FileCopyrightText: 2024-2026 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Changelog
=========
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Changed
- **(Breaking change!)** The `IResource` interface is replaced with the `Resource` record. Resources are compared by reference: the same resource object is processed at most once, while different resource objects are always processed separately. A resource declares the resources it depends on via `DependsOn` (use `Resource.NoDependencies` for resources without dependencies).
- **(Breaking change!)** The bundled resource factories from the `Archive`, `Chocolatey`, `Downloads`, `Files` and `WindowsCertificates` modules are now static members of the types with the same names (use `open type` instead of `open`, e.g. `open type Fabricator.Resources.Files`). `FileSource`, `CertificateStoreLocation` and `CertificateStores` are moved to the `Fabricator.Resources` namespace.
- **(Breaking change!)** The `Files.FileResource` class is replaced with the `Files.file` method.
- **(Breaking change!)** `WindowsCertificates.trustedCertificate` now takes tupled arguments instead of curried ones.
- **(Breaking change!)** Resources are now checked and applied in parallel. The order of the resources passed to `EntryPoint.main` no longer defines the order of their processing; use `DependsOn` to declare which resources have to be applied before others.
- **(Breaking change!)** `check` now reports that not all resources are applied (exit code 3) if any of the dependencies is not applied, even if the root resources are.
- **(Breaking change!)** If a resource fails to be checked or applied during `apply`, the resources depending on it are skipped, while independent resources continue to be processed (previously, the processing stopped on the first failure).
- **(Breaking change!)** `Resource.AlreadyApplied` and `Resource.Apply` now receive a `ResourceContext`, allowing the resource to report the status, progress, and log of the operation via `ResourceContext.Reporter`. Use `ResourceContext.Null` to call these functions directly.
- **(Breaking change!)** `Resource.AlreadyApplied` and `Resource.Apply` now return a `ResourceChange` instead of `bool` and `unit`: `AlreadyApplied` returns `NoChanges` if the resource is already applied, or the change required to apply it otherwise; `Apply` returns the change it has made. A change is either a `TextDiff`, a human-readable `NamedChange`, or `ChangeWithNoDescription`.
- **(Breaking change!)** `Resource` has a new `Secrets` field listing the secrets the resource requires (use `Resource.NoSecrets` for resources without secrets), and `ResourceContext` has a new `Secrets` field passing them to the resource checks and applications.
- The output of the external commands run by `Chocolatey.chocolateyPackage` and `DotNetTool.Install` is now streamed to the resource log.
- `Downloads.downloadFile` no longer buffers the whole file in memory before saving it, and reports the download progress.
- The check of `HostsFile.Record` now fails if the hosts file does not exist, same as its application (previously, the resource was reported as not applied).

### Added
- All the bundled resources accept an optional `dependsOn` parameter to declare their dependencies.
- `Resource.NoDependencies` for resources without dependencies.
- `ConcurrencyGroup` and `Resource.Lock`: resources from the same concurrency group are never checked or applied concurrently. `HostsFile.Record` (per hosts file), `Chocolatey.chocolateyPackage` and `DotNetTool.Install` (per installation path) use their own groups, available as `HostsFile.ConcurrencyGroup`, `Chocolatey.ConcurrencyGroup` and `DotNetTool.ConcurrencyGroup`.
- `IReporter`, `IProgressReporter`, `ProgressUnit` and `ResourceContext` to report the status, progress, and log of resource checks and applications.
- `Downloads.downloadFile` accepts an optional `readTimeout` (100 seconds by default): the download fails if no data is received for that long.
- A live progress display in interactive terminals, showing the overall progress of the execution and the running resource checks and applications with their statuses and progress.
- The logs of resource checks and applications are now printed in order: the lines of different resources are never interleaved.
- After `check` and `apply` have finished, a report listing every processed resource with its final state (already applied, not applied, applied, skipped, or failed) is printed. The states are marked with emoji, or with ASCII markers (`[=]`, `[ ]`, `[x]`, `[-]`, `[!]`) if the output doesn't support Unicode. Under each resource, the report shows the change it requires (`check`) or has made (`apply`): a description, or a text diff (e.g. of a file content). Pass `--brief` (e.g. `check --brief`) to only print the resource states. The report ends with a short summary of the number of resources in each state.
- `ResourceChange` and `Diff` to describe the changes of the resources. All the bundled resources describe their changes: `Files.file` and `HostsFile.Record` show the diffs of the file contents (binary files and files larger than 1 MiB are described with their sizes), `WindowsServices.createWindowsService` shows the diff of the service properties, and the rest describe their changes in words.
- Secret stores: `ISecretStore`, `ISecretStoreScope`, `ISecretStoreUi`, `SecretStoreContext`, `SecretName`, `SecretRef`, `SecretRequest` and `SecretRequestMode`. A `SecretRef` references a secret by its store and name; resources requiring secrets from the user accept them as `SecretRef`s. A resource lists the secrets it requires in `Resource.Secrets` as `SecretRequest`s: a `SecretRef` with the operations requiring it (check, application, or both). Before checking or applying anything, Fabricator opens all the required stores one by one (a store may ask the user for input while opening), and closes them at the end. Every secret is fetched right before the operation requiring it, passed via `ResourceContext.Secrets`, and disposed right after the operation.
- `PlainSecretStore`, a secret store asking the user to enter every required secret when it's opened. **It is not secure**: the secrets are kept in the process memory for the whole session. Only use it for simple cases.
- New package `FVNever.Fabricator.Secrets.KeePass` with `KeePassSecretStore`, a secret store reading the secrets from a KeePass (KDBX) database. It asks for the master password of the database when opened. A secret is looked up by the entry title, and its value is the entry's password. Entries in the recycle bin are ignored, and getting a secret fails if several entries have the same title.
- `Secrets.Materialize` and `Secrets.OfString` to convert between `SecureString` and `string`.
- A new resource, `DigitalOceanDns.Domain`, managing all the DNS records of a domain hosted by [DigitalOcean DNS](https://docs.digitalocean.com/products/networking/dns/), with the API token taken from a secret store (passed as a `SecretRef`). The records not declared are deleted, except the SOA and NS records of the domain itself, which are managed by DigitalOcean. The changes are shown as a diff of the records in the zone file format.
- The console asks for the secrets without echoing them. If the standard input is redirected, the secrets are read from it, one per line.
- `--dry-run` flag for `check` and `apply` (e.g. `apply --dry-run`): only prints the list of the checks and applications the command would perform, in the order of the report, marked with `⏸️` (or `[ ]` if the output doesn't support Unicode). No resource is checked or applied and no secret store is opened. Use it for a quick CI check that the script compiles and is able to call the actions.
- Ctrl+C support in `EntryPoint.main`: on the first Ctrl+C, no new resources are processed, the running ones are notified via their `Async.CancellationToken`, and the program exits with code 2 after all of them have finished. A second Ctrl+C terminates the program immediately.

### Fixed
- The `check` command now reports "Checking the current environment." instead of "Applying changes to the current environment." in the beginning.
- `HostsFile.Record` no longer puts the entry split from a multi-host line to the beginning of the hosts file: it now goes right after the line it was split from.
- `DotNetTool.Install` no longer fails to check the tool if the installation path contains no tools or several tools.

## [0.5.0] - 2026-01-11
### Added
- A new resource, `DotNetTool.Install`, managing the installed [.NET tools](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools).

## [0.4.0] - 2026-01-04
### Added
- [#82](https://github.com/ForNeVeR/Fabricator/issues/82): new `WindowsCertificates.trustedCertificate` resource to install X.509 certificates to Windows certificate stores.

### Changed
- [#80](https://github.com/ForNeVeR/Fabricator/issues/80): improved output in case of SHA-256 mismatch on file download.

## [0.3.0] - 2025-12-31
### Changed
- Update the dependencies.

### Added
- New resource `HostsFile.Record` to control the `hosts` file content.

## [0.2.0] - 2025-10-19
### Fixed
- `EntryPoint.main` now works correctly when invoked with `fsi.CommandLineArgs`.
- `FileResource` will create the target directory when needed.

### Added
- A new `Fabricator.Resources.Chocolatey` module to work with Chocolatey packages on Windows.

## [0.1.0] - 2025-10-19
This is the first published version of Fabricator. Added the following three packages:
- **FVNever.Fabricator.Console** for functions related to command-line argument handling and task execution,
- **FVNever.Fabricator.Core** for core interfaces,
- **FVNever.Fabricator.Resource** for bundled resource implementations.

The following resource types are supported:
- unpacked archive contents,
- files downloaded from the internet,
- asserts that a file exists,
- empty directories,
- files generated from templates or copied from other locations,
- Windows services.

[0.1.0]: https://github.com/ForNeVeR/Fabricator/releases/tag/v0.1.0
[0.2.0]: https://github.com/ForNeVeR/Fabricator/compare/v0.1.0...v0.2.0
[0.3.0]: https://github.com/ForNeVeR/Fabricator/compare/v0.2.0...v0.3.0
[0.4.0]: https://github.com/ForNeVeR/Fabricator/compare/v0.3.0...v0.4.0
[0.5.0]: https://github.com/ForNeVeR/Fabricator/compare/v0.4.0...v0.5.0
[Unreleased]: https://github.com/ForNeVeR/Fabricator/compare/v0.5.0...HEAD
