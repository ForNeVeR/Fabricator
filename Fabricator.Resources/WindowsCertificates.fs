// SPDX-FileCopyrightText: 2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Resources.WindowsCertificates

open System
open System.IO
open System.Security.Cryptography.X509Certificates
open Fabricator.Core
open TruePath
open TruePath.SystemIo

/// <summary>
/// Represents a certificate store location and name for certificate installation.
/// </summary>
type CertificateStoreLocation = {
    /// The store location (e.g., LocalMachine or CurrentUser)
    Location: StoreLocation
    /// The store name (e.g., Root, My, CA, etc.)
    StoreName: StoreName
}

/// <summary>
/// Common certificate store locations for convenience.
/// </summary>
type CertificateStores =
    /// <summary>
    /// Trusted Root Certification Authorities in Local Machine.
    /// Equivalent to Cert:\LocalMachine\Root in PowerShell.
    /// </summary>
    static member LocalMachineTrustedRootCertificationAuthorities = {
        Location = StoreLocation.LocalMachine
        StoreName = StoreName.Root
    }
    
    /// <summary>
    /// Personal certificate store in Local Machine.
    /// Equivalent to Cert:\LocalMachine\My in PowerShell.
    /// </summary>
    static member LocalMachinePersonal = {
        Location = StoreLocation.LocalMachine
        StoreName = StoreName.My
    }
    
    /// <summary>
    /// Trusted Root Certification Authorities in Current User.
    /// Equivalent to Cert:\CurrentUser\Root in PowerShell.
    /// </summary>
    static member CurrentUserTrustedRootCertificationAuthorities = {
        Location = StoreLocation.CurrentUser
        StoreName = StoreName.Root
    }
    
    /// <summary>
    /// Personal certificate store in Current User.
    /// Equivalent to Cert:\CurrentUser\My in PowerShell.
    /// </summary>
    static member CurrentUserPersonal = {
        Location = StoreLocation.CurrentUser
        StoreName = StoreName.My
    }

let private getCertificateFromFile (path: AbsolutePath) =
    if not(path.Exists()) then
        failwithf $"Certificate file does not exist: {path.Value}"
    X509CertificateLoader.LoadCertificateFromFile(path.Value)

let private isCertificateInStore (cert: X509Certificate2) (storeLocation: CertificateStoreLocation) =
    use store = new X509Store(storeLocation.StoreName, storeLocation.Location)
    store.Open(OpenFlags.ReadOnly)
    let certificates = store.Certificates
    let found = certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
    found.Count > 0

let private addCertificateToStore (cert: X509Certificate2) (storeLocation: CertificateStoreLocation) =
    use store = new X509Store(storeLocation.StoreName, storeLocation.Location)
    store.Open(OpenFlags.ReadWrite)
    store.Add(cert)

/// <summary>
/// Creates a resource that ensures a certificate is installed in the specified certificate store.
/// This is a pure .NET implementation equivalent to PowerShell's Import-Certificate command.
/// </summary>
/// <param name="certificatePath">The absolute path to the certificate file to install.</param>
/// <param name="storeLocation">The certificate store location where the certificate should be installed.</param>
/// <returns>
/// An implementation of the <see cref="T:Fabricator.Core.IResource"/> interface, which provides methods
/// to check if the certificate is already installed and to install it if needed.
/// </returns>
/// <remarks>
/// This feature is Windows-only. On other platforms, certificate management varies significantly.
/// The resource identifies certificates by their thumbprint, so if a certificate with the same
/// thumbprint already exists in the store, it will be considered already applied.
/// </remarks>
/// <example>
/// Install a certificate to the Local Machine Root store:
/// <code>
/// let certResource = trustedCertificate(AbsolutePath @"C:\certs\mycert.cer", CertificateStores.LocalMachineTrustedRootCertificationAuthorities)
/// </code>
/// </example>
let trustedCertificate (certificatePath: AbsolutePath) (storeLocation: CertificateStoreLocation): IResource =
    let cert = lazy (getCertificateFromFile certificatePath)
    
    { new IResource with
        member this.PresentableName =
            let fileName = Path.GetFileName(certificatePath.Value)
            $"Certificate \"{fileName}\" in {storeLocation.Location}/{storeLocation.StoreName}"
        
        member this.AlreadyApplied() = async {
            let certificate = cert.Value
            return isCertificateInStore certificate storeLocation
        }
        
        member this.Apply() = async {
            let certificate = cert.Value
            addCertificateToStore certificate storeLocation
        }
    }
