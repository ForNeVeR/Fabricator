// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Example

open System
open System.IO

open Fabricator.Console
open type Fabricator.Resources.Archive
open type Fabricator.Resources.Downloads
open type Fabricator.Resources.Files
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

let installReadeck = downloadFile(readeckUrl, readeckHash, readeckExecutable)

let installShawl =
    let download = downloadFile(shawlUrl, shawlHash, shawlDownloadCache)
    let unpack = unpackArchive(shawlDownloadCache, shawlHash, shawlExecutable.Parent.Value, dependsOn = [ download ])
    ensureFileExists(shawlExecutable, dependsOn = [ unpack ])

let readeckDataDirectory = createDirectory readeckDataDir
let shawlLogDirectory = createDirectory(shawlLogDir, dependsOn = [ readeckDataDirectory ])

let joinCommandLine(args: string seq): string =
    args
    |> Seq.map(fun a ->
        if a.Contains ' '
        then "\"" + a + "\"" // NOTE: This is not full formatting, but should be enough for our case
        else a
    )
    |> String.concat " "

let private readeckService =
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
        ],
        dependsOn = [ installReadeck; installShawl; shawlLogDirectory ]
    )

let private resources = [
    readeckService
]

[<EntryPoint>]
let main(args: string[]): int =
    match args with
    | _ when args.Length > 0 && args[0] = "demo" -> EntryPoint.main args[1..] (Demo.resources(shawlUrl, shawlHash))
    | _ -> EntryPoint.main args resources
