// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.Module {
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using System;

    /// <summary>
    /// Cryptographic primitives
    /// </summary>
    public interface ISecureElement {

        /// <summary>
        /// Create a certificate
        /// </summary>
        /// <param name="commonName"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        Task<X509Certificate2Collection> CreateServerCertificateAsync(
            string commonName, DateTime expiration);

        /// <summary>
        /// Decrypt
        /// </summary>
        /// <param name="initializationVector"></param>
        /// <param name="ciphertext"></param>
        /// <returns></returns>
        ///
        Task<byte[]> DecryptAsync(string initializationVector,
            byte[] ciphertext);

        /// <summary>
        /// Encrypt
        /// </summary>
        /// <param name="initializationVector"></param>
        /// <param name="plaintext"></param>
        /// <returns></returns>
        Task<byte[]> EncryptAsync(string initializationVector,
            byte[] plaintext);
    }
}
