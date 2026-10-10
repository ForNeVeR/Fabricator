// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.ReportTests

open System
open System.Collections.Generic
open Fabricator.Console
open Fabricator.Console.Lowering
open Fabricator.Core
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
            check b, ChangeNeeded ChangeWithNoDescription
            check c, error
            check d, Blocked
        ]
    Assert.Equal<(string * ReportItemState) list>(
        [
            "A", ReportItemState.AlreadyApplied
            "B", ReportItemState.NotApplied
            "C", ReportItemState.CheckErrored
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
            check b, ChangeNeeded ChangeWithNoDescription; apply b, Applied ChangeWithNoDescription
            check c, ChangeNeeded ChangeWithNoDescription; apply c, error
            check d, error; apply d, Blocked
            check e, Blocked; apply e, Blocked
        ]
    Assert.Equal<(string * ReportItemState) list>(
        [
            "A", ReportItemState.AlreadyApplied
            "B", ReportItemState.Applied
            "C", ReportItemState.ApplyErrored
            "D", ReportItemState.CheckErrored
            "E", ReportItemState.Skipped
        ],
        states report
    )

[<Fact>]
let ``Unexpected outcome combination is rejected``(): unit =
    let a = resource "A" []
    Assert.Throws<InvalidOperationException>(fun () ->
        createReport CheckAndApply [ a ] [ check a, CheckPassed; apply a, Applied ChangeWithNoDescription ] |> ignore
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
    { Items = [ for i, state in List.indexed itemStates -> { ResourceName = $"R{i}"; State = state; Change = NoChanges } ] }

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
[<InlineData "CheckErrored">]
[<InlineData "ApplyErrored">]
let ``Report with a failure is not successful``(failure: string): unit =
    let failure =
        match failure with
        | "Skipped" -> ReportItemState.Skipped
        | "CheckErrored" -> ReportItemState.CheckErrored
        | _ -> ReportItemState.ApplyErrored
    let report = reportOf [ ReportItemState.NotApplied; failure; ReportItemState.Applied ]
    Assert.False(Report.isSuccessful report)
    Assert.Equal(CheckError, Report.checkStatus report)

[<Fact>]
let ``Report lines are formatted with emoji``(): unit =
    let line state = Report.formatLine true { ResourceName = "R"; State = state; Change = NoChanges }
    Assert.Equal("➖ R (already applied)", line ReportItemState.AlreadyApplied)
    Assert.Equal("🟡 R (not applied)", line ReportItemState.NotApplied)
    Assert.Equal("✅ R (applied)", line ReportItemState.Applied)
    Assert.Equal("⏩ R (skipped: a dependency has failed)", line ReportItemState.Skipped)
    Assert.Equal("❌ R (failed to check)", line ReportItemState.CheckErrored)
    Assert.Equal("❌ R (failed to apply)", line ReportItemState.ApplyErrored)

[<Fact>]
let ``Report lines are formatted in ASCII``(): unit =
    let line state = Report.formatLine false { ResourceName = "R"; State = state; Change = NoChanges }
    Assert.Equal("[=] R (already applied)", line ReportItemState.AlreadyApplied)
    Assert.Equal("[ ] R (not applied)", line ReportItemState.NotApplied)
    Assert.Equal("[x] R (applied)", line ReportItemState.Applied)
    Assert.Equal("[-] R (skipped: a dependency has failed)", line ReportItemState.Skipped)
    Assert.Equal("[!] R (failed to check)", line ReportItemState.CheckErrored)
    Assert.Equal("[!] R (failed to apply)", line ReportItemState.ApplyErrored)

[<Theory>]
[<InlineData true>]
[<InlineData false>]
let ``Report markers are distinct for every state except errors``(useEmoji: bool): unit =
    let markers =
        [
            ReportItemState.AlreadyApplied
            ReportItemState.NotApplied
            ReportItemState.Applied
            ReportItemState.Skipped
            ReportItemState.CheckErrored
        ]
        |> List.map (Report.marker useEmoji)
    Assert.Equal(markers.Length, (List.distinct markers).Length)
    Assert.Equal(
        Report.marker useEmoji ReportItemState.CheckErrored,
        Report.marker useEmoji ReportItemState.ApplyErrored
    )

[<Fact>]
let ``Report items carry the check changes in check mode``(): unit =
    let a, b = resource "A" [], resource "B" []
    let report = createReport CheckOnly [ a; b ] [ check a, CheckPassed; check b, ChangeNeeded(NamedChange "b") ]
    Assert.Equal<ResourceChange list>([ NoChanges; NamedChange "b" ], report.Items |> List.map _.Change)

[<Fact>]
let ``Report items carry the apply changes in apply mode``(): unit =
    let a, b = resource "A" [], resource "B" []
    let report =
        createReport CheckAndApply [ a; b ] [
            check a, CheckPassed; apply a, NotRequired
            check b, ChangeNeeded(NamedChange "planned"); apply b, Applied(NamedChange "made")
        ]
    Assert.Equal<ResourceChange list>([ NoChanges; NamedChange "made" ], report.Items |> List.map _.Change)

let private detailTexts change = Report.details change |> Seq.map _.Text
let private detailKinds change = Report.details change |> Seq.map _.Kind

[<Fact>]
let ``Changes without description have no details``(): unit =
    Assert.Empty(Report.details NoChanges)
    Assert.Empty(Report.details ChangeWithNoDescription)

[<Fact>]
let ``Named change details are its lines``(): unit =
    let details = Report.details(NamedChange "first\r\nsecond\n")
    Assert.Equal<DetailLine seq>(
        [
            { Text = "first"; Kind = DetailKind.Plain }
            { Text = "second"; Kind = DetailKind.Plain }
        ],
        details
    )

[<Fact>]
let ``Text diff details are a unified patch``(): unit =
    let change = TextDiff { Name = "file.txt"; OldText = Some "a\nb\nc\n"; NewText = "a\nB\nc\n" }
    Assert.Equal<string seq>(
        [ "--- file.txt"; "+++ file.txt"; "@@ -1,3 +1,3 @@"; " a"; "-b"; "+B"; " c" ],
        detailTexts change
    )
    Assert.Equal<DetailKind seq>(
        [
            DetailKind.FileHeader; DetailKind.FileHeader; DetailKind.HunkHeader
            DetailKind.Plain; DetailKind.Removed; DetailKind.Added; DetailKind.Plain
        ],
        detailKinds change
    )

[<Fact>]
let ``Text diff headers are not confused with the changed lines``(): unit =
    let change = TextDiff { Name = "f"; OldText = Some "-- x\n"; NewText = "++ y\n" }
    Assert.Equal<string seq>([ "--- f"; "+++ f"; "@@ -1,1 +1,1 @@"; "--- x"; "+++ y" ], detailTexts change)
    Assert.Equal<DetailKind seq>(
        [ DetailKind.FileHeader; DetailKind.FileHeader; DetailKind.HunkHeader; DetailKind.Removed; DetailKind.Added ],
        detailKinds change
    )

[<Fact>]
let ``New file diff creates the file``(): unit =
    let change = TextDiff { Name = "file.txt"; OldText = None; NewText = "a\r\nb\r\n" }
    Assert.Equal<string seq>(
        [ "--- /dev/null"; "+++ file.txt (new)"; "@@ -0,0 +1,2 @@"; "+a"; "+b" ],
        detailTexts change
    )
    Assert.Equal<DetailKind seq>(
        [ DetailKind.FileHeader; DetailKind.FileHeader; DetailKind.HunkHeader; DetailKind.Added; DetailKind.Added ],
        detailKinds change
    )

[<Fact>]
let ``New file diff marks the missing final line break``(): unit =
    let change = TextDiff { Name = "f"; OldText = None; NewText = "a" }
    Assert.Equal<string seq>(
        [ "--- /dev/null"; "+++ f (new)"; "@@ -0,0 +1,1 @@"; "+a"; @"\ No newline at end of file" ],
        detailTexts change
    )

[<Fact>]
let ``Empty new file diff has no hunks``(): unit =
    let change = TextDiff { Name = "f"; OldText = None; NewText = "" }
    Assert.Equal<string seq>([ "--- /dev/null"; "+++ f (new)" ], detailTexts change)

let private summaryOf states = reportOf states |> Report.formatSummary

[<Fact>]
let ``Summary counts the resources in every state``(): unit =
    Assert.Equal(
        "1 already applied, 2 to apply, 1 errored, 1 blocked",
        summaryOf [
            ReportItemState.NotApplied
            ReportItemState.AlreadyApplied
            ReportItemState.CheckErrored
            ReportItemState.NotApplied
            ReportItemState.Skipped
        ]
    )

[<Fact>]
let ``Summary sums the check and apply errors``(): unit =
    Assert.Equal(
        "2 applied, 2 errored",
        summaryOf [
            ReportItemState.Applied; ReportItemState.CheckErrored
            ReportItemState.ApplyErrored; ReportItemState.Applied
        ]
    )

[<Fact>]
let ``Summary omits the states no resources are in``(): unit =
    Assert.Equal("3 already applied", summaryOf [ for _ in 1 .. 3 -> ReportItemState.AlreadyApplied ])
    Assert.Empty(Report.summary(reportOf []))

[<Fact>]
let ``Summary parts are presented as their states``(): unit =
    let parts = Report.summary(reportOf [ ReportItemState.ApplyErrored; ReportItemState.Skipped ])
    Assert.Equal<SummaryPart list>(
        [
            { Text = "1 errored"; State = ReportItemState.CheckErrored }
            { Text = "1 blocked"; State = ReportItemState.Skipped }
        ],
        parts
    )
