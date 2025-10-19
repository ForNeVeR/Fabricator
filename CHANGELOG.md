<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Changelog
=========
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
[Unreleased]: https://github.com/ForNeVeR/Fabricator/compare/v0.1.0...HEAD
