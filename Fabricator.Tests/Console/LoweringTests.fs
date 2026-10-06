// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Console.LoweringTests

open Fabricator.Console
open Fabricator.Console.Lowering
open Fabricator.Core
open Fabricator.Tests.FakeResource
open Xunit

let private resource name = FakeResource(name, EventLog())
let private check(r: FakeResource) = { Kind = Check; Resource = r }
let private apply(r: FakeResource) = { Kind = Apply; Resource = r }

let private lowerOk mode (roots: FakeResource list) =
    match lower mode (roots |> Seq.cast<IResource>) with
    | Ok graph -> graph
    | Error cycle -> failwithf $"Unexpected cycle: {cycle}"

let private assertTasks (expected: LoweredTask list) (graph: TaskExecutor.TaskGraph<LoweredTask>) =
    Assert.Equal<Set<string>>(
        expected |> Seq.map string |> Set.ofSeq,
        graph.Keys |> Seq.map string |> Set.ofSeq
    )
    Assert.Equal(expected.Length, graph.Count)

let private assertPrerequisites
    (task: LoweredTask)
    (expected: LoweredTask list)
    (graph: TaskExecutor.TaskGraph<LoweredTask>) =
    Assert.Equal<Set<string>>(
        expected |> Seq.map string |> Set.ofSeq,
        graph[task] |> Seq.map string |> Set.ofSeq
    )
    Assert.Equal(expected.Length, graph[task].Count)

let private assertCycle (expected: FakeResource list) (roots: FakeResource list) =
    match lower CheckAndApply (roots |> Seq.cast<IResource>) with
    | Ok _ -> Assert.Fail "Cycle expected."
    | Error cycle ->
        Assert.Equal<string list>(
            expected |> List.map (fun r -> (r :> IResource).PresentableName),
            cycle |> List.map _.PresentableName
        )

[<Fact>]
let ``Check-only mode produces a check task for a single root``(): unit =
    let r = resource "R"
    let graph = lowerOk CheckOnly [ r ]
    assertTasks [ check r ] graph
    assertPrerequisites (check r) [] graph

[<Fact>]
let ``Check-only mode has independent check tasks for all reachable resources``(): unit =
    let r, d, e = resource "R", resource "D", resource "E"
    r.DependOn d
    d.DependOn e

    let graph = lowerOk CheckOnly [ r ]

    assertTasks [ check r; check d; check e ] graph
    assertPrerequisites (check r) [] graph
    assertPrerequisites (check d) [] graph
    assertPrerequisites (check e) [] graph

[<Fact>]
let ``Apply mode applies dependencies before dependents``(): unit =
    let r, d, e = resource "R", resource "D", resource "E"
    r.DependOn d
    d.DependOn e

    let graph = lowerOk CheckAndApply [ r ]

    assertTasks [ check r; check d; check e; apply r; apply d; apply e ] graph
    assertPrerequisites (check r) [] graph
    assertPrerequisites (check d) [] graph
    assertPrerequisites (check e) [] graph
    assertPrerequisites (apply r) [ check r; apply d ] graph
    assertPrerequisites (apply d) [ check d; apply e; check r ] graph
    assertPrerequisites (apply e) [ check e; check d; check r ] graph

[<Fact>]
let ``Shared dependency is lowered once and applied after all its dependents are checked``(): unit =
    let r1, r2, d = resource "R1", resource "R2", resource "D"
    r1.DependOn d
    r2.DependOn d

    let graph = lowerOk CheckAndApply [ r1; r2 ]

    assertTasks [ check r1; check r2; check d; apply r1; apply r2; apply d ] graph
    assertPrerequisites (check d) [] graph
    assertPrerequisites (apply d) [ check d; check r1; check r2 ] graph
    assertPrerequisites (apply r1) [ check r1; apply d ] graph
    assertPrerequisites (apply r2) [ check r2; apply d ] graph

[<Fact>]
let ``Root that is also a dependency of another root is lowered once``(): unit =
    let r, d = resource "R", resource "D"
    r.DependOn d

    let graph = lowerOk CheckAndApply [ r; d ]

    assertTasks [ check r; check d; apply r; apply d ] graph
    assertPrerequisites (apply r) [ check r; apply d ] graph
    assertPrerequisites (apply d) [ check d; check r ] graph

[<Fact>]
let ``Resources not reachable from roots are not lowered``(): unit =
    let r, d, unrelated = resource "R", resource "D", resource "Unrelated"
    r.DependOn d
    unrelated.DependOn r

    let graph = lowerOk CheckOnly [ r ]

    assertTasks [ check r; check d ] graph

[<Fact>]
let ``No roots produce an empty graph``(): unit =
    let graph = lowerOk CheckAndApply []
    Assert.Empty graph

type private EqualResource =
    { Name: string }
    interface IResource with
        member this.PresentableName = this.Name
        member _.AlreadyApplied() = async.Return true
        member _.Apply() = async.Return()
        member _.DependsOn = Resource.NoDependencies

[<Fact>]
let ``Equal resources are lowered as one resource``(): unit =
    let roots: IResource list = [ { Name = "R" }; { Name = "R" } ]
    match lower CheckAndApply roots with
    | Error _ -> Assert.Fail "Unexpected cycle."
    | Ok graph -> Assert.Equal(2, graph.Count)

[<Fact>]
let ``Self-dependency is reported as a cycle``(): unit =
    let r = resource "R"
    r.DependOn r
    assertCycle [ r; r ] [ r ]

[<Fact>]
let ``Indirect cycle is reported with its path``(): unit =
    let r, a, b, c = resource "R", resource "A", resource "B", resource "C"
    r.DependOn a
    a.DependOn b
    b.DependOn c
    c.DependOn a
    assertCycle [ a; b; c; a ] [ r ]

[<Fact>]
let ``Cycle among dependencies of a later root is detected``(): unit =
    let r1, r2, a, b = resource "R1", resource "R2", resource "A", resource "B"
    r2.DependOn a
    a.DependOn b
    b.DependOn a
    assertCycle [ a; b; a ] [ r1; r2 ]

[<Fact>]
let ``Diamond is not a cycle``(): unit =
    let a, b, c, d = resource "A", resource "B", resource "C", resource "D"
    a.DependOn(b, c)
    b.DependOn d
    c.DependOn d
    let graph = lowerOk CheckAndApply [ a ]
    assertPrerequisites (apply d) [ check d; check b; check c; check a ] graph
    assertPrerequisites (apply a) [ check a; apply b; apply c ] graph
