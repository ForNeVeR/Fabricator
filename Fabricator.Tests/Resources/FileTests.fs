// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.FileTests

open System.IO
open System.Threading.Tasks

open FSharp.Control.Tasks
open Xunit

open Fabricator.Core
open Fabricator.Resources.Files

let fileFromSource s =
    FileResource(s, Path.GetTempFileName()) :> IResource

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

let private testAlreadyApplied sourceContent targetContent = task {
    let sourcePath = Path.GetTempFileName()
    let targetPath = Path.GetTempFileName()
    do! File.WriteAllBytesAsync(sourcePath, sourceContent)
    do! File.WriteAllBytesAsync(targetPath, targetContent)

    let resource = FileResource(AbsoluteFile sourcePath, targetPath) :> IResource
    return! resource.AlreadyApplied()
}

[<Fact>]
let ``AlreadyApplied returns false when not applied``(): Task = upcast task {
    let! result = testAlreadyApplied [|0uy; 1uy; 2uy|] Array.empty
    Assert.False result
}

[<Fact>]
let ``AlreadyApplied returns true when applied``(): Task = upcast task {
    let bytes = [|3uy; 2uy; 1uy|]
    let! result = testAlreadyApplied bytes bytes
    Assert.True result
}

[<Fact>]
let ``Apply should create target file``(): Task = upcast task {
    let bytes = [|0uy; 1uy; 2uy|]
    let targetFile = Path.GetTempFileName()
    let resource = FileResource(GeneratedContent("content", fun() -> bytes), targetFile) :> IResource

    Assert.Equal(0L, FileInfo(targetFile).Length)
    do! resource.Apply()
    let! actualContent = File.ReadAllBytesAsync(targetFile)
    Assert.Equal<byte>(bytes, actualContent)
}
