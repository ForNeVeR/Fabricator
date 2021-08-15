module Fabricator.Console.EntryPoint

open System

open Argu

open Fabricator.Console.Arguments
open Fabricator.Core

module ExitCodes =
    let Success = 0
    let InvalidArgs = 1
    let ExecutionError = 2

let private printUsage() =
    printfn "Usage:"

/// Performs tasks on passed cluster according to the passed arguments
let main (args: string[]) (cluster: Machine seq): int =
    let parser = ArgumentParser.Create<Command>()
    match parser.ParseCommandLine(args, raiseOnUsage = false) with
    | x when args.Length = 0 || x.IsUsageRequested ->
        printf $"{parser.PrintUsage()}"
        ExitCodes.InvalidArgs
    | command ->
        match command.GetSubCommand() with
        | Command.Build buildOptions ->
            let machineName = buildOptions.GetResult BuildCommandArguments.MachineName
            match Seq.tryFind(fun m -> m.Name = machineName) cluster with
            | Some machine ->
                Async.RunSynchronously(Commands.build machine Environment.CurrentDirectory)
                ExitCodes.Success
            | None ->
                eprintfn $"Machine with name {machineName} was not found in cluster."
                ExitCodes.ExecutionError
