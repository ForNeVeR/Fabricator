// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.DigitalOceanDnsTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open System.Web
open Xunit
open Fabricator.Core
open Fabricator.Resources
open type Fabricator.Resources.DigitalOceanDns
open Fabricator.Tests.FakeResource
open Fabricator.Tests.FakeSecretStore

let private domain = "example.com"
let private token = "test-token"

/// <summary>
/// An in-memory DigitalOcean domain records API. Records its requests to the log: <c>GET page=N</c>,
/// <c>DELETE id</c>, and <c>POST type name data</c>.
/// </summary>
type private FakeDigitalOceanApi(log: EventLog) =
    inherit HttpMessageHandler()

    let records = ResizeArray<int64 * DigitalOceanDnsRecord>()
    let mutable nextId = 1L

    member val Authorization: string option = None with get, set
    member val Failure: (HttpStatusCode * string) option = None with get, set

    member _.Records: DigitalOceanDnsRecord list = records |> Seq.map snd |> List.ofSeq

    member _.Add(record: DigitalOceanDnsRecord): unit =
        records.Add(nextId, record)
        nextId <- nextId + 1L

    /// Adds the records DigitalOcean creates for every domain.
    member this.AddDefaults(): unit =
        this.Add(DigitalOceanDns.Record("SOA", "@", "1800"))
        for i in 1..3 do
            this.Add(DigitalOceanDns.Record("NS", "@", $"ns{i}.digitalocean.com"))

    /// <summary>
    /// The data as DigitalOcean is believed to store it: host names without the trailing dot, and <c>@</c> for the
    /// domain itself.
    /// </summary>
    static member private StoredData(recordType: string, data: string): string =
        match recordType with
        | "CNAME" | "MX" | "NS" | "SRV" ->
            let hostName = if data.EndsWith "." then data.Substring(0, data.Length - 1) else data
            if hostName = domain then "@" else hostName
        | _ -> data

    static member private ToJson(id: int64, record: DigitalOceanDnsRecord): JsonNode =
        let nullable (value: JsonNode option): JsonNode | null = match value with Some v -> v | None -> null
        let number (value: int option) = value |> Option.map (fun v -> JsonValue.Create v :> JsonNode) |> nullable
        JsonObject(dict [
            "id", JsonValue.Create id :> JsonNode | null
            "type", JsonValue.Create record.Type
            "name", JsonValue.Create record.Name
            "data", JsonValue.Create record.Data
            "ttl", JsonValue.Create record.Ttl
            "priority", number record.Priority
            "port", number record.Port
            "weight", number record.Weight
            "flags", number record.Flags
            "tag", match record.Tag with Some tag -> JsonValue.Create tag :> JsonNode | null | None -> null
        ])

    member private this.Handle(request: HttpRequestMessage): Task<HttpResponseMessage> = task {
        this.Authorization <- request.Headers.Authorization |> Option.ofObj |> Option.map string
        let uri = nonNull request.RequestUri
        let recordsPath = $"/v2/domains/{domain}/records"
        let respond (status: HttpStatusCode) (body: string) =
            new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))
        match this.Failure with
        | Some(status, body) -> return respond status body
        | None ->

        if request.Method = HttpMethod.Get && uri.AbsolutePath = recordsPath then
            let query = HttpUtility.ParseQueryString uri.Query
            let page = query["page"] |> Option.ofObj |> Option.map int |> Option.defaultValue 1
            let perPage = query["per_page"] |> Option.ofObj |> Option.map int |> Option.defaultValue 20
            log.Add $"GET page={page}"
            let items =
                records
                |> Seq.skip ((page - 1) * perPage)
                |> Seq.truncate perPage
                |> Seq.map FakeDigitalOceanApi.ToJson
            let pages = JsonObject()
            if page * perPage < records.Count then
                let next = $"https://api.digitalocean.com{recordsPath}?page={page + 1}&per_page={perPage}"
                pages.Add("next", JsonValue.Create next)
            let body = JsonObject(dict [
                "domain_records", JsonArray(Array.ofSeq items) :> JsonNode | null
                "links", JsonObject(dict [ "pages", pages :> JsonNode | null ])
                "meta", JsonObject(dict [ "total", JsonValue.Create records.Count :> JsonNode | null ])
            ])
            return respond HttpStatusCode.OK (body.ToJsonString())
        elif request.Method = HttpMethod.Post && uri.AbsolutePath = recordsPath then
            let! content = (nonNull request.Content).ReadAsStringAsync()
            let json = nonNull (JsonNode.Parse content)
            let field (name: string) = json[name] |> Option.ofObj
            let recordType = (nonNull json["type"]).GetValue<string>()
            let record = {
                Type = recordType
                Name = (nonNull json["name"]).GetValue<string>()
                Data = FakeDigitalOceanApi.StoredData(recordType, (nonNull json["data"]).GetValue<string>())
                Ttl = (nonNull json["ttl"]).GetValue<int>()
                Priority = field "priority" |> Option.map _.GetValue<int>()
                Port = field "port" |> Option.map _.GetValue<int>()
                Weight = field "weight" |> Option.map _.GetValue<int>()
                Flags = field "flags" |> Option.map _.GetValue<int>()
                Tag = field "tag" |> Option.map _.GetValue<string>()
            }
            log.Add $"POST {record.Type} {record.Name} {record.Data}"
            let id = nextId
            this.Add record
            let body = JsonObject(dict [ "domain_record", FakeDigitalOceanApi.ToJson(id, record) ])
            return respond HttpStatusCode.Created (body.ToJsonString())
        elif request.Method = HttpMethod.Delete && uri.AbsolutePath.StartsWith(recordsPath + "/") then
            let id = int64 (uri.AbsolutePath.Substring(recordsPath.Length + 1))
            log.Add $"DELETE {id}"
            match Seq.tryFindIndex (fun (recordId, _) -> recordId = id) records with
            | Some index ->
                records.RemoveAt index
                return new HttpResponseMessage(HttpStatusCode.NoContent)
            | None ->
                return respond HttpStatusCode.NotFound """{"id":"not_found","message":"The resource was not found."}"""
        else
            return respond HttpStatusCode.NotFound """{"id":"not_found","message":"Unknown endpoint."}"""
    }

    override this.SendAsync(request: HttpRequestMessage, _: CancellationToken): Task<HttpResponseMessage> =
        this.Handle request

let private create (api: FakeDigitalOceanApi) (log: EventLog) (records: DigitalOceanDnsRecord list): Resource =
    let store = FakeSecretStore("store", log)
    DigitalOceanDns.Create(domain, store, SecretName "digitalocean-token", records, None, Some api)

/// A fresh context for every operation, since the resource disposes the secrets it receives.
let private context (log: EventLog) (resource: Resource): ResourceContext = {
    Reporter = {
        new IReporter with
            member _.Status _ = ()
            member _.Log message = log.Add $"log: {message}"
            member _.WithProgress(_, _, _, action) = action ProgressReporter.Null
    }
    Secrets = Dictionary(resource.Secrets |> Seq.map (fun s -> KeyValuePair(s, Secrets.OfString token)))
}

let private check (log: EventLog) (resource: Resource) =
    resource.AlreadyApplied(context log resource) |> Async.StartAsTask

let private apply (log: EventLog) (resource: Resource) =
    resource.Apply(context log resource) |> Async.StartAsTask

let private zone (lines: string list) =
    "# <name> <ttl> IN <type> <data>\n" + (lines |> Seq.map (fun l -> l + "\n") |> String.concat "")

[<Fact>]
let ``Matching zone requires no changes``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.AddDefaults()
    api.Add(Record("TXT", "@", "my-text"))
    api.Add(Record("CNAME", "www", "example.org"))
    api.Add(Record("MX", "@", "mail.example.org", priority = 10))
    let resource = create api log [
        Record("txt", "@", "my-text")
        Record("CNAME", "WWW", "Example.org.")
        Record("MX", "@", "mail.example.org.", priority = 10)
    ]
    let! change = check log resource
    Assert.Equal(NoChanges, change)
}

[<Fact>]
let ``Check shows the zone diff``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.AddDefaults()
    api.Add(Record("A", "@", "1.2.3.4"))
    api.Add(Record("TXT", "@", "keep"))
    let resource = create api log [ Record("TXT", "@", "keep"); Record("TXT", "@", "my-text") ]
    let! change = check log resource
    let expected = TextDiff {
        Name = "DigitalOcean DNS zone \"example.com\""
        OldText = Some(zone [ "@ 1800 IN A 1.2.3.4"; "@ 1800 IN TXT \"keep\"" ])
        NewText = zone [ "@ 1800 IN TXT \"keep\""; "@ 1800 IN TXT \"my-text\"" ]
    }
    Assert.Equal(expected, change)
}

[<Fact>]
let ``Apply deletes the extra records and creates the missing ones, logging every action first``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.AddDefaults() // IDs 1–4
    api.Add(Record("A", "@", "1.2.3.4")) // 5
    api.Add(Record("TXT", "@", "keep")) // 6
    api.Add(Record("A", "www", "1.2.3.4")) // 7
    let desired = [ Record("TXT", "@", "keep"); Record("TXT", "@", "my-text"); Record("CNAME", "blog", "example.org.") ]
    let resource = create api log desired

    let! change = apply log resource
    Assert.NotEqual(NoChanges, change)
    Assert.Equal<string seq>([
        "GET page=1"
        "log: Deleting record: @ 1800 IN A 1.2.3.4"
        "DELETE 5"
        "log: Deleting record: www 1800 IN A 1.2.3.4"
        "DELETE 7"
        "log: Creating record: @ 1800 IN TXT \"my-text\""
        "POST TXT @ my-text"
        "log: Creating record: blog 1800 IN CNAME example.org."
        "POST CNAME blog example.org"
    ], log.Events)

    let! change = check log resource
    Assert.Equal(NoChanges, change)
}

[<Fact>]
let ``SOA and NS records of the domain itself are ignored``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.AddDefaults()
    api.Add(Record("NS", "sub", "ns.example.org"))
    let resource = create api log []

    let! change = check log resource
    match change with
    | TextDiff diff -> Assert.Equal(Some(zone [ "sub 1800 IN NS ns.example.org." ]), diff.OldText)
    | other -> Assert.Fail $"Unexpected change: {other}"

    let! _ = apply log resource
    Assert.Equal<DigitalOceanDnsRecord list>([
        Record("SOA", "@", "1800")
        Record("NS", "@", "ns1.digitalocean.com")
        Record("NS", "@", "ns2.digitalocean.com")
        Record("NS", "@", "ns3.digitalocean.com")
    ], api.Records)
}

[<Fact>]
let ``Host name of the domain itself matches the short form``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.Add(Record("MX", "@", "@", priority = 10))
    let resource = create api log [ Record("MX", "@", "Example.com.", priority = 10) ]
    let! change = check log resource
    Assert.Equal(NoChanges, change)
}

[<Fact>]
let ``Applied host name of the domain itself requires no further changes``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    let resource = create api log [ Record("CNAME", "www", "example.com.") ]
    let! _ = apply log resource
    Assert.Equal<DigitalOceanDnsRecord list>([ Record("CNAME", "www", "@") ], api.Records)
    let! change = check log resource
    Assert.Equal(NoChanges, change)
}

let private assertInvalid (records: DigitalOceanDnsRecord list) (expectedProblems: string list) =
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    let e = Assert.Throws<ArgumentException>(fun () -> create api log records |> ignore)
    Assert.Equal("records", e.ParamName)
    for problem in expectedProblems do
        Assert.Contains(problem, e.Message)
    Assert.Empty log.Events

[<Fact>]
let ``Apex NS record cannot be declared``(): unit =
    assertInvalid [ Record("NS", "@", "ns1.digitalocean.com.") ] [
        "@ 1800 IN NS ns1.digitalocean.com.: SOA and NS records of the domain itself are managed by DigitalOcean"
    ]

[<Fact>]
let ``SOA record cannot be declared``(): unit =
    assertInvalid [ Record("SOA", "@", "1800") ] [ "SOA and NS records of the domain itself" ]

[<Fact>]
let ``Unsupported record type cannot be declared``(): unit =
    assertInvalid [ Record("PTR", "1", "host.example.com.") ] [ "unsupported record type \"PTR\"" ]

[<Fact>]
let ``Record name should be relative``(): unit =
    let problem = "the name should be relative to the domain"
    assertInvalid [ Record("A", "www.", "1.2.3.4") ] [ problem ]
    assertInvalid [ Record("A", "www.example.com", "1.2.3.4") ] [ problem ]
    assertInvalid [ Record("A", "example.com", "1.2.3.4") ] [ problem ]

[<Fact>]
let ``Record data should not be empty``(): unit =
    assertInvalid [ Record("TXT", "@", "") ] [ "the data is empty" ]

[<Fact>]
let ``Record TTL should be at least 30 seconds``(): unit =
    assertInvalid [ Record("TXT", "@", "my-text", ttl = 10) ] [ "the TTL should be at least 30 seconds" ]

[<Fact>]
let ``Record fields should match the type``(): unit =
    assertInvalid [ Record("MX", "@", "mail.example.com.") ] [ "MX records require priority" ]
    assertInvalid [ Record("TXT", "@", "my-text", priority = 10) ] [ "priority is not applicable to TXT records" ]

[<Fact>]
let ``Duplicate records cannot be declared``(): unit =
    assertInvalid [ Record("TXT", "@", "my-text"); Record("txt", "@", "my-text") ] [
        "@ 1800 IN TXT \"my-text\": declared 2 times"
    ]

[<Fact>]
let ``All invalid records are reported``(): unit =
    assertInvalid [ Record("NS", "@", "ns1.digitalocean.com."); Record("PTR", "1", "host.example.com.") ] [
        "@ 1800 IN NS ns1.digitalocean.com."
        "1 1800 IN PTR host.example.com."
    ]

[<Fact>]
let ``All pages of the records are read``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    let records = [ for i in 0..449 -> Record("A", $"host{i}", $"10.0.{i / 256}.{i % 256}") ]
    records |> List.iter api.Add
    let resource = create api log records
    let! change = check log resource
    Assert.Equal(NoChanges, change)
    Assert.Equal<string seq>([ "GET page=1"; "GET page=2"; "GET page=3" ], log.Events)
}

[<Fact>]
let ``Token is sent as a bearer token``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    let resource = create api log []
    let! _ = check log resource
    Assert.Equal(Some $"Bearer {token}", api.Authorization)
}

[<Fact>]
let ``API error is reported``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    let body = """{"id":"unauthorized","message":"Unable to authenticate you."}"""
    api.Failure <- Some(HttpStatusCode.Unauthorized, body)
    let resource = create api log []
    let! e = Assert.ThrowsAnyAsync<exn>(fun () -> check log resource :> Task)
    // Async.AwaitTask wraps the exceptions from the awaited tasks.
    let e = match e with :? AggregateException as e -> nonNull e.InnerException | e -> e
    let e = Assert.IsType<HttpRequestException> e
    Assert.Equal(Nullable HttpStatusCode.Unauthorized, e.StatusCode)
    Assert.Contains("401", e.Message)
    Assert.Contains("Unable to authenticate you.", e.Message)
}

[<Fact>]
let ``Changed TTL recreates the record``(): Task = task {
    let log = EventLog()
    let api = new FakeDigitalOceanApi(log)
    api.Add(Record("TXT", "@", "my-text"))
    let resource = create api log [ Record("TXT", "@", "my-text", ttl = 3600) ]
    let! _ = apply log resource
    Assert.Equal<string seq>([
        "GET page=1"
        "log: Deleting record: @ 1800 IN TXT \"my-text\""
        "DELETE 1"
        "log: Creating record: @ 3600 IN TXT \"my-text\""
        "POST TXT @ my-text"
    ], log.Events)
    Assert.Equal<DigitalOceanDnsRecord list>([ Record("TXT", "@", "my-text", ttl = 3600) ], api.Records)
}

[<Theory>]
[<InlineData("TXT", "@", "say \"hi\" \\o/", "@ 1800 IN TXT \"say \\\"hi\\\" \\\\o/\"")>]
[<InlineData("A", "www", "1.2.3.4", "www 1800 IN A 1.2.3.4")>]
[<InlineData("AAAA", "www", "2001:DB8::1", "www 1800 IN AAAA 2001:db8::1")>]
[<InlineData("CNAME", "blog", "@", "blog 1800 IN CNAME @")>]
[<InlineData("CNAME", "blog", "Example.org.", "blog 1800 IN CNAME example.org.")>]
let ``Records are presented as zone lines``(recordType: string, name: string, data: string, expected: string): unit =
    Assert.Equal(expected, DigitalOceanDnsZone.key domain (Record(recordType, name, data)))

[<Fact>]
let ``Records with extra fields are presented as zone lines``(): unit =
    Assert.Equal(
        "@ 3600 IN MX 10 mail.example.com.",
        DigitalOceanDnsZone.key domain (Record("MX", "@", "mail.example.com", ttl = 3600, priority = 10))
    )
    Assert.Equal(
        "_sip._tcp 1800 IN SRV 10 5 5060 sip.example.com.",
        DigitalOceanDnsZone.key domain (
            Record("SRV", "_sip._tcp", "sip.example.com.", priority = 10, weight = 5, port = 5060)
        )
    )
    Assert.Equal(
        "@ 1800 IN CAA 0 issue \"letsencrypt.org\"",
        DigitalOceanDnsZone.key domain (Record("CAA", "@", "letsencrypt.org", flags = 0, tag = "issue"))
    )
