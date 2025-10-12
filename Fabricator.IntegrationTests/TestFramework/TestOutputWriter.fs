namespace Fabricator.IntegrationTests.TestFramework

open System
open System.IO
open System.Text
open Xunit.Abstractions

type TestOutputWriter(output: ITestOutputHelper) =
    inherit TextWriter()

    let monitor = obj()
    let buffer = StringBuilder()

    override this.Encoding = Encoding.UTF8

    override this.Write(value: char) =
        lock monitor (fun() ->
            match value with
            | '\n' -> this.Flush()
            | _ -> buffer.Append value |> ignore
        )

    override _.Flush() =
        lock monitor (fun() ->
            let string =
                // Strip \r\n on Windows:
                if Environment.NewLine = "\r\n" && buffer.Length > 0 && buffer.[buffer.Length - 1] = '\r' then
                    buffer.ToString(0, buffer.Length - 1)
                else
                    buffer.ToString()

            output.WriteLine string
            buffer.Clear() |> ignore
        )

    override this.Dispose(disposing: bool) =
        if disposing then
            this.Flush()
