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
- The output of the external commands run by `Chocolatey.chocolateyPackage` and `DotNetTool.Install` is now streamed to the resource log.
- `Downloads.downloadFile` no longer buffers the whole file in memory before saving it, and reports the download progress.

### Added
- All the bundled resources accept an optional `dependsOn` parameter to declare their dependencies.
- `Resource.NoDependencies` for resources without dependencies.
- `ConcurrencyGroup` and `Resource.Lock`: resources from the same concurrency group are never checked or applied concurrently. `HostsFile.Record` (per hosts file), `Chocolatey.chocolateyPackage` and `DotNetTool.Install` (per installation path) use their own groups, available as `HostsFile.ConcurrencyGroup`, `Chocolatey.ConcurrencyGroup` and `DotNetTool.ConcurrencyGroup`.
- `IReporter`, `IProgressReporter`, `ProgressUnit` and `ResourceContext` to report the status, progress, and log of resource checks and applications.
- `Downloads.downloadFile` accepts an optional `readTimeout` (100 seconds by default): the download fails if no data is received for that long.
- A live progress display in interactive terminals, showing the overall progress of the execution and the running resource checks and applications with their statuses and progress.
- The logs of resource checks and applications are now printed in order: the lines of different resources are never interleaved.
- After `check` and `apply` have finished, a report listing every processed resource with its final state (already applied, not applied, applied, skipped, or failed) is printed. The states are marked with emoji, or with ASCII markers (`[=]`, `[ ]`, `[x]`, `[-]`, `[!]`) if the output doesn't support Unicode. Under each resource, the report shows the change it requires (`check`) or has made (`apply`): a description, or a text diff (e.g. of a file content). Pass `--brief` (e.g. `check --brief`) to only print the resource states. The report ends with a short summary of the number of resources in each state.
- `ResourceChange` and `Diff` to describe the changes of the resources. All the bundled resources describe their changes: `Files.file` and `HostsFile.Record` show the diffs of the file contents (binary files are described with their sizes), `WindowsServices.createWindowsService` shows the diff of the service properties, and the rest describe their changes in words.
- Ctrl+C support in `EntryPoint.main`: on the first Ctrl+C, no new resources are processed, the running ones are notified via their `Async.CancellationToken`, and the program exits with code 2 after all of them have finished. A second Ctrl+C terminates the program immediately.

### Fixed
- The `check` command now reports "Checking the current environment." instead of "Applying changes to the current environment." in the beginning.

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
