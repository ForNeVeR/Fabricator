// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.Net.Http
open Fabricator.Core
open Fabricator.Resources.Hash
open Fabricator.Resources.ResourceUtil
open TruePath
open TruePath.SystemIo

type Downloads =
    /// <summary>Creates a resource downloading a file and verifying its hash.</summary>
    /// <param name="uri">The URI to download the file from.</param>
    /// <param name="expectedHash">The expected SHA-256 hash of the file.</param>
    /// <param name="downloadPath">The path to save the file to.</param>
    /// <param name="dependsOn">The resources this resource depends on.</param>
    static member downloadFile(
        uri: Uri,
        expectedHash: Sha256Hash,
        downloadPath: AbsolutePath,
        ?dependsOn: Resource seq
    ): Resource =
        let calcHash(path: AbsolutePath) = async {
            if not(path.Exists()) then return None
            else
                let! result = Sha256Hash.OfFile path
                return Some result
        }

        {
            PresentableName = $"Download file from {uri} to {downloadPath}"
            DependsOn = dependencies dependsOn
            AlreadyApplied = fun () -> async {
                let! downloadedHash = calcHash downloadPath
                return downloadedHash = Some expectedHash
            }
            Apply = fun () -> async {
                downloadPath.Parent.Value.CreateDirectory()

                use httpClient = new HttpClient()
                let! ct = Async.CancellationToken

                let! response = Async.AwaitTask <| httpClient.GetAsync(uri, ct)
                response.EnsureSuccessStatusCode() |> ignore

                let saveContent = async {
                    use resultStream = downloadPath.OpenWrite()
                    do! Async.AwaitTask(response.Content.CopyToAsync(resultStream, ct))
                }
                do! saveContent

                let! downloadedHash = calcHash downloadPath
                if downloadedHash <> Some expectedHash then
                    downloadPath.Delete()
                    let actualHash = downloadedHash |> Option.map string |> Option.defaultValue "None"
                    failwithf $"Hash mismatch for URL \"{uri}\":\nexpected hash {expectedHash},\nactual hash   {actualHash}."
            }
        }
