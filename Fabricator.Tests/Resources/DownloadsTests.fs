// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.DownloadsTests

open System
open System.Diagnostics
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open Fabricator.Core
open Fabricator.Resources
open Fabricator.Resources.Hash
open TruePath
open TruePath.SystemIo

/// Starts a server that sends the response headers and a part of the body, then stalls without closing the connection.
let private startStallingServer(ct: CancellationToken): TcpListener * Task =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let serving = task {
        use! client = listener.AcceptTcpClientAsync ct
        use stream = client.GetStream()
        let request = Array.zeroCreate<byte> 4096
        let! _ = stream.ReadAsync(request, ct)
        let response = Encoding.ASCII.GetBytes "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nabc"
        do! stream.WriteAsync(response, ct)
        do! stream.FlushAsync ct
        try
            do! Task.Delay(Timeout.Infinite, ct)
        with
        | :? OperationCanceledException -> ()
    }
    listener, serving

[<Fact>]
let ``Download fails if the server stops sending data``(): Task = task {
    use cts = new CancellationTokenSource()
    let listener, serving = startStallingServer cts.Token
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    let path = Temporary.CreateTempFile()
    try
        let resource = Downloads.downloadFile(
            Uri $"http://127.0.0.1:{port}/file",
            Sha256Hash.OfString "0000000000000000000000000000000000000000000000000000000000000000",
            path,
            readTimeout = TimeSpan.FromMilliseconds 200.0
        )

        let stopwatch = Stopwatch.StartNew()
        let! ex = Assert.ThrowsAnyAsync<exn>(fun () ->
            resource.Apply ResourceContext.Null |> Async.StartAsTask :> Task
        )
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds 10.0, $"The download took {stopwatch.Elapsed}.")
        // Async.AwaitTask wraps the exceptions of the awaited tasks.
        let ex = match ex with :? AggregateException as e -> nonNull e.InnerException | e -> e
        let ex = Assert.IsType<TimeoutException> ex
        Assert.Contains("No data received", ex.Message)
    finally
        cts.Cancel()
        listener.Stop()
        if path.Exists() then path.Delete()
    do! serving
}
