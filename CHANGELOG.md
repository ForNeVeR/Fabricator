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
- **(Breaking change)** `IResource` has a new member, `DependsOn: IReadOnlySet<IResource>`, declaring the resources this one depends on. All `IResource` implementations have to provide it (use `Resource.NoDependencies` for resources without dependencies).
- **(Breaking change)** Resources are now checked and applied in parallel. The order of the resources passed to `EntryPoint.main` no longer defines the order of their processing; use `DependsOn` to declare which resources have to be applied before others.
- **(Breaking change)** The resources passed to `EntryPoint.main` are now considered the _root_ resources: their dependencies don't have to be passed explicitly and are processed only when required:
  - `check` checks the dependencies of a resource only if the resource itself is not applied;
  - `apply` checks the dependencies of a resource only if the resource itself is not applied, applies the dependencies that are not applied yet, and then applies the resource.
- **(Breaking change)** If a resource fails to be checked or applied during `apply`, the resources depending on it are skipped, while independent resources continue to be processed (previously, the processing stopped on the first failure).
- The `check` command now reports "Checking the current environment." instead of "Applying changes to the current environment." in the beginning.

### Added
- `Resource.dependsOn` function to add dependencies to any resource, including the bundled ones.
- `Resource.NoDependencies` for resources without dependencies.
- Dependency cycles are detected and reported before any resource is processed.

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
