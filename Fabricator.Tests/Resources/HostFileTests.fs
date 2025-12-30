// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.HostFileTests

open System
open System.IO
open System.Threading.Tasks
open FSharp.Control.Tasks
open Xunit
open Fabricator.Core
open Fabricator.Resources

[<Fact>]
let ``PresentableName returns correct format``(): unit =
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "")
    let resource = HostFile.Record("127.0.0.1", "example.com", tempPath)
    Assert.Equal("Host file entry \"example.com\"", resource.PresentableName)
    File.Delete(tempPath)

[<Fact>]
let ``AlreadyApplied returns false when host file does not exist``(): Task = upcast task {
    let nonExistentPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let resource = HostFile.Record("127.0.0.1", "example.com", nonExistentPath)
    let! result = resource.AlreadyApplied()
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied returns false when entry does not exist``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n192.168.1.1 router\n")
    
    let resource = HostFile.Record("10.0.0.1", "newhost.com", tempPath)
    let! result = resource.AlreadyApplied()
    
    File.Delete(tempPath)
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied returns true when exact entry exists``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n192.168.1.1 example.com\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied()
    
    File.Delete(tempPath)
    Assert.True result
}

[<Fact>]
let ``AlreadyApplied returns false when host exists with different IP``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n192.168.1.1 example.com\n")
    
    let resource = HostFile.Record("10.0.0.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied()
    
    File.Delete(tempPath)
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied handles whitespace variations``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    // Use multiple spaces and tabs
    File.WriteAllText(tempPath, "192.168.1.1    example.com\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied()
    
    File.Delete(tempPath)
    Assert.True result
}

[<Fact>]
let ``AlreadyApplied ignores comments``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "# 192.168.1.1 example.com\n127.0.0.1 localhost\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    let! result = resource.AlreadyApplied()
    
    File.Delete(tempPath)
    Assert.False result
}

[<Fact>]
let ``Apply adds new entry when host does not exist``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    do! resource.Apply()
    
    let content = File.ReadAllText(tempPath)
    File.Delete(tempPath)
    
    Assert.Contains("192.168.1.1\texample.com", content)
    Assert.Contains("127.0.0.1 localhost", content)
}

[<Fact>]
let ``Apply replaces existing entry with different IP``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n192.168.1.1 example.com\n")
    
    let resource = HostFile.Record("10.0.0.1", "example.com", tempPath)
    do! resource.Apply()
    
    let lines = File.ReadAllLines(tempPath)
    File.Delete(tempPath)
    
    Assert.Contains("10.0.0.1\texample.com", lines)
    Assert.DoesNotContain("192.168.1.1 example.com", lines)
}

[<Fact>]
let ``Apply fails when hosts file does not exist``(): Task = upcast task {
    let nonExistentPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let resource = HostFile.Record("192.168.1.1", "example.com", nonExistentPath)
    
    let! ex = Assert.ThrowsAsync<Exception>(fun () -> Async.StartAsTask(resource.Apply()) :> Task)
    Assert.Contains("Hosts file not found", ex.Message)
}

[<Fact>]
let ``Apply is idempotent``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    do! resource.Apply()
    do! resource.Apply()
    
    let lines = File.ReadAllLines(tempPath)
    File.Delete(tempPath)
    
    let matchingLines = lines |> Array.filter (fun line -> line.Contains("example.com"))
    Assert.Single(matchingLines) |> ignore
}

[<Fact>]
let ``Apply and AlreadyApplied work together``(): Task = upcast task {
    let tempPath = Path.GetTempFileName()
    File.WriteAllText(tempPath, "127.0.0.1 localhost\n")
    
    let resource = HostFile.Record("192.168.1.1", "example.com", tempPath)
    
    let! beforeApply = resource.AlreadyApplied()
    Assert.False beforeApply
    
    do! resource.Apply()
    
    let! afterApply = resource.AlreadyApplied()
    File.Delete(tempPath)
    Assert.True afterApply
}
