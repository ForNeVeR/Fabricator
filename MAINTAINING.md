<!--
SPDX-FileCopyrightText: 2024-2026 Friedrich von Never <friedrich@fornever.me>

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
6. Prepare a corresponding entry in the `CHANGELOG.md` file (usually by renaming the "Unreleased" section).
7. Merge the aforementioned changes via a pull request.
8. Push a tag in form of `v<VERSION>`, e.g. `v0.0.0`. GitHub Actions will do the rest (push the NuGet packages).
9. If you were publishing a new package, don't forget to adjust the NuGet Trusted Publishing Policy as described in the corresponding section (switch from **push new packages and package versions** to **push only new packages versions**).

NuGet Publishing Policy
-----------------------
This repository relies on [NuGet Trusted Publishing][docs.nuget-trusted-publishing] policy. In case you need to create it again, follow these steps:

1. Sign in to nuget.org.
2. Go to the [Trusted Publishing][nuget.trusted-publishing] section.
3. Create a new policy:
   - **Policy Name:** `github.fabricator`.
   - **Repository Owner:** `ForNeVeR`.
   - **Repository**: `Fabricator`.
   - **Workflow File**: `release.yml`.
   - **Environment:** _leave empty_.
   - **Scope:** **push only new packages versions**.
   - **Glob Patterns and Packages**: `FVNever.Fabricator.*`.

When introducing a new package, you can adjust the policy temporarily to allow it to **push new packages and package versions**.

[docs.nuget-trusted-publishing]: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
[nuget.trusted-publishing]: https://www.nuget.org/account/trustedpublishing
