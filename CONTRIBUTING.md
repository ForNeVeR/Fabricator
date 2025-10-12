<!--
SPDX-FileCopyrightText: 2024-2025 Friedrich von Never <friedrich@fornever.me>

SPDX-License-Identifier: MIT
-->

Contributor Guide
=================

### Prerequisites
Fabricator requires [.NET 5 (or later) SDK][dotnet-sdk] for development.

### Build
To build the project (while automatically restoring the dependencies, if
necessary), execute the following command:

```console
$ dotnet build
```

### Test
To run the automatic test suite, execute the following command:

```console
$ dotnet test
```

### Pack
To pack the artifacts for uploading onto NuGet, execute the following command:

```console
$ dotnet pack
```

File Encoding Changes
---------------------
If the automation asks you to update the file encoding (line endings or UTF-8 BOM) in certain files, run the following PowerShell script ([PowerShell Core][powershell] is recommended to run this script):
```console
$ pwsh -c "Install-Module VerifyEncoding -Repository PSGallery -RequiredVersion 2.2.1 -Force && Test-Encoding -AutoFix"
```

The `-AutoFix` switch will automatically fix the encoding issues, and you'll only need to commit and push the changes.

[dotnet-sdk]: http://dot.net/
[powershell]: https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell
