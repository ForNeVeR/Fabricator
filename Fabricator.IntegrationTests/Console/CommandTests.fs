// SPDX-FileCopyrightText: 2021-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.IntegrationTests.Console

open System
open System.IO
open System.Threading.Tasks

open FSharp.Control.Tasks
open Medallion.Shell
open Xunit

open Fabricator.Core
open Fabricator.IntegrationTests.TestFramework
open Xunit.Abstractions

type CommandTests(output: ITestOutputHelper) =

    [<Fact>]
    let publishCommandTest(): Task =
        let slnDirectory =
            FileSystem.findNearestDirectoryWithFile [|"Fabricator.sln"|] Environment.CurrentDirectory
            |> Option.get
        let exampleDirectory = Path.Combine(slnDirectory, "Fabricator.Example")
        let publishDirectory = Path.Combine(exampleDirectory, "publish")
        if Directory.Exists publishDirectory then
            Directory.Delete(publishDirectory, recursive = true)

        upcast task {
            use writer = new TestOutputWriter(output)
            let command =
                Command.Run(
                    "dotnet",
                    [|
                        "run"
                        "--"
                        "build"
                        "--machinename"; "localhost"
                    |] |> Seq.cast,
                    fun (options: Shell.Options) -> options.WorkingDirectory(exampleDirectory) |> ignore
                ).RedirectTo(writer).RedirectStandardErrorTo(writer)

            let! result = Async.AwaitTask command.Task
            Assert.True result.Success

            let files = Directory.GetFiles(publishDirectory, "*.exe") |> Seq.map Path.GetFileName
            Assert.Equal([|"Fabricator.Example.exe"|], files)
        }
