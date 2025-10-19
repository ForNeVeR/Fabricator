<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Maintainer Guide
================

Publish a New Version
---------------------
1. Choose the new version according to the project's versioning scheme.
2. Update the project's status in the `README.md` file, if required.
3. Update the copyright statement in the `LICENSE.txt` file, if required.
4. Update the `<Copyright>` statement in the `Directory.Build.props`, if required.
5. Run the `scripts/Update-Version.ps1` script with the argument `-NewVersion <the new version>`. It will update all the various places where the new version should be used.
6. Merge the aforementioned changes via a pull request.
