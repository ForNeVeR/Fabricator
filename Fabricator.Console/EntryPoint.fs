module Fabricator.Console.EntryPoint

open System

open Argu

open Fabricator.Console.Arguments
open Fabricator.Core

module ExitCodes =
    let Success = 0
    let InvalidArgs = 1
    let ExecutionError = 2

type RunMode =
    | Build = 0
    | Verify = 1

let private printUsage() =
    printfn "Usage:"

let private run (options: ParseResults<BuildCommandArguments>) runner runMode cluster =
    let machineName = options.GetResult BuildCommandArguments.MachineName
    match Seq.tryFind (fun m -> m.Name = machineName) cluster with
    | Some machine ->
        Async.RunSynchronously(runner machine Environment.CurrentDirectory)
        ExitCodes.Success
    | None ->
        eprintfn $"Machine with name {machineName} was not found in cluster."
        ExitCodes.ExecutionError

/// Performs tasks on passed cluster according to the passed arguments
let main (args: string[]) (cluster: RunMode -> Machine seq): int =
    let parser = ArgumentParser.Create<Command>()
    match parser.ParseCommandLine(args, raiseOnUsage = false) with
    | x when args.Length = 0 || x.IsUsageRequested ->
        printf $"{parser.PrintUsage()}"
        ExitCodes.InvalidArgs
    | command ->
        let runMode =
            match command.GetSubCommand() with
            | Command.Build _ -> RunMode.Build
            | Command.Verify _ -> RunMode.Verify
        let cluster = cluster runMode

        match command.GetSubCommand() with
        | Command.Build options -> run options Commands.build RunMode.Build cluster
        | Command.Verify options -> run options Commands.verify RunMode.Verify cluster
