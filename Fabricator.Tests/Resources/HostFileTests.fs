// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.HostFileTests

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
    let tempPath = AbsolutePath(Path.GetTempFileName())
    try
        return! action tempPath
    finally
        if tempPath.Exists() then
            tempPath.Delete()
}

let private testAlreadyApplied (hostsContent: string) (ipAddress: string) (host: string) = 
    withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, hostsContent)
        let resource = HostFile.Record(ipAddress, host, path)
        return! resource.AlreadyApplied()
    })

let private testApply (hostsContent: string) (ipAddress: string) (host: string) (expectedHostsContent: string) = 
    withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, hostsContent)
        let resource = HostFile.Record(ipAddress, host, path)
        do! resource.Apply()
        let! actualContent = File.ReadAllTextAsync(path.Value)
        Assert.Equal(expectedHostsContent, actualContent)
    })

let private testApplyAndRead (hostsContent: string) (ipAddress: string) (host: string) = 
    withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, hostsContent)
        let resource = HostFile.Record(ipAddress, host, path)
        do! resource.Apply()
        return! File.ReadAllTextAsync(path.Value)
    })

[<Fact>]
let ``PresentableName returns correct format``(): Task = upcast task {
    do! withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, "")
        let resource = HostFile.Record("127.0.0.1", "example.com", path)
        Assert.Equal("Host file entry \"example.com\"", resource.PresentableName)
    })
}

[<Fact>]
let ``AlreadyApplied returns false when host file does not exist``(): Task = upcast task {
    let tempPath = AbsolutePath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))
    let resource = HostFile.Record("127.0.0.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied()
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied returns false when entry does not exist``(): Task = upcast task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 router\n" "10.0.0.1" "newhost.com"
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied returns true when exact entry exists``(): Task = upcast task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 example.com\n" "192.168.1.1" "example.com"
    Assert.True result
}

[<Fact>]
let ``AlreadyApplied returns false when host exists with different IP``(): Task = upcast task {
    let! result = testAlreadyApplied "127.0.0.1 localhost\n192.168.1.1 example.com\n" "10.0.0.1" "example.com"
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied handles whitespace variations``(): Task = upcast task {
    let! result = testAlreadyApplied "192.168.1.1    example.com\n" "192.168.1.1" "example.com"
    Assert.True result
}

[<Fact>]
let ``AlreadyApplied ignores comment lines``(): Task = upcast task {
    let! result = testAlreadyApplied "# 192.168.1.1 example.com\n127.0.0.1 localhost\n" "192.168.1.1" "example.com"
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied handles inline comments``(): Task = upcast task {
    let! result = testAlreadyApplied "192.168.1.1 example.com # this is a comment\n" "192.168.1.1" "example.com"
    Assert.True result
}

[<Fact>]
let ``AlreadyApplied handles multi-host entries``(): Task = upcast task {
    let! result = testAlreadyApplied "192.168.1.1 example.com another.com\n" "192.168.1.1" "example.com"
    Assert.True result
}

[<Fact>]
let ``Apply adds new entry when host does not exist``(): Task = upcast task {
    let! content = testApplyAndRead "127.0.0.1 localhost\n" "192.168.1.1" "example.com"
    Assert.Contains("192.168.1.1\texample.com", content)
    Assert.Contains("127.0.0.1 localhost", content)
}

[<Fact>]
let ``Apply replaces existing entry with different IP``(): Task = upcast task {
    let! content = testApplyAndRead "127.0.0.1 localhost\n192.168.1.1 example.com\n" "10.0.0.1" "example.com"
    Assert.Contains("10.0.0.1\texample.com", content)
    Assert.DoesNotContain("192.168.1.1 example.com", content)
}

[<Fact>]
let ``Apply splits multi-host entry when updating IP``(): Task = upcast task {
    let! content = testApplyAndRead "192.168.1.1 example.com another.com\n" "10.0.0.1" "example.com"
    Assert.Contains("10.0.0.1\texample.com", content)
    Assert.Contains("192.168.1.1\tanother.com", content)
    Assert.DoesNotContain("192.168.1.1 example.com another.com", content)
}

[<Fact>]
let ``Apply preserves inline comments``(): Task = upcast task {
    let! content = testApplyAndRead "127.0.0.1 localhost # default\n" "192.168.1.1" "newhost.com"
    Assert.Contains("127.0.0.1 localhost # default", content)
    Assert.Contains("192.168.1.1\tnewhost.com", content)
}

[<Fact>]
let ``Apply fails when hosts file does not exist``(): Task = upcast task {
    let nonExistentPath = AbsolutePath(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()))
    let resource = HostFile.Record("192.168.1.1", "example.com", nonExistentPath)
    let! ex = Assert.ThrowsAsync<Exception>(fun () -> Async.StartAsTask(resource.Apply()) :> Task)
    Assert.Contains("Hosts file not found", ex.Message)
}

[<Fact>]
let ``Apply is idempotent``(): Task = upcast task {
    do! withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, "127.0.0.1 localhost\n")
        let resource = HostFile.Record("192.168.1.1", "example.com", path)
        do! resource.Apply()
        do! resource.Apply()
        let! content = File.ReadAllTextAsync(path.Value)
        let lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        let matchingLines = lines |> Array.filter (fun line -> line.Contains("example.com"))
        Assert.Single(matchingLines) |> ignore
    })
}

[<Fact>]
let ``Apply and AlreadyApplied work together``(): Task = upcast task {
    do! withTempFile (fun path -> task {
        do! File.WriteAllTextAsync(path.Value, "127.0.0.1 localhost\n")
        let resource = HostFile.Record("192.168.1.1", "example.com", path)
        let! beforeApply = resource.AlreadyApplied()
        Assert.False beforeApply
        do! resource.Apply()
        let! afterApply = resource.AlreadyApplied()
        Assert.True afterApply
    })
}

[<Fact>]
let ``DefaultHostsPath returns Windows path on Windows``(): unit =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        let path = HostFile.DefaultHostsPath
        Assert.Equal(@"C:\Windows\System32\drivers\etc\hosts", path.Value)

[<Fact>]
let ``DefaultHostsPath returns Unix path on non-Windows``(): unit =
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        let path = HostFile.DefaultHostsPath
        Assert.Equal("/etc/hosts", path.Value)
