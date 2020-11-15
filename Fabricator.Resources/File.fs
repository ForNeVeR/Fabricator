module Fabricator.Resources.File

open System
open System.IO

open Fabricator.Core

type FileSource =
    | ContentFile of relativePath: string
    | AbsoluteFile of absolutePath: string
    | GeneratedContent of name: string * (unit -> byte[])

let private resourceName source =
    match source with
    | ContentFile path | AbsoluteFile path ->
        Path.GetFileName path
    | GeneratedContent(name, _) -> name

let private readAllBytesAsync path = async {
    let! ct = Async.CancellationToken
    return! Async.AwaitTask <| File.ReadAllBytesAsync(path, ct)
}

let private writeAllBytesAsync path bytes = async {
    let! ct = Async.CancellationToken
    return! Async.AwaitTask(File.WriteAllBytesAsync(path, bytes, ct))
}

let private getContent source =
    match source with
    | AbsoluteFile path ->
        readAllBytesAsync path
    | ContentFile path ->
        let filePath = Path.Combine(FrameworkUtil.getApplicationDirectoryPath(), path)
        readAllBytesAsync filePath
    | GeneratedContent(_, generator) ->
        generator() |> async.Return

let private arraysEqual a b =
    ReadOnlySpan(a).SequenceEqual(ReadOnlySpan(b))

type FileResource(source: FileSource, targetAbsolutePath: string) =
    interface IResource with
        member _.PresentableName = resourceName source
        member _.AlreadyApplied() = async {
            if not(File.Exists targetAbsolutePath) then return false else
            let! ct = Async.CancellationToken
            let! existingContent = Async.AwaitTask <| File.ReadAllBytesAsync(targetAbsolutePath, ct)
            let! actualContent = getContent source
            return arraysEqual existingContent actualContent
        }
        member _.Apply() = async {
            let! ct = Async.CancellationToken
            let! content = getContent source
            do! writeAllBytesAsync targetAbsolutePath content
        }
