// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
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
    /// <param name="readTimeout">
    /// The maximum time to wait for the next portion of the file data, after which the download fails. 100 seconds by
    /// default.
    /// </param>
    static member downloadFile(
        uri: Uri,
        expectedHash: Sha256Hash,
        downloadPath: AbsolutePath,
        ?dependsOn: Resource seq,
        ?readTimeout: TimeSpan
    ): Resource =
        let readTimeout = defaultArg readTimeout (TimeSpan.FromSeconds 100.0)
        let calcHash(path: AbsolutePath) = async {
            if not(path.Exists()) then return None
            else
                let! result = Sha256Hash.OfFile path
                return Some result
        }

        // The HttpClient timeout only covers receiving the response headers, so the body reading needs its own.
        let readChunk (input: Stream) (buffer: byte[]) (ct: CancellationToken): Task<int> = task {
            try
                return! input.ReadAsync(buffer, 0, buffer.Length, ct).WaitAsync(readTimeout, ct)
            with
            | :? TimeoutException as e ->
                return raise <| TimeoutException(
                    $"No data received from {uri} in %.1f{readTimeout.TotalSeconds}s.", e
                )
        }

        {
            PresentableName = $"Download file from {uri} to {downloadPath}"
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun ctx -> async {
                ctx.Reporter.Status "Computing hash"
                let! downloadedHash = calcHash downloadPath
                return downloadedHash = Some expectedHash
            }
            Apply = fun ctx -> async {
                let reporter = ctx.Reporter
                let stopwatch = Stopwatch.StartNew()
                downloadPath.Parent.Value.CreateDirectory()

                use httpClient = new HttpClient()
                let! ct = Async.CancellationToken

                reporter.Status $"Connecting to {uri.Authority}"
                let! response =
                    Async.AwaitTask <| httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                use response = response
                response.EnsureSuccessStatusCode() |> ignore

                let total = response.Content.Headers.ContentLength |> Option.ofNullable
                let! downloadedBytes =
                    reporter.WithProgress($"Downloading {downloadPath.FileName}", total, Bytes, fun progress -> async {
                        use! input = Async.AwaitTask <| response.Content.ReadAsStreamAsync ct
                        use output = downloadPath.OpenWrite()
                        output.SetLength 0L
                        let buffer = Array.zeroCreate<byte> 81920
                        let mutable downloadedBytes = 0L
                        let mutable finished = false
                        while not finished do
                            let! read = Async.AwaitTask(readChunk input buffer ct)
                            if read = 0 then finished <- true
                            else
                                do! Async.AwaitTask(output.WriteAsync(buffer, 0, read, ct))
                                downloadedBytes <- downloadedBytes + int64 read
                                progress.Report downloadedBytes
                        return downloadedBytes
                    })

                reporter.Status "Verifying hash"
                let! downloadedHash = calcHash downloadPath
                if downloadedHash <> Some expectedHash then
                    downloadPath.Delete()
                    let actualHash = downloadedHash |> Option.map string |> Option.defaultValue "None"
                    failwithf $"Hash mismatch for URL \"{uri}\":\nexpected hash {expectedHash},\nactual hash   {actualHash}."

                reporter.Log(
                    $"Downloaded {downloadedBytes} bytes from {uri} to \"{downloadPath.Value}\" " +
                    $"in %.2f{stopwatch.Elapsed.TotalSeconds}s."
                )
            }
        }
