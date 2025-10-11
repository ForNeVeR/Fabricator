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

type private DownloadResource(uri: Uri, expectedHash: Sha256Hash, downloadPath: AbsolutePath) =
    interface IResource with
        member _.PresentableName: string = $"Download file from {uri} to {downloadPath}"
        member _.AlreadyApplied(): Async<bool> = async {
            let! downloadedHash = calcHash downloadPath
            return downloadedHash = Some expectedHash
        }
        member _.Apply(): Async<unit> = async {
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
                failwithf $"Hash mismatch for URL \"{uri}\":\nexpected hash {expectedHash},\nactual hash   {downloadedHash}."
        }

let downloadFile(uri: Uri, hash: Sha256Hash, path: AbsolutePath): IResource =
    DownloadResource(uri, hash, path)
