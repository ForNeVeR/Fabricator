// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.IO

open Fabricator.Core
open Fabricator.Resources.ResourceUtil
open TruePath
open TruePath.SystemIo

type FileSource =
    | ContentFile of relativePath: string
    | AbsoluteFile of absolutePath: string
    | GeneratedContent of name: string * (unit -> byte[])

type Files =
    /// <summary>Creates a resource making sure the target file has the content from the source.</summary>
    /// <param name="source">The source of the file content.</param>
    /// <param name="targetAbsolutePath">The path of the target file.</param>
    /// <param name="dependsOn">The resources this resource depends on.</param>
    static member file(source: FileSource, targetAbsolutePath: string, ?dependsOn: Resource seq): Resource =
        let resourceName source =
            match source with
            | ContentFile path | AbsoluteFile path ->
                nonNull <| Path.GetFileName path
            | GeneratedContent(name, _) -> name

        let readAllBytesAsync path = async {
            let! ct = Async.CancellationToken
            return! Async.AwaitTask <| File.ReadAllBytesAsync(path, ct)
        }

        let writeAllBytesAsync (path: string) (bytes: byte[]) = async {
            let! ct = Async.CancellationToken
            Directory.CreateDirectory(nonNull <| Path.GetDirectoryName path) |> ignore
            return! Async.AwaitTask(File.WriteAllBytesAsync(path, bytes, ct))
        }

        let getContent source =
            match source with
            | AbsoluteFile path ->
                readAllBytesAsync path
            | ContentFile path ->
                let filePath = Path.Combine(Environment.CurrentDirectory, path)
                readAllBytesAsync filePath
            | GeneratedContent(_, generator) ->
                generator() |> async.Return

        let readExistingContent() = async {
            if not(File.Exists targetAbsolutePath) then return None
            else
                let! content = readAllBytesAsync targetAbsolutePath
                return Some content
        }

        {
            PresentableName = resourceName source
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun _ -> async {
                let! existingContent = readExistingContent()
                let! content = getContent source
                return Diffs.fileChange targetAbsolutePath existingContent content
            }
            Apply = fun ctx -> async {
                ctx.Reporter.Status "Reading the content"
                let! existingContent = readExistingContent()
                let! content = getContent source
                ctx.Reporter.Status $"Writing to \"{targetAbsolutePath}\""
                do! writeAllBytesAsync targetAbsolutePath content
                return Diffs.fileChange targetAbsolutePath existingContent content
            }
        }

    /// <summary>Creates a resource making sure the directory exists.</summary>
    /// <param name="path">The path of the directory.</param>
    /// <param name="dependsOn">The resources this resource depends on.</param>
    static member createDirectory(path: AbsolutePath, ?dependsOn: Resource seq): Resource =
        {
            PresentableName = $"Directory \"{path.Value}\""
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun _ -> async {
                return if path.ExistsDirectory() then NoChanges else NamedChange $"new directory \"{path.Value}\""
            }
            Apply = fun _ -> async {
                path.CreateDirectory()
                return NamedChange $"new directory \"{path.Value}\""
            }
        }

    /// <summary>
    /// Creates a resource asserting that the file exists. It is never considered already applied, and its application
    /// fails if the file does not exist.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="dependsOn">The resources this resource depends on, e.g. the ones that create the file.</param>
    static member ensureFileExists(path: AbsolutePath, ?dependsOn: Resource seq): Resource =
        {
            PresentableName = $"File \"{path.Value}\""
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun _ -> async {
                return ChangeWithNoDescription
            }
            Apply = fun _ -> async {
                if path.ReadKind() <> Nullable FileEntryKind.File then
                    failwithf $"File \"{path}\" does not exist."
                return NoChanges
            }
        }
