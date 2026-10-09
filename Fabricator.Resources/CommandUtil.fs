// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Resources.CommandUtil

open System.IO
open System.Text
open Fabricator.Core
open Medallion.Shell

type CommandOutput = {
    Success: bool
    ExitCode: int
    StandardOutput: string
    StandardError: string
}

let private formatArgument(argument: obj) =
    let text = string argument
    if text.Contains ' ' then $"\"{text}\"" else text

/// Reads the lines from the reader until its end, logging each line. Returns all the read text.
let private readLines (reporter: IReporter) (reader: TextReader): Async<string> = async {
    let text = StringBuilder()
    let mutable finished = false
    while not finished do
        let! line = Async.AwaitTask(reader.ReadLineAsync())
        match line with
        | null -> finished <- true
        | line ->
            reporter.Log line
            text.AppendLine line |> ignore
    return text.ToString()
}

/// Runs the command, logging its command line and output. Fails if the command exits with a non-zero code.
let runCommand (reporter: IReporter) (exe: string) (args: obj[]): Async<CommandOutput> = async {
    let! ct = Async.CancellationToken
    reporter.Log $"""> {exe} {args |> Seq.map formatArgument |> String.concat " "}"""
    let command = Command.Run(exe, args, fun options -> options.CancellationToken ct |> ignore)

    let! output = Async.Parallel [|
        readLines reporter command.StandardOutput
        readLines reporter command.StandardError
    |]
    let standardOutput, standardError = output[0], output[1]
    let! result = Async.AwaitTask command.Task

    if not result.Success
    then failwithf $"{exe} execution error {string result.ExitCode}: {standardError}\n{standardOutput}"

    return { Success = result.Success; ExitCode = result.ExitCode; StandardOutput = standardOutput; StandardError = standardError }
}
