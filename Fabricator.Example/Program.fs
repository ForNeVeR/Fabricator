// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Example

open System
open System.IO

open Fabricator.Console
open Fabricator.Resources.Archive
open Fabricator.Resources.Downloads
open Fabricator.Resources.Files
open Fabricator.Resources.Hash
open type Fabricator.Resources.WindowsServices
open TruePath

// Updatable parameters:
let readeckVersion = "0.20.3"
let readeckHash = Sha256 "BF9DDAA5541D59FC57243FDE4340FC5A2393456E13A2C2C150686943B24C1BE1"

let shawlVersion = "1.7.0"
let shawlHash = Sha256 "EAA4FED710E844CC7968FDB82E816D406ED89C4486AB34C3E5DB2DA7E5927923"

// Calculated parameters:
let fileName(uri: Uri) = nonNull <| Path.GetFileName uri.LocalPath

let readeckUrl = Uri $"https://codeberg.org/readeck/readeck/releases/download/{readeckVersion}/readeck-{readeckVersion}-windows-amd64.exe"
let readeckBinDir = AbsolutePath @"C:\Programs\readeck"
let readeckFileName = fileName readeckUrl
let readeckDataDir = AbsolutePath @"C:\ProgramData\readeck"
let readeckExecutable = readeckBinDir / readeckFileName

let cacheDir = AbsolutePath @"T:\Temp\fabricator\download-cache"
let shawlUrl = Uri $"https://github.com/mtkennerly/shawl/releases/download/v{shawlVersion}/shawl-v{shawlVersion}-win64.zip"
let shawlLogDir = readeckDataDir / "shawl"
let shawlDownloadCache = cacheDir / fileName shawlUrl
let shawlExecutable = AbsolutePath @"C:\Programs\shawl\shawl.exe"

let installReadeck = [
    downloadFile(readeckUrl, readeckHash, readeckExecutable)
]

let installShawl = [
    downloadFile(shawlUrl, shawlHash, shawlDownloadCache)
    unpackArchive(shawlDownloadCache, shawlHash, shawlExecutable.Parent.Value)
    ensureFileExists shawlExecutable
]

let joinCommandLine(args: string seq): string =
    args
    |> Seq.map(fun a ->
        if a.Contains ' '
        then "\"" + a + "\"" // NOTE: This is not full formatting, but should be enough for our case
        else a
    )
    |> String.concat " "

let installReadeckService = [
    yield! installShawl
    createDirectory shawlLogDir
    createWindowsService(
        name = "readeck",
        account = @"NT AUTHORITY\Network Service",
        commandLine = joinCommandLine [
            shawlExecutable.Value
            "run"
            "--name"; "readeck"
            "--cwd"; readeckDataDir.Value
            "--log-dir"; shawlLogDir.Value
            "--"
            readeckExecutable.Value
            "serve"
        ]
    )
]

let private readeckService = [
    yield! installReadeck
    createDirectory readeckDataDir
    yield! installReadeckService
]

let private resources = [
    yield! readeckService
]

[<EntryPoint>]
let main(args: string[]): int =
    EntryPoint.main args resources
