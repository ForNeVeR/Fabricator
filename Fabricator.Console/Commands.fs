module Fabricator.Console.Commands

open System
open System.IO

open Medallion.Shell

open Fabricator.Core

let private findBuildRoot startDirectory =
    let result = FileSystem.findNearestDirectoryWithFile [| "*.fsproj"; "*.csproj" |] startDirectory
    match result with
    | Some r -> r
    | None -> failwith $"Could not find any sln files in any of the root directories of path \"{startDirectory}\"."

let private getRuntime machine =
    match machine.Type with
    | MachineType.Linux -> "linux-x64"
    | MachineType.Windows -> "win-x64"
    | x -> failwith $"Unknown machine type for machine {machine.Name}: {x}."

let build (machine: Machine) (startDirectory: string): Async<unit> = async {
    printfn $"Building agent for machine {machine.Name}."
    let buildRoot = findBuildRoot startDirectory
    printfn $"Found project in directory \"{buildRoot}\"."

    let publishDirectory = Path.Combine(buildRoot, "publish")
    let executable = "dotnet"
    let arguments = [|
        "publish"
        "--runtime"; getRuntime machine
        "--configuration"; "Release"
        "--output"; publishDirectory
        "-p:PublishTrimmed=true"
        "-p:PublishSingleFile=true"
    |]
    let stringifiedArguments =
        String.Join(
            ", ",
            arguments
            |> Seq.map(fun a -> $"\"{a}\"")
        )
    printfn $"Running executable \"{executable}\" with arguments [{stringifiedArguments}]."

    let buildCommand =
        Command.Run(
            executable,
            arguments |> Seq.cast,
            fun (opts: Shell.Options) -> opts.WorkingDirectory(buildRoot) |> ignore)
            .RedirectTo(Console.Out)
            .RedirectStandardErrorTo(Console.Error)
    let! buildResult = Async.AwaitTask buildCommand.Task
    if buildResult.ExitCode <> 0 then failwith $"Build command returned an exit code {buildResult.ExitCode}."

    printfn $"Successfully published project to directory \"{publishDirectory}\"."
}

let verify (machine: Machine) (startDirectory: string): Async<unit> = async {
    failwith "TODO"
}
