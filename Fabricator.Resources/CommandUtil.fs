// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module internal Fabricator.Resources.CommandUtil

open Medallion.Shell

let runCommand (exe: string) (args: obj[]): Async<CommandResult> = async {
    let! command = Async.AwaitTask <| Command.Run(exe, args).Task

    if not command.Success
    then failwithf $"{exe} execution error {string command.ExitCode}: {command.StandardError}\n{command.StandardOutput}"

    return command
}
