// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.Collections.Immutable
open System.Net.Http
open System.Net.Http.Headers
open System.Security
open System.Text
open System.Text.Json.Nodes
open Fabricator.Core
open Fabricator.Resources.ResourceUtil

/// <summary>
/// A DNS record managed by <see cref="M:Fabricator.Resources.DigitalOceanDns.Domain(System.String,Fabricator.Core.ISecretStore,Fabricator.Core.SecretName,System.Collections.Generic.IEnumerable{Fabricator.Resources.DigitalOceanDnsRecord},Microsoft.FSharp.Core.FSharpOption{System.Collections.Generic.IEnumerable{Fabricator.Core.Resource}})"/>. Use
/// <see cref="M:Fabricator.Resources.DigitalOceanDns.Record(System.String,System.String,System.String,Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.String})"/> to create one.
/// </summary>
type DigitalOceanDnsRecord =
    {
        /// The record type: A, AAAA, CAA, CNAME, MX, NS, SRV, or TXT.
        Type: string
        /// <summary>The name of the record, relative to the domain; <c>@</c> denotes the domain itself.</summary>
        Name: string
        /// <summary>
        /// The value of the record, e.g. an IP address, a text, or a host name. Host names must be fully qualified and
        /// end with a dot, e.g. <c>mail.example.com.</c>, or be <c>@</c> for the domain itself.
        /// </summary>
        Data: string
        /// The time to live of the record, in seconds.
        Ttl: int
        /// The priority, required for MX and SRV records.
        Priority: int option
        /// The port, required for SRV records.
        Port: int option
        /// The weight, required for SRV records.
        Weight: int option
        /// The flags (0–255), required for CAA records.
        Flags: int option
        /// <summary>The tag (<c>issue</c>, <c>issuewild</c>, or <c>iodef</c>), required for CAA records.</summary>
        Tag: string option
    }

/// Presentation, comparison, and validation of the DNS records.
module internal DigitalOceanDnsZone =

    let SupportedTypes = set [ "A"; "AAAA"; "CAA"; "CNAME"; "MX"; "NS"; "SRV"; "TXT" ]

    /// The types whose data is a host name.
    let private HostNameTypes = set [ "CNAME"; "MX"; "NS"; "SRV" ]

    /// The types whose data is case-sensitive.
    let private CaseSensitiveTypes = set [ "CAA"; "TXT" ]

    let private CaaTags = set [ "issue"; "issuewild"; "iodef" ]

    /// The CAA tags whose value is a domain name.
    let private CaaDomainTags = set [ "issue"; "issuewild" ]

    /// The optional fields required by the record type. Other optional fields are not applicable to it.
    let RequiredFields(recordType: string): Set<string> =
        match recordType with
        | "MX" -> set [ "priority" ]
        | "SRV" -> set [ "priority"; "port"; "weight" ]
        | "CAA" -> set [ "flags"; "tag" ]
        | _ -> Set.empty

    /// The header of the zone text, explaining the record lines.
    let Header = "# <name> <ttl> IN <type> <data>"

    /// Brings the domain name to its canonical form: lower-case, without the trailing dot.
    let normalizeDomain(domain: string): string =
        domain.Trim().TrimEnd('.').ToLowerInvariant()

    /// <summary>
    /// Brings the record to the form sent to DigitalOcean: upper-case type, lower-case name and tag, and trimmed data.
    /// </summary>
    let clean(record: DigitalOceanDnsRecord): DigitalOceanDnsRecord =
        { record with
            Type = record.Type.Trim().ToUpperInvariant()
            Name = record.Name.Trim().ToLowerInvariant()
            Data = record.Data.Trim()
            Tag = record.Tag |> Option.map _.Trim().ToLowerInvariant() }

    /// <summary>
    /// Brings the record of the <paramref name="domain"/> to the form in which equal records are equal: the
    /// <see cref="clean"/> form, with the data in lower case (except for the case-sensitive fields), and the host names
    /// without the trailing dot, or <c>@</c> if they denote the domain itself. DigitalOcean expects the fully qualified
    /// host names to end with a dot, but returns them without it, and returns the domain itself as <c>@</c>. The same
    /// goes for the domain names in the CAA <c>issue</c> and <c>issuewild</c> records.
    /// </summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="record">The record to normalize.</param>
    /// <returns>The normalized record.</returns>
    let normalize (domain: string) (record: DigitalOceanDnsRecord): DigitalOceanDnsRecord =
        let record = clean record
        let data = record.Data
        let data = if CaseSensitiveTypes.Contains record.Type then data else data.ToLowerInvariant()
        let data =
            if HostNameTypes.Contains record.Type then
                let hostName = if data.EndsWith "." then data.Substring(0, data.Length - 1) else data
                if hostName = domain then "@" else hostName
            elif record.Type = "CAA" && record.Tag |> Option.exists CaaDomainTags.Contains && data.EndsWith "." then
                data.Substring(0, data.Length - 1)
            else data
        { record with Data = data }

    /// SOA and NS records of the domain itself are managed by DigitalOcean.
    let isUnmanaged(record: DigitalOceanDnsRecord): bool =
        let record = clean record
        record.Type = "SOA" || record.Type = "NS" && record.Name = "@"

    let private quote(text: string) =
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""

    let private hostName(name: string) =
        if name = "@" || name.EndsWith "." then name else name + "."

    let private number(value: int option) =
        value |> Option.map string |> Option.defaultValue "?"

    /// Presents the record as a zone file line, e.g. <c>@ 1800 IN TXT "text"</c>.
    let line(record: DigitalOceanDnsRecord): string =
        let data =
            match record.Type with
            | "MX" -> [ number record.Priority; hostName record.Data ]
            | "SRV" -> [ number record.Priority; number record.Weight; number record.Port; hostName record.Data ]
            | "CAA" -> [ number record.Flags; defaultArg record.Tag "?"; quote record.Data ]
            | "TXT" -> [ quote record.Data ]
            | "CNAME" | "NS" -> [ hostName record.Data ]
            | _ -> [ record.Data ]
        $"""{record.Name} {record.Ttl} IN {record.Type} {String.Join(" ", data)}"""

    /// The key identifying equal records of the domain.
    let key (domain: string) (record: DigitalOceanDnsRecord): string = line(normalize domain record)

    /// Presents the records of the domain as a zone text with a header, sorted for a stable diff.
    let text (domain: string) (records: DigitalOceanDnsRecord seq): string =
        let lines = records |> Seq.map (key domain) |> Seq.sort
        Seq.append [ Header ] lines |> Seq.map (fun l -> l + "\n") |> String.concat ""

    /// <summary>
    /// Plans the changes bringing the <paramref name="existing"/> records to the <paramref name="desired"/> ones.
    /// </summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="existing">A collection of the already existing records.</param>
    /// <param name="desired">A collection of the desired (target) records.</param>
    /// <returns>The existing records to delete, and the desired records to create, in their original order.</returns>
    let plan (domain: string) (existing: ('id * DigitalOceanDnsRecord) seq) (desired: DigitalOceanDnsRecord seq)
            : ('id * DigitalOceanDnsRecord)[] * DigitalOceanDnsRecord[] =
        let key = key domain
        let desiredKeys = desired |> Seq.map key |> Set.ofSeq
        let toDelete, kept =
            existing
            |> Seq.fold (fun (toDelete, kept) (id, record) ->
                let k = key record
                if desiredKeys.Contains k && not (Set.contains k kept) then toDelete, Set.add k kept
                else (id, record) :: toDelete, kept
            ) ([], Set.empty)
        Seq.rev toDelete |> Seq.toArray, desired |> Seq.filter (fun r -> not (kept.Contains(key r))) |> Seq.toArray

    let private problems (domain: string) (original: DigitalOceanDnsRecord): string[] =
        let record = clean original
        let recordType = record.Type
        let name = record.Name
        let fields = [
            "priority", record.Priority.IsSome
            "port", record.Port.IsSome
            "weight", record.Weight.IsSome
            "flags", record.Flags.IsSome
            "tag", record.Tag.IsSome
        ]
        [|
            if isUnmanaged record then
                "SOA and NS records of the domain itself are managed by DigitalOcean and are not managed by this " +
                "resource"
            elif not (SupportedTypes.Contains recordType) then
                $"unsupported record type \"{original.Type}\""
            else
                let required = RequiredFields recordType
                for field, present in fields do
                    match required.Contains field, present with
                    | true, false -> $"{recordType} records require {field}"
                    | false, true -> $"{field} is not applicable to {recordType} records"
                    | _ -> ()
                match record.Tag with
                | Some tag when recordType = "CAA" && not (CaaTags.Contains tag) ->
                    $"unsupported CAA tag \"{original.Tag |> Option.defaultValue tag}\""
                | _ -> ()

            if name = "" then "the name is empty"
            elif name.EndsWith "." || name = domain || name.EndsWith("." + domain) then
                "the name should be relative to the domain (use \"@\" for the domain itself)"

            if String.IsNullOrWhiteSpace record.Data then "the data is empty"
            elif HostNameTypes.Contains recordType && record.Data <> "@" && not (record.Data.EndsWith ".") then
                "host names should be fully qualified, ending with a dot, or \"@\""

            // https://docs.digitalocean.com/products/networking/dns/details/limits/
            if record.Ttl < 30 then "the TTL should be at least 30 seconds"
        |]

    /// <summary>
    /// Checks the records declared for the <paramref name="domain"/>, and throws an
    /// <see cref="T:System.ArgumentException"/> listing all the problems if any.
    /// </summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="records">The records declared for the domain.</param>
    /// <exception cref="T:System.ArgumentException">
    /// The <paramref name="records"/> are invalid.
    /// </exception>
    let validate (domain: string) (records: DigitalOceanDnsRecord seq): unit =
        let checkedRecords = records |> Seq.map (fun record -> record, problems domain record) |> Seq.toArray
        let invalid = checkedRecords |> Seq.filter (fun (_, problems) -> not (Array.isEmpty problems))
        let duplicates =
            checkedRecords
            |> Array.filter (fun (_, problems) -> Array.isEmpty problems)
            |> Array.map fst
            |> Array.groupBy (key domain)
            |> Seq.filter (fun (_, group) -> group.Length > 1)
            |> Seq.map (fun (_, group) -> Array.head group, [| $"declared {group.Length} times" |])
        match Seq.concat [ invalid; duplicates ] |> Seq.toArray with
        | [||] -> ()
        | errors ->
            let lines = errors |> Seq.map (fun (record, problems) ->
                $"""- {line record}: {String.Join("; ", problems)}"""
            )
            let message = $"Invalid DNS records declared for the domain \"{domain}\":\n" + String.Join("\n", lines)
            raise <| ArgumentException(message, "records")

/// A client of the DigitalOcean domain records API.
module internal DigitalOceanDnsApi =

    let BaseUri = Uri "https://api.digitalocean.com/v2/"

    let createClient (handler: HttpMessageHandler option) (token: SecureString): HttpClient =
        let client =
            match handler with
            | Some handler -> new HttpClient(handler, disposeHandler = false)
            | None -> new HttpClient()
        client.BaseAddress <- BaseUri
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", Secrets.Materialize token)
        client

    let private send (client: HttpClient) (method: HttpMethod) (uri: Uri) (content: HttpContent option) = async {
        use request = new HttpRequestMessage(method, uri)
        content |> Option.iter (fun c -> request.Content <- c)
        let! ct = Async.CancellationToken
        let! response = Async.AwaitTask <| client.SendAsync(request, ct)
        use response = response
        let! body = Async.AwaitTask <| response.Content.ReadAsStringAsync ct
        if not response.IsSuccessStatusCode then
            raise <| HttpRequestException(
                $"DigitalOcean API request {method} {uri} failed with status {int response.StatusCode} " +
                $"({response.StatusCode}): {body}",
                null,
                response.StatusCode
            )
        return body
    }

    let private recordsUri(domain: string) =
        Uri($"domains/{Uri.EscapeDataString domain}/records", UriKind.Relative)

    let private parseRecord(node: JsonNode): int64 * DigitalOceanDnsRecord =
        let field (name: string) = node[name]
        let string (name: string) = (nonNull (field name)).GetValue<string>()
        let recordType = string "type"
        let applicable = DigitalOceanDnsZone.RequiredFields recordType
        let optional (name: string) (read: JsonNode -> 'a) =
            match field name with
            | null -> None
            | value when applicable.Contains name -> Some(read value)
            | _ -> None
        let int (value: JsonNode) = value.GetValue<int>()
        (nonNull (field "id")).GetValue<int64>(), {
            Type = recordType
            Name = string "name"
            Data = string "data"
            Ttl = (nonNull (field "ttl")).GetValue<int>()
            Priority = optional "priority" int
            Port = optional "port" int
            Weight = optional "weight" int
            Flags = optional "flags" int
            Tag = optional "tag" _.GetValue<string>()
        }

    /// Lists all the records of the domain, with their identifiers.
    let listRecords (client: HttpClient) (domain: string): Async<(int64 * DigitalOceanDnsRecord)[]> = async {
        let records = ResizeArray()
        let mutable next = Some(Uri(recordsUri(domain).OriginalString + "?per_page=200", UriKind.Relative))
        while next.IsSome do
            let! body = send client HttpMethod.Get next.Value None
            let document = (nonNull (JsonNode.Parse body)).AsObject()
            for item in (nonNull document["domain_records"]).AsArray() do
                records.Add(parseRecord(nonNull item))
            next <-
                match document["links"] with
                | null -> None
                | links ->
                    match links["pages"] with
                    | null -> None
                    | pages ->
                        match pages["next"] with
                        | null -> None
                        // Only take the path and query, so the token is never sent to another host.
                        | nextUri -> Some(Uri(Uri(nextUri.GetValue<string>()).PathAndQuery, UriKind.Relative))
        return Array.ofSeq records
    }

    let createRecord (client: HttpClient) (domain: string) (record: DigitalOceanDnsRecord): Async<unit> = async {
        let body = JsonObject()
        body.Add("type", JsonValue.Create record.Type)
        body.Add("name", JsonValue.Create record.Name)
        body.Add("data", JsonValue.Create record.Data)
        body.Add("ttl", JsonValue.Create record.Ttl)
        let addOptional (name: string) (value: int option) =
            value |> Option.iter (fun v -> body.Add(name, JsonValue.Create v))
        addOptional "priority" record.Priority
        addOptional "port" record.Port
        addOptional "weight" record.Weight
        addOptional "flags" record.Flags
        record.Tag |> Option.iter (fun tag -> body.Add("tag", JsonValue.Create tag))
        use content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        let! _ = send client HttpMethod.Post (recordsUri domain) (Some content)
        ()
    }

    let deleteRecord (client: HttpClient) (domain: string) (id: int64): Async<unit> = async {
        let uri = Uri($"{recordsUri(domain).OriginalString}/{id}", UriKind.Relative)
        let! _ = send client HttpMethod.Delete uri None
        ()
    }

/// <summary>
/// Resources managing the DNS records of the domains hosted by
/// <a href="https://docs.digitalocean.com/products/networking/dns/">DigitalOcean DNS</a>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="M:Fabricator.Resources.DigitalOceanDns.Domain(System.String,Fabricator.Core.ISecretStore,Fabricator.Core.SecretName,System.Collections.Generic.IEnumerable{Fabricator.Resources.DigitalOceanDnsRecord},Microsoft.FSharp.Core.FSharpOption{System.Collections.Generic.IEnumerable{Fabricator.Core.Resource}})"/>
/// manages all the DNS records of a domain: when applied, it deletes the records not declared, and creates the missing
/// ones. The SOA and NS records of the domain itself (<c>@</c>) are managed by DigitalOcean and are ignored. The
/// changes are shown as a diff of the records in the zone file format.
/// </para>
/// <para>
/// The resources require a DigitalOcean API token, taken from a secret store. Use a
/// <a href="https://docs.digitalocean.com/reference/api/create-personal-access-token/">personal access token</a>:
/// generate it in the DigitalOcean control panel, in <b>API</b> → <b>Tokens</b> → <b>Generate New Token</b>
/// (https://cloud.digitalocean.com/account/api/tokens). Give it either full access, or the custom scopes
/// <c>domain:read</c>, <c>domain:create</c>, and <c>domain:delete</c>.
/// </para>
/// </remarks>
/// <example>
/// To make <c>example.com</c> have a single TXT record <c>TXT @ mytext</c>, with the token stored in a KeePass database
/// (see the <c>FVNever.Fabricator.Secrets.KeePass</c> package):
/// <code lang="fsharp">
/// open Fabricator.Core
/// open Fabricator.Resources
/// open Fabricator.Secrets.KeePass
/// open TruePath
///
/// let secrets = KeePassSecretStore(AbsolutePath @"C:\Secrets\infra.kdbx")
/// let dns = DigitalOceanDns.Domain(
///     "example.com",
///     secrets, SecretName "digitalocean-token",
///     [
///         DigitalOceanDns.Record("TXT", "@", "mytext")
///     ]
/// )
/// </code>
/// </example>
type DigitalOceanDns =

    /// <summary>Creates a DNS record to pass to <see cref="M:Fabricator.Resources.DigitalOceanDns.Domain(System.String,Fabricator.Core.ISecretStore,Fabricator.Core.SecretName,System.Collections.Generic.IEnumerable{Fabricator.Resources.DigitalOceanDnsRecord},Microsoft.FSharp.Core.FSharpOption{System.Collections.Generic.IEnumerable{Fabricator.Core.Resource}})"/>.</summary>
    /// <param name="recordType">The record type: A, AAAA, CAA, CNAME, MX, NS, SRV, or TXT.</param>
    /// <param name="name">The name of the record, relative to the domain; <c>@</c> denotes the domain itself.</param>
    /// <param name="data">
    /// The value of the record. Host names must be fully qualified and end with a dot, e.g.
    /// <c>mail.example.com.</c>, or be <c>@</c> for the domain itself.
    /// </param>
    /// <param name="ttl">The time to live of the record, in seconds. 1800 by default.</param>
    /// <param name="priority">The priority, required for MX and SRV records.</param>
    /// <param name="port">The port, required for SRV records.</param>
    /// <param name="weight">The weight, required for SRV records.</param>
    /// <param name="flags">The flags (0–255), required for CAA records.</param>
    /// <param name="tag">The tag (<c>issue</c>, <c>issuewild</c>, or <c>iodef</c>), required for CAA records.</param>
    static member Record(
        recordType: string,
        name: string,
        data: string,
        ?ttl: int,
        ?priority: int,
        ?port: int,
        ?weight: int,
        ?flags: int,
        ?tag: string
    ): DigitalOceanDnsRecord = {
        Type = recordType
        Name = name
        Data = data
        Ttl = defaultArg ttl 1800
        Priority = priority
        Port = port
        Weight = weight
        Flags = flags
        Tag = tag
    }

    /// <summary>The concurrency group of the resources managing the records of the specified domain.</summary>
    /// <param name="domain">The domain name.</param>
    static member ConcurrencyGroup(domain: string): ConcurrencyGroup =
        ConcurrencyGroup $"DigitalOceanDns:{DigitalOceanDnsZone.normalizeDomain domain}"

    static member internal Create(
        domain: string,
        tokenStore: ISecretStore,
        tokenName: SecretName,
        records: DigitalOceanDnsRecord seq,
        dependsOn: Resource seq option,
        handler: HttpMessageHandler option
    ): Resource =
        let domain = DigitalOceanDnsZone.normalizeDomain domain
        if domain = "" then raise <| ArgumentException("The domain name is empty.", nameof domain)
        let records = Array.ofSeq records
        DigitalOceanDnsZone.validate domain records
        let desired = records |> Array.map DigitalOceanDnsZone.clean

        let token = { Store = tokenStore; Name = tokenName; RequestMode = SecretRequestMode.CheckAndApply }
        let openClient(ctx: ResourceContext) =
            use secret = ctx.Secrets[token]
            DigitalOceanDnsApi.createClient handler secret

        let listManagedRecords client (ctx: ResourceContext) = async {
            ctx.Reporter.Status "Reading the records"
            let! records = DigitalOceanDnsApi.listRecords client domain
            return records |> Array.filter (fun (_, r) -> not (DigitalOceanDnsZone.isUnmanaged r))
        }

        let change(existing: (int64 * DigitalOceanDnsRecord) seq) =
            Diffs.textChange
                $"DigitalOcean DNS zone \"{domain}\""
                (Some(DigitalOceanDnsZone.text domain (Seq.map snd existing)))
                (DigitalOceanDnsZone.text domain desired)

        {
            PresentableName = $"DigitalOcean DNS records of \"{domain}\""
            DependsOn = dependencies dependsOn
            Lock = Some(DigitalOceanDns.ConcurrencyGroup domain)
            Secrets = ImmutableArray.Create token

            AlreadyApplied = fun ctx -> async {
                use client = openClient ctx
                let! existing = listManagedRecords client ctx
                return change existing
            }

            Apply = fun ctx -> async {
                use client = openClient ctx
                let! existing = listManagedRecords client ctx
                let toDelete, toCreate = DigitalOceanDnsZone.plan domain existing desired

                // Deleting first avoids conflicts of the new records with the old ones, e.g. of CNAME records.
                do! ctx.Reporter.WithProgress(
                    "Deleting existing records",
                    Some(toDelete.Length),
                    ProgressUnit.Items,
                    fun reporter -> async {
                        for index, (id, record) in Seq.indexed toDelete do
                            ctx.Reporter.Log $"Deleting record: {DigitalOceanDnsZone.key domain record}"
                            do! DigitalOceanDnsApi.deleteRecord client domain id
                            reporter.Report(int64(index + 1))
                    }
                )

                do! ctx.Reporter.WithProgress(
                    "Creating new records",
                    Some(toCreate.Length),
                    ProgressUnit.Items,
                    fun reporter -> async {
                        for index, record in Seq.indexed toCreate do
                            ctx.Reporter.Log $"Creating record: {DigitalOceanDnsZone.key domain record}"
                            do! DigitalOceanDnsApi.createRecord client domain record
                            reporter.Report(int64(index + 1))
                    }
                )

                return change existing
            }
        }

    /// <summary>
    /// Creates a resource managing all the DNS records of a domain hosted by
    /// <a href="https://docs.digitalocean.com/products/networking/dns/">DigitalOcean DNS</a>.
    /// </summary>
    /// <param name="domain">
    /// The domain name, e.g. <c>example.com</c>. It should already be added to DigitalOcean.
    /// </param>
    /// <param name="tokenStore">The secret store to get the DigitalOcean API token from.</param>
    /// <param name="tokenName">The name of the DigitalOcean API token in the store.</param>
    /// <param name="records">
    /// All the records the domain should have, created via <see cref="M:Fabricator.Resources.DigitalOceanDns.Record(System.String,System.String,System.String,Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.Int32},Microsoft.FSharp.Core.FSharpOption{System.String})"/>.
    /// </param>
    /// <param name="dependsOn">The resources this resource depends on.</param>
    /// <returns>A resource managing the DNS records of the domain.</returns>
    /// <remarks>
    /// <para>
    /// The resource owns all the records of the domain: when applied, it deletes the records not passed in
    /// <paramref name="records"/>, and creates the missing ones. A changed record (e.g. one with another TTL) is
    /// deleted and created again. The only exceptions are the SOA and NS records of the domain itself (<c>@</c>):
    /// DigitalOcean manages them, so the resource ignores them, and doesn't allow to declare them.
    /// </para>
    /// <para>
    /// The changes are shown as a diff of the records in the zone file format, one record per line. Every deletion
    /// and creation is written to the log before it is executed.
    /// </para>
    /// <para>
    /// The token is required for both the check and the application. See
    /// <see cref="T:Fabricator.Resources.DigitalOceanDns"/> for how to get it, and for an example.
    /// </para>
    /// <para>
    /// Throws an <see cref="T:System.ArgumentException"/> listing all the invalid records if any of the
    /// <paramref name="records"/> is the SOA or NS record of the domain itself, has an unsupported type, a name that is
    /// not relative to the domain, empty data, a host name that is not fully qualified, a TTL less than 30 seconds,
    /// misses a field required by its type or has a field not applicable to it, has an unsupported CAA tag, or is
    /// declared several times.
    /// </para>
    /// </remarks>
    static member Domain(
        domain: string,
        tokenStore: ISecretStore,
        tokenName: SecretName,
        records: DigitalOceanDnsRecord seq,
        ?dependsOn: Resource seq
    ): Resource =
        DigitalOceanDns.Create(domain, tokenStore, tokenName, records, dependsOn, None)
