// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Resources.Files

open System
open System.IO

open Fabricator.Core
open TruePath
open TruePath.SystemIo

type FileSource =
    | ContentFile of relativePath: string
    | AbsoluteFile of absolutePath: string
    | GeneratedContent of name: string * (unit -> byte[])

let private resourceName source =
    match source with
    | ContentFile path | AbsoluteFile path ->
        nonNull <| Path.GetFileName path
    | GeneratedContent(name, _) -> name

let private readAllBytesAsync path = async {
    let! ct = Async.CancellationToken
    return! Async.AwaitTask <| File.ReadAllBytesAsync(path, ct)
}

let private writeAllBytesAsync (path: string) (bytes: byte[]) = async {
    let! ct = Async.CancellationToken
    Directory.CreateDirectory(nonNull <| Path.GetDirectoryName path) |> ignore
    return! Async.AwaitTask(File.WriteAllBytesAsync(path, bytes, ct))
}

let private getContent source =
    match source with
    | AbsoluteFile path ->
        readAllBytesAsync path
    | ContentFile path ->
        let filePath = Path.Combine(Environment.CurrentDirectory, path)
        readAllBytesAsync filePath
    | GeneratedContent(_, generator) ->
        generator() |> async.Return

let private arraysEqual (a: 'a[]) (b: 'a[]) =
    ReadOnlySpan(a).SequenceEqual(ReadOnlySpan(b))

let file(source: FileSource, targetAbsolutePath: string): Resource =
    {
        PresentableName = resourceName source
        DependsOn = Resource.NoDependencies
        AlreadyApplied = fun () -> async {
            if not(File.Exists targetAbsolutePath) then return false else
            let! ct = Async.CancellationToken
            let! existingContent = Async.AwaitTask <| File.ReadAllBytesAsync(targetAbsolutePath, ct)
            let! actualContent = getContent source
            return arraysEqual existingContent actualContent
        }
        Apply = fun () -> async {
            let! content = getContent source
            do! writeAllBytesAsync targetAbsolutePath content
        }
    }

let createDirectory(path: AbsolutePath): Resource =
    {
        PresentableName = $"Directory \"{path.Value}\""
        DependsOn = Resource.NoDependencies
        AlreadyApplied = fun () -> async {
            return path.ExistsDirectory()
        }
        Apply = fun () -> async {
            path.CreateDirectory()
        }
    }

let ensureFileExists(path: AbsolutePath): Resource =
    {
        PresentableName = $"File \"{path.Value}\""
        DependsOn = Resource.NoDependencies
        AlreadyApplied = fun () -> async {
            return false
        }
        Apply = fun () -> async {
            if path.ReadKind() <> Nullable FileEntryKind.File then
                failwithf $"File \"{path}\" does not exist."
        }
    }
