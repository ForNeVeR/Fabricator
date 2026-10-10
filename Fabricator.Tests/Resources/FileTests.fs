// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.FileTests

open System.IO
open System.Threading.Tasks

open FSharp.Control.Tasks
open Xunit

open Fabricator.Core
open Fabricator.Resources
open type Fabricator.Resources.Files

let fileFromSource s =
    file(s, Path.GetTempFileName())

[<Fact>]
let ``PresentableName for ContentFile``(): unit =
    let resource = fileFromSource(ContentFile "file1.txt")
    Assert.Equal("file1.txt", resource.PresentableName)

[<Fact>]
let ``PresentableName for AbsoluteFile``(): unit =
    let path = Path.Combine(Path.GetTempPath(), "file2.txt")
    let resource = fileFromSource(AbsoluteFile path)
    Assert.Equal("file2.txt", resource.PresentableName)

[<Fact>]
let ``PresentableName for GeneratedContent``(): unit =
    let resource = fileFromSource(ContentFile("file.txt"))
    Assert.Equal("file.txt", resource.PresentableName)

let private testAlreadyApplied (sourceContent: byte[]) (targetContent: byte[]) = task {
    let sourcePath = Path.GetTempFileName()
    let targetPath = Path.GetTempFileName()
    do! File.WriteAllBytesAsync(sourcePath, sourceContent)
    do! File.WriteAllBytesAsync(targetPath, targetContent)

    let resource = file(AbsoluteFile sourcePath, targetPath)
    return! resource.AlreadyApplied ResourceContext.Null
}

[<Fact>]
let ``AlreadyApplied returns false when not applied``(): Task = upcast task {
    let! result = testAlreadyApplied [|0uy; 1uy; 2uy|] Array.empty
    Assert.NotEqual(NoChanges, result)
}

[<Fact>]
let ``AlreadyApplied returns true when applied``(): Task = upcast task {
    let bytes = [|3uy; 2uy; 1uy|]
    let! result = testAlreadyApplied bytes bytes
    Assert.Equal(NoChanges, result)
}

[<Fact>]
let ``Apply should create target file``(): Task = upcast task {
    let bytes = [|0uy; 1uy; 2uy|]
    let targetFile = Path.GetTempFileName()
    let resource = file(GeneratedContent("content", fun() -> bytes), targetFile)

    Assert.Equal(0L, FileInfo(targetFile).Length)
    let! _ = resource.Apply ResourceContext.Null
    let! actualContent = File.ReadAllBytesAsync(targetFile)
    Assert.Equal<byte>(bytes, actualContent)
}

[<Fact>]
let ``AlreadyApplied returns the text diff of the target file``(): Task = upcast task {
    let sourcePath = Path.GetTempFileName()
    let targetPath = Path.GetTempFileName()
    do! File.WriteAllTextAsync(sourcePath, "new\n")
    do! File.WriteAllTextAsync(targetPath, "old\n")
    let resource = file(AbsoluteFile sourcePath, targetPath)
    let! change = resource.AlreadyApplied ResourceContext.Null
    Assert.Equal(TextDiff { Name = targetPath; OldText = Some "old\n"; NewText = "new\n" }, change)
}

[<Fact>]
let ``AlreadyApplied does not read a missing source when the target is missing``(): Task = upcast task {
    let sourcePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let targetPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    let resource = file(AbsoluteFile sourcePath, targetPath)
    let! change = resource.AlreadyApplied ResourceContext.Null
    Assert.Equal(NamedChange $"new file \"{targetPath}\"", change)
}

[<Fact>]
let ``AlreadyApplied returns the creation diff when the target is missing``(): Task = upcast task {
    let sourcePath = Path.GetTempFileName()
    let targetPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    do! File.WriteAllTextAsync(sourcePath, "text\n")
    let resource = file(AbsoluteFile sourcePath, targetPath)
    let! change = resource.AlreadyApplied ResourceContext.Null
    Assert.Equal(TextDiff { Name = targetPath; OldText = None; NewText = "text\n" }, change)
}

[<Fact>]
let ``Apply returns the creation diff for a new target file``(): Task = upcast task {
    let targetPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
    try
        let resource = file(GeneratedContent("content", fun () -> "text\n"B), targetPath)
        let! change = resource.Apply ResourceContext.Null
        Assert.Equal(TextDiff { Name = targetPath; OldText = None; NewText = "text\n" }, change)
    finally
        File.Delete targetPath
}
