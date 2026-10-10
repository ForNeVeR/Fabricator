// SPDX-FileCopyrightText: 2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.HostsFileTests

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading.Tasks
open FSharp.Control.Tasks
open Xunit
open Fabricator.Core
open Fabricator.Resources
open TruePath
open TruePath.SystemIo

// Helper functions for testing
let private withTempFile (action: AbsolutePath -> Task<'a>): Task<'a> = task {
    let tempPath = Temporary.CreateTempFile()
    try
        return! action tempPath
    finally
        tempPath.Delete()
}

let private testAlreadyApplied (hostsContent: string) (ipAddress: string) (host: string) =
    withTempFile (fun path -> task {
        do! path.WriteAllTextAsync(hostsContent)
        let resource = HostsFile.Record(ipAddress, host, path)
        return! resource.AlreadyApplied ResourceContext.Null
    })

let private testApply (hostsContent: string) (ipAddress: string) (host: string) (expectedHostsContent: string) =
    withTempFile (fun path -> task {
        do! path.WriteAllTextAsync(hostsContent)
        let resource = HostsFile.Record(ipAddress, host, path)
        let! _ = resource.Apply ResourceContext.Null
        let! actualContent = path.ReadAllTextAsync()
        Assert.Equal(expectedHostsContent, actualContent)
    })

let private testApplyAndRead (hostsContent: string) (ipAddress: string) (host: string) =
    withTempFile (fun path -> task {
        do! path.WriteAllTextAsync(hostsContent)
        let resource = HostsFile.Record(ipAddress, host, path)
        let! _ = resource.Apply ResourceContext.Null
        return! path.ReadAllTextAsync()
    })

[<Fact>]
let ``PresentableName returns correct format``(): Task = task {
    let tempPath = Temporary.CreateTempFile()
    try
        let resource = HostsFile.Record("127.0.0.1", "example.com", tempPath)
        Assert.Equal("Host file entry \"example.com\"", resource.PresentableName)
    finally
        tempPath.Delete()
}

[<Fact>]
let ``AlreadyApplied returns false when host file does not exist``(): Task = task {
    let tempPath = Temporary.CreateTempFile()
    tempPath.Delete() // Delete to make it non-existent
    let resource = HostsFile.Record("127.0.0.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied ResourceContext.Null
    Assert.NotEqual(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied returns false when entry does not exist``(): Task = task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 router\n" "10.0.0.1" "newhost.com"
    Assert.NotEqual(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied returns true when exact entry exists``(): Task = task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 example.com\n" "192.168.1.1" "example.com"
    Assert.Equal(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied returns false when host exists with different IP``(): Task = task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 example.com\n" "10.0.0.1" "example.com"
    Assert.NotEqual(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied handles whitespace variations``(): Task = task {
    let! result = testAlreadyApplied "192.168.1.1    example.com\n" "192.168.1.1" "example.com"
    Assert.Equal(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied ignores comment lines``(): Task = task {
    let! result = testAlreadyApplied "# 192.168.1.1 example.com\n127.0.0.1 localhost\n" "192.168.1.1" "example.com"
    Assert.NotEqual(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied handles inline comments``(): Task = task {
    let! result = testAlreadyApplied "192.168.1.1 example.com # this is a comment\n" "192.168.1.1" "example.com"
    Assert.Equal(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied handles multi-host entries``(): Task = task {
    let! result = testAlreadyApplied "192.168.1.1 example.com another.com\n" "192.168.1.1" "example.com"
    Assert.Equal(NoChanges, result)
}

[<Fact>]
let ``Apply adds new entry when host does not exist``(): Task = task {
    let! content = testApplyAndRead "127.0.0.1 localhost\n" "192.168.1.1" "example.com"
    Assert.Contains("192.168.1.1 example.com", content)
    Assert.Contains("127.0.0.1 localhost", content)
}

[<Fact>]
let ``Apply replaces existing entry with different IP``(): Task = task {
    let! content = testApplyAndRead "127.0.0.1 localhost\n192.168.1.1 example.com\n" "10.0.0.1" "example.com"
    Assert.Contains("10.0.0.1 example.com", content)
    Assert.DoesNotContain("192.168.1.1 example.com", content)
}

[<Fact>]
let ``Apply splits multi-host entry when updating IP``(): Task = task {
    let! content = testApplyAndRead "192.168.1.1 example.com another.com\n" "10.0.0.1" "example.com"
    Assert.Contains("10.0.0.1 example.com", content)
    Assert.Contains("192.168.1.1 another.com", content)
    Assert.DoesNotContain("192.168.1.1 example.com another.com", content)
}

[<Fact>]
let ``Apply splits multi-host entry with inline comment``(): Task = task {
    let! content = testApplyAndRead "192.168.1.1 example.com another.com # production hosts\n" "10.0.0.1" "example.com"
    Assert.Contains("10.0.0.1 example.com", content)
    Assert.Contains("192.168.1.1 another.com", content)
    // The inline comment is not expected to be preserved when splitting
    Assert.DoesNotContain("192.168.1.1 example.com another.com", content)
}

[<Fact>]
let ``Apply preserves inline comments``(): Task = task {
    let! content = testApplyAndRead "127.0.0.1 localhost # default\n" "192.168.1.1" "newhost.com"
    Assert.Contains("127.0.0.1 localhost # default", content)
    Assert.Contains("192.168.1.1 newhost.com", content)
}

[<Fact>]
let ``Apply fails when hosts file does not exist``(): Task = task {
    let nonExistentPath = Temporary.CreateTempFile()
    nonExistentPath.Delete() // Delete to make it non-existent
    let resource = HostsFile.Record("192.168.1.1", "example.com", nonExistentPath)
    let! ex = Assert.ThrowsAsync<Exception>(fun () -> Async.StartAsTask(resource.Apply ResourceContext.Null) :> Task)
    Assert.Contains("Hosts file not found", ex.Message)
}

[<Fact>]
let ``Apply adds new entry when different host has same IP``(): Task = task {
    let! content = testApplyAndRead "192.168.1.1 existinghost.com\n" "192.168.1.1" "newhost.com"
    // Should add a new line since the host doesn't exist yet
    Assert.Contains("192.168.1.1 existinghost.com", content)
    Assert.Contains("192.168.1.1 newhost.com", content)
}

[<Fact>]
let ``Apply is idempotent``(): Task = task {
    do! withTempFile (fun path -> task {
        do! path.WriteAllTextAsync("127.0.0.1 localhost\n")
        let resource = HostsFile.Record("192.168.1.1", "example.com", path)
        let! _ = resource.Apply ResourceContext.Null
        let! _ = resource.Apply ResourceContext.Null
        let! content = path.ReadAllTextAsync()
        let lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        let matchingLines = lines |> Array.filter (fun line -> line.Contains("example.com"))
        Assert.Single(matchingLines) |> ignore
    })
}

[<Fact>]
let ``Apply and AlreadyApplied work together``(): Task = task {
    do! withTempFile (fun path -> task {
        do! path.WriteAllTextAsync("127.0.0.1 localhost\n")
        let resource = HostsFile.Record("192.168.1.1", "example.com", path)
        let! beforeApply = resource.AlreadyApplied ResourceContext.Null
        Assert.NotEqual(NoChanges, beforeApply)
        let! _ = resource.Apply ResourceContext.Null
        let! afterApply = resource.AlreadyApplied ResourceContext.Null
        Assert.Equal(NoChanges, afterApply)
    })
}

[<Fact>]
let ``DefaultHostsPath returns Windows path on Windows``(): unit =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        let path = HostsFile.DefaultHostsPath
        let systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System)
        let expectedPath = Path.Combine(systemFolder, @"drivers\etc\hosts")
        Assert.Equal(expectedPath, path.Value)

[<Fact>]
let ``DefaultHostsPath returns Unix path on non-Windows``(): unit =
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        let path = HostsFile.DefaultHostsPath
        Assert.Equal("/etc/hosts", path.Value)

[<Fact>]
let ``Records for the same hosts file share a concurrency group``(): unit =
    let path = AbsolutePath "/etc/hosts"
    let first = HostsFile.Record("127.0.0.1", "first.example", path)
    let second = HostsFile.Record("127.0.0.1", "second.example", path)
    Assert.Equal(Some(HostsFile.ConcurrencyGroup path), first.Lock)
    Assert.Equal(first.Lock, second.Lock)

[<Fact>]
let ``Several records applied together are all written to the hosts file``(): Task =
    withTempFile (fun path -> task {
        do! path.WriteAllTextAsync "127.0.0.1 localhost\n"
        let hosts = [ for i in 1 .. 10 -> $"host{i}.example" ]
        let resources = [ for host in hosts -> HostsFile.Record("127.0.0.1", host, path) ]

        use output = new StringWriter()
        let! report = Fabricator.Console.Commands.apply (Fabricator.Console.ExecutionUi.PlainUi output) resources |> Async.StartAsTask

        Assert.True(Fabricator.Console.Report.isSuccessful report, output.ToString())
        let! lines = path.ReadAllLinesAsync()
        for host in hosts do
            Assert.Contains($"127.0.0.1 {host}", lines)
    })

[<Fact>]
let ``AlreadyApplied returns the diff that Apply makes``(): Task =
    withTempFile (fun path -> task {
        do! path.WriteAllTextAsync("127.0.0.1 localhost\n10.0.0.1 example.com\n")
        let resource = HostsFile.Record("192.168.1.1", "example.com", path)
        let! planned = resource.AlreadyApplied ResourceContext.Null
        let! made = resource.Apply ResourceContext.Null
        let! written = path.ReadAllTextAsync()
        let expected = {
            Name = path.Value
            OldText = Some "127.0.0.1 localhost\n10.0.0.1 example.com\n"
            NewText = "127.0.0.1 localhost\n192.168.1.1 example.com\n"
        }
        Assert.Equal(TextDiff expected, planned)
        Assert.Equal(TextDiff expected, made)
        Assert.Equal(expected.NewText, written.ReplaceLineEndings "\n")
    })
