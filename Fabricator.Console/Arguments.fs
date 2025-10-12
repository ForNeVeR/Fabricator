namespace Fabricator.Console.Arguments

open Argu

[<RequireQualifiedAccess>]
type BuildCommandArguments =
    | [<Unique>] MachineName of string
    interface IArgParserTemplate with
        member arg.Usage =
            match arg with
            | MachineName _ -> "the target machine name."

[<RequireQualifiedAccess>]
type Command =
    | [<CliPrefix(CliPrefix.None)>] Build of ParseResults<BuildCommandArguments>
    interface IArgParserTemplate with
        member command.Usage =
            match command with
            | Build _ -> "build the agent prepared for deployment to the target machine."
