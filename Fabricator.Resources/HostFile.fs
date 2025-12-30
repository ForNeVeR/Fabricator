// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.IO
open Fabricator.Core

type HostFile =
    /// <summary>
    /// Creates a resource for managing a host file entry.
    /// </summary>
    /// <param name="ipAddress">The IP address for the host entry.</param>
    /// <param name="host">The hostname to map to the IP address.</param>
    /// <param name="hostsFilePath">Optional path to the hosts file. Defaults to Windows hosts file path.
    /// Note: This resource is designed for Windows only at this stage.</param>
    /// <returns>An IResource for managing the host file entry.</returns>
    static member Record(ipAddress: string, host: string, ?hostsFilePath: string): IResource =
        let defaultHostsPath = @"C:\Windows\System32\drivers\etc\hosts"
        let filePath = defaultArg hostsFilePath defaultHostsPath
        
        let normalizeEntry (line: string) =
            // Normalize whitespace in a line to compare entries
            let parts = line.Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length >= 2 then
                Some (parts.[0].Trim().ToLowerInvariant(), parts.[1].Trim().ToLowerInvariant())
            else
                None
        
        let expectedEntry = (ipAddress.Trim().ToLowerInvariant(), host.Trim().ToLowerInvariant())
        
        let findEntry (lines: string[]) =
            lines
            |> Array.tryFind (fun line ->
                let trimmed = line.Trim()
                if trimmed.StartsWith("#") || String.IsNullOrWhiteSpace(trimmed) then
                    false
                else
                    match normalizeEntry line with
                    | Some (ip, h) -> h = snd expectedEntry
                    | None -> false
            )
        
        let entryMatches (line: string) =
            match normalizeEntry line with
            | Some entry -> entry = expectedEntry
            | None -> false
        
        { new IResource with
            member this.PresentableName = $"Host file entry \"{host}\""
            
            member this.AlreadyApplied() = async {
                if not (File.Exists filePath) then
                    return false
                else
                    let! ct = Async.CancellationToken
                    let! lines = Async.AwaitTask <| File.ReadAllLinesAsync(filePath, ct)
                    
                    match findEntry lines with
                    | Some line -> return entryMatches line
                    | None -> return false
            }
            
            member this.Apply() = async {
                let! ct = Async.CancellationToken
                
                // Ensure the file exists
                if not (File.Exists filePath) then
                    failwithf $"Hosts file not found at \"{filePath}\". Ensure the file exists or specify a valid path using the hostsFilePath parameter."
                
                let! lines = Async.AwaitTask <| File.ReadAllLinesAsync(filePath, ct)
                
                let existingEntry = findEntry lines
                
                let newLines =
                    match existingEntry with
                    | Some line ->
                        // Replace existing entry
                        lines |> Array.map (fun l ->
                            if l = line then
                                $"{ipAddress}\t{host}"
                            else
                                l
                        )
                    | None ->
                        // Add new entry at the end
                        Array.append lines [| $"{ipAddress}\t{host}" |]
                
                do! Async.AwaitTask(File.WriteAllLinesAsync(filePath, newLines, ct))
            }
        }
