// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

/// Resources demonstrating the progress reporting features. None of them changes anything outside the demo directory.
module Fabricator.Demo

open System
open System.Collections.Immutable
open System.IO
open Fabricator.Core
open Fabricator.Resources
open Fabricator.Resources.Hash
open TruePath
open TruePath.SystemIo

let private demoDirectory = AbsolutePath(Path.GetTempPath()) / "fabricator-demo"

/// Simulates a check that takes some time, and always reports that the resource is not applied, so every apply run
/// shows the application again.
let private inspect(ctx: ResourceContext) = async {
    ctx.Reporter.Status "Inspecting…"
    do! Async.Sleep(Random.Shared.Next(300, 800))
    return false
}

let private demo (name: string) (dependsOn: Resource seq) (apply: ResourceContext -> Async<unit>): Resource = {
    PresentableName = $"Demo: {name}"
    DependsOn = ImmutableHashSet.CreateRange dependsOn
    Lock = None
    AlreadyApplied = inspect
    Apply = apply
}

let private statusOnly = demo "status only" [] (fun ctx -> async {
    for step in [ "Preparing"; "Resolving"; "Configuring"; "Finalizing" ] do
        ctx.Reporter.Status $"{step}…"
        ctx.Reporter.Log $"{step} step started."
        do! Async.Sleep 700
})

let private itemsProgress = demo "items progress" [] (fun ctx ->
    ctx.Reporter.WithProgress("Processing items", Some 40L, Items, fun progress -> async {
        for i in 1L .. 40L do
            do! Async.Sleep 100
            progress.Report i
        ctx.Reporter.Log "Processed 40 items."
    })
)

let private megabyte = 1024L * 1024L

/// Pretends to transfer 50 MB of data in 5 seconds.
let private transfer (progress: IProgressReporter) = async {
    for i in 1L .. 50L do
        do! Async.Sleep 100
        progress.Report(i * megabyte)
}

let private bytesProgress = demo "bytes progress" [] (fun ctx ->
    ctx.Reporter.WithProgress("Transferring", Some(50L * megabyte), Bytes, transfer)
)

let private bytesUnknownTotal = demo "bytes, unknown total" [] (fun ctx ->
    ctx.Reporter.WithProgress("Receiving a stream", None, Bytes, transfer)
)

let private realDownload(uri: Uri, hash: Sha256Hash) =
    let path = demoDirectory / "download" / (nonNull <| Path.GetFileName uri.LocalPath)
    let clean = demo "clean download" [] (fun ctx -> async {
        if path.Exists() then
            path.Delete()
            ctx.Reporter.Log $"Deleted \"{path.Value}\"."
    })
    Downloads.downloadFile(uri, hash, path, dependsOn = [ clean ])

let private commandOutput =
    let path = demoDirectory / "tools"
    let clean = demo "clean tools" [] (fun ctx -> async {
        if path.ExistsDirectory() then
            Directory.Delete(path.Value, recursive = true)
            ctx.Reporter.Log $"Deleted \"{path.Value}\"."
    })
    DotNetTool.Install("dotnet-trace", "10.0.731102", path, dependsOn = [ clean ])

let private chatty(name: string) = demo $"chatty {name}" [] (fun ctx -> async {
    for i in 1 .. 15 do
        ctx.Reporter.Status $"Line {i} of 15"
        ctx.Reporter.Log $"Message {i} of 15."
        do! Async.Sleep(Random.Shared.Next(50, 200))
})

let private failure = demo "failure" [] (fun ctx -> async {
    ctx.Reporter.Log "About to do something that fails."
    do! Async.Sleep 1000
    ctx.Reporter.Log "Failing now."
    failwith "Demo failure."
})

let private blockedDependent = demo "blocked dependent" [ failure ] (fun _ -> async.Return())

let private fanOut = [
    for i in 1 .. 24 ->
        demo $"fan-out {i}" [] (fun ctx -> async {
            ctx.Reporter.Status "Working…"
            do! Async.Sleep(Random.Shared.Next(1000, 3000))
        })
]

let private lockedGroup = ConcurrencyGroup "Demo"
let private locked = [
    for i in 1 .. 3 ->
        { demo $"locked {i}" [] (fun ctx -> async {
            ctx.Reporter.Status "Holding the Demo lock"
            ctx.Reporter.Log "Acquired the Demo lock."
            do! Async.Sleep 1500
          }) with Lock = Some lockedGroup }
]

/// <summary>Creates the demo resources.</summary>
/// <param name="download">The URI and the hash of a file to download from the Internet.</param>
let resources(download: Uri * Sha256Hash): Resource list =
    let chattyResources = [ for name in [ "A"; "B"; "C" ] -> chatty name ]
    let summary =
        demo "summary" [ statusOnly; itemsProgress; bytesProgress; bytesUnknownTotal; yield! chattyResources ] (fun ctx ->
            async { ctx.Reporter.Log "All the dependencies have been applied." }
        )
    [
        summary
        realDownload download
        commandOutput
        blockedDependent
        yield! fanOut
        yield! locked
    ]
