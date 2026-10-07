// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Resources.Downloads

open System
open System.Net.Http
open Fabricator.Core
open Fabricator.Resources.Hash
open TruePath
open TruePath.SystemIo

let private calcHash(path: AbsolutePath) = async {
    if not(path.Exists()) then return None
    else
        let! result = Sha256Hash.OfFile path
        return Some result
}

let downloadFile(uri: Uri, expectedHash: Sha256Hash, downloadPath: AbsolutePath): Resource =
    {
        PresentableName = $"Download file from {uri} to {downloadPath}"
        DependsOn = Resource.NoDependencies
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
