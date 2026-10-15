# SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
#
# SPDX-License-Identifier: MIT

<#
.SYNOPSIS
    Generates the KeePass test databases using KeePassXC CLI.
.DESCRIPTION
    Creates two databases, to test the KeePass secret store against databases produced by an independent
    implementation:
    - keepass-v3.kdbx (KDBX 3.1): a regular entry, and two entries with the same title in different groups;
    - keepass-v4.kdbx (KDBX 4.1): a regular entry, and an entry moved to the recycle bin. KeePassXC upgrades a database
      to KDBX 4.1 when an entry is moved to the recycle bin, so this case cannot be a part of the KDBX 3.1 database.

    The generated files are not byte-to-byte reproducible: KeePassXC randomizes the seeds, the encryption IV, and the
    entry UUIDs on each run.

    Keep the values in sync with Fabricator.Tests/Secrets/KeePassSecretStoreTests.fs.
.PARAMETER OutputDirectory
    Path to the directory to generate the databases in. Existing files are overwritten.
.PARAMETER KeePassXcCli
    Path to the keepassxc-cli executable. Detected automatically if not passed.
#>
param(
    [string] $OutputDirectory = "$PSScriptRoot/../Fabricator.Tests/TestData",
    [string] $KeePassXcCli
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

$masterPassword = 'test-master-password'
$entryTitle = 'Sample Entry'
$entryUserName = 'sample-user'
$entryPassword = 'sample-password'
$groupName = 'Group'
$duplicateTitle = 'Duplicate Entry'
$recycledTitle = 'Recycled Entry'

if (!$KeePassXcCli) {
    $command = Get-Command keepassxc-cli -CommandType Application -ErrorAction Ignore | Select-Object -First 1
    $candidates = @(
        if ($command) { $command.Source }
        'C:\Program Files\KeePassXC\keepassxc-cli.exe'
        '/Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli'
    )
    $KeePassXcCli = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$KeePassXcCli) {
        throw "Cannot find keepassxc-cli. Install KeePassXC or pass the path via -KeePassXcCli."
    }
}
Write-Host "Using KeePassXC CLI: $KeePassXcCli"

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

function New-Database {
    param(
        [string] $FileName,
        [int] $MajorVersion,
        [int] $MinorVersion,
        [scriptblock] $AddEntries,
        [string[]] $ExpectedEntries
    )

    $path = Join-Path $OutputDirectory $FileName
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path
    }

    Write-Host "Creating database `"$path`"…"
    # The new database password is read twice: the second time is the confirmation.
    "$masterPassword`n$masterPassword" | & $KeePassXcCli db-create --quiet --set-password $path

    Write-Host "Adding entry `"$entryTitle`"…"
    # The database password first, then the password for the new entry.
    "$masterPassword`n$entryPassword" |
        & $KeePassXcCli add --quiet --username $entryUserName --password-prompt $path $entryTitle

    & $AddEntries $path

    Write-Host 'Verifying the database format…'
    $expectedHeader = [byte[]] @(
        0x03, 0xD9, 0xA2, 0x9A # Signature 1
        0x67, 0xFB, 0x4B, 0xB5 # Signature 2 (KeePass 2.x)
        $MinorVersion, 0x00
        $MajorVersion, 0x00
    )
    $stream = [IO.File]::OpenRead($path)
    try {
        $header = [byte[]]::new($expectedHeader.Length)
        $stream.ReadExactly($header)
    } finally {
        $stream.Dispose()
    }
    if ([Convert]::ToHexString($header) -ne [Convert]::ToHexString($expectedHeader)) {
        throw "Unexpected database header: $([Convert]::ToHexString($header)), " +
            "expected KDBX $MajorVersion.${MinorVersion}: $([Convert]::ToHexString($expectedHeader))."
    }

    Write-Host 'Verifying the entry…'
    $actualPassword = $masterPassword |
        & $KeePassXcCli show --quiet --show-protected --attributes Password $path $entryTitle
    if ($actualPassword -ne $entryPassword) {
        throw "Unexpected entry password read back from the database: `"$actualPassword`"."
    }

    Write-Host 'Verifying the database structure…'
    $actualEntries = $masterPassword | & $KeePassXcCli ls --quiet --recursive --flatten $path
    foreach ($entry in $ExpectedEntries) {
        if ($actualEntries -notcontains $entry) {
            throw "Entry `"$entry`" not found in the database. Actual structure:`n$($actualEntries -join "`n")"
        }
    }

    Write-Host "KDBX $MajorVersion.$MinorVersion database generated successfully: `"$path`"."
}

New-Database -FileName 'keepass-v3.kdbx' -MajorVersion 3 -MinorVersion 1 -AddEntries {
    param([string] $path)
    Write-Host "Adding entries `"$duplicateTitle`" and `"$groupName/$duplicateTitle`"…"
    $masterPassword | & $KeePassXcCli mkdir --quiet $path $groupName
    $masterPassword | & $KeePassXcCli add --quiet $path $duplicateTitle
    $masterPassword | & $KeePassXcCli add --quiet $path "$groupName/$duplicateTitle"
} -ExpectedEntries @(
    $entryTitle
    $duplicateTitle
    "$groupName/$duplicateTitle"
)

New-Database -FileName 'keepass-v4.kdbx' -MajorVersion 4 -MinorVersion 1 -AddEntries {
    param([string] $path)
    Write-Host "Adding entry `"$recycledTitle`" and moving it to the recycle bin…"
    $masterPassword | & $KeePassXcCli add --quiet $path $recycledTitle
    $masterPassword | & $KeePassXcCli rm --quiet $path $recycledTitle
} -ExpectedEntries @(
    $entryTitle
    "Recycle Bin/$recycledTitle"
)
