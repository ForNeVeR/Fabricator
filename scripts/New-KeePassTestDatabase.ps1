# SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
#
# SPDX-License-Identifier: MIT

<#
.SYNOPSIS
    Generates the KeePass test database using KeePassXC CLI.
.DESCRIPTION
    Creates a KDBX 3.1 database with a single entry, to test the KeePass secret store against a database produced by
    an independent implementation. The generated file is not byte-to-byte reproducible: KeePassXC randomizes the
    seeds, the encryption IV, and the entry UUID on each run.

    Keep the values in sync with Fabricator.Tests/Secrets/KeePassSecretStoreTests.fs.
.PARAMETER OutputPath
    Path to the database file to generate. An existing file is overwritten.
.PARAMETER KeePassXcCli
    Path to the keepassxc-cli executable. Detected automatically if not passed.
#>
param(
    [string] $OutputPath = "$PSScriptRoot/../Fabricator.Tests/TestData/keepass-v3.kdbx",
    [string] $KeePassXcCli
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

$masterPassword = 'test-master-password'
$entryTitle = 'Sample Entry'
$entryUserName = 'sample-user'
$entryPassword = 'sample-password'

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

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath
}
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null

Write-Host "Creating database `"$OutputPath`"…"
# The new database password is read twice: the second time is the confirmation.
"$masterPassword`n$masterPassword" | & $KeePassXcCli db-create --quiet --set-password $OutputPath

Write-Host "Adding entry `"$entryTitle`"…"
# The database password first, then the password for the new entry.
"$masterPassword`n$entryPassword" |
    & $KeePassXcCli add --quiet --username $entryUserName --password-prompt $OutputPath $entryTitle

Write-Host 'Verifying the database format…'
$expectedHeader = [byte[]] @(
    0x03, 0xD9, 0xA2, 0x9A # Signature 1
    0x67, 0xFB, 0x4B, 0xB5 # Signature 2 (KeePass 2.x)
    0x01, 0x00 # Minor version: 1
    0x03, 0x00 # Major version: 3
)
$stream = [IO.File]::OpenRead($OutputPath)
try {
    $header = [byte[]]::new($expectedHeader.Length)
    $stream.ReadExactly($header)
} finally {
    $stream.Dispose()
}
if ([Convert]::ToHexString($header) -ne [Convert]::ToHexString($expectedHeader)) {
    throw "Unexpected database header: $([Convert]::ToHexString($header)), " +
        "expected KDBX 3.1: $([Convert]::ToHexString($expectedHeader))."
}

Write-Host 'Verifying the entry…'
$actualPassword = $masterPassword |
    & $KeePassXcCli show --quiet --show-protected --attributes Password $OutputPath $entryTitle
if ($actualPassword -ne $entryPassword) {
    throw "Unexpected entry password read back from the database: `"$actualPassword`"."
}

Write-Host "KDBX 3.1 database generated successfully: `"$OutputPath`"."
