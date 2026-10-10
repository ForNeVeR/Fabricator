// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.ReportTests

open System
open System.Collections.Generic
open Fabricator.Console
open Fabricator.Console.Lowering
open Fabricator.Tests.FakeResource
open Xunit

let private resource name (dependencies: FakeResource list) = FakeResource(name, EventLog(), Array.ofList dependencies)
let private check(r: FakeResource) = { Kind = Check; Resource = r.Resource }
let private apply(r: FakeResource) = { Kind = Apply; Resource = r.Resource }

let private results(outcomes: (LoweredTask * TaskOutcome) list): IReadOnlyDictionary<LoweredTask, TaskOutcome> =
    outcomes |> dict |> Dictionary :> _

let private createReport mode (roots: FakeResource list) outcomes =
    Report.create mode (roots |> Seq.map _.Resource) (results outcomes)

let private states(report: Report) =
    report.Items |> List.map (fun item -> item.ResourceName, item.State)

let private error = Errored(Exception "Failure")

[<Fact>]
let ``Check outcomes are mapped to the report states``(): unit =
    let a, b, c, d = resource "A" [], resource "B" [], resource "C" [], resource "D" []
    let report =
        createReport CheckOnly [ a; b; c; d ] [
            check a, CheckPassed
            check b, CheckFailed
            check c, error
            check d, Blocked
        ]
    Assert.Equal<(string * ReportItemState) list>(
        [
            "A", ReportItemState.AlreadyApplied
            "B", ReportItemState.NotApplied
            "C", ReportItemState.CheckFailed
            "D", ReportItemState.Skipped
        ],
        states report
    )

[<Fact>]
let ``Apply outcomes are mapped to the report states``(): unit =
    let a, b, c, d, e = resource "A" [], resource "B" [], resource "C" [], resource "D" [], resource "E" []
    let report =
        createReport CheckAndApply [ a; b; c; d; e ] [
            check a, CheckPassed; apply a, NotRequired
            check b, CheckFailed; apply b, Applied
            check c, CheckFailed; apply c, error
            check d, error; apply d, Blocked
            check e, Blocked; apply e, Blocked
        ]
    Assert.Equal<(string * ReportItemState) list>(
        [
            "A", ReportItemState.AlreadyApplied
            "B", ReportItemState.Applied
            "C", ReportItemState.ApplyFailed
            "D", ReportItemState.CheckFailed
            "E", ReportItemState.Skipped
        ],
        states report
    )

[<Fact>]
let ``Unexpected outcome combination is rejected``(): unit =
    let a = resource "A" []
    Assert.Throws<InvalidOperationException>(fun () ->
        createReport CheckAndApply [ a ] [ check a, CheckPassed; apply a, Applied ] |> ignore
    ) |> ignore

[<Fact>]
let ``Dependencies go before dependents, ordered by name, each resource once``(): unit =
    let shared = resource "Shared" []
    let z, y = resource "Z" [ shared ], resource "Y" []
    let root1 = resource "Root1" [ z; y ]
    let root2 = resource "Root2" [ shared ]
    let all = [ shared; z; y; root1; root2 ]
    let report = createReport CheckOnly [ root1; root2; z ] [ for r in all -> check r, CheckPassed ]
    Assert.Equal<string list>(
        [ "Y"; "Shared"; "Z"; "Root1"; "Root2" ],
        report.Items |> List.map _.ResourceName
    )

let private reportOf(itemStates: ReportItemState list) =
    { Items = [ for i, state in List.indexed itemStates -> { ResourceName = $"R{i}"; State = state } ] }

[<Fact>]
let ``Empty report is successful and fully applied``(): unit =
    let report = reportOf []
    Assert.True(Report.isSuccessful report)
    Assert.Equal(AllApplied, Report.checkStatus report)

[<Fact>]
let ``Report without failures is successful``(): unit =
    let report = reportOf [ ReportItemState.AlreadyApplied; ReportItemState.Applied ]
    Assert.True(Report.isSuccessful report)
    Assert.Equal(AllApplied, Report.checkStatus report)

[<Fact>]
let ``Report with non-applied resources is not fully applied``(): unit =
    let report = reportOf [ ReportItemState.AlreadyApplied; ReportItemState.NotApplied ]
    Assert.True(Report.isSuccessful report)
    Assert.Equal(NotAllApplied, Report.checkStatus report)

[<Theory>]
[<InlineData "Skipped">]
[<InlineData "CheckFailed">]
[<InlineData "ApplyFailed">]
let ``Report with a failure is not successful``(failure: string): unit =
    let failure =
        match failure with
        | "Skipped" -> ReportItemState.Skipped
        | "CheckFailed" -> ReportItemState.CheckFailed
        | _ -> ReportItemState.ApplyFailed
    let report = reportOf [ ReportItemState.NotApplied; failure; ReportItemState.Applied ]
    Assert.False(Report.isSuccessful report)
    Assert.Equal(CheckError, Report.checkStatus report)

[<Fact>]
let ``Report lines are formatted with emoji``(): unit =
    let line state = Report.formatLine true { ResourceName = "R"; State = state }
    Assert.Equal("➖ R (already applied)", line ReportItemState.AlreadyApplied)
    Assert.Equal("🟡 R (not applied)", line ReportItemState.NotApplied)
    Assert.Equal("✅ R (applied)", line ReportItemState.Applied)
    Assert.Equal("⏩ R (skipped: a dependency has failed)", line ReportItemState.Skipped)
    Assert.Equal("❌ R (failed to check)", line ReportItemState.CheckFailed)
    Assert.Equal("❌ R (failed to apply)", line ReportItemState.ApplyFailed)

[<Fact>]
let ``Report lines are formatted in ASCII``(): unit =
    let line state = Report.formatLine false { ResourceName = "R"; State = state }
    Assert.Equal("[=] R (already applied)", line ReportItemState.AlreadyApplied)
    Assert.Equal("[x] R (not applied)", line ReportItemState.NotApplied)
    Assert.Equal("[x] R (applied)", line ReportItemState.Applied)
    Assert.Equal("[=] R (skipped: a dependency has failed)", line ReportItemState.Skipped)
    Assert.Equal("[x] R (failed to check)", line ReportItemState.CheckFailed)
    Assert.Equal("[x] R (failed to apply)", line ReportItemState.ApplyFailed)
