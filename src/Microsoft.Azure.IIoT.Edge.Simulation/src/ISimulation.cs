﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.Edge.Simulation {
    using Microsoft.Azure.IIoT.Edge.Deployment;
    using Microsoft.Azure.IIoT.Net;
    using Microsoft.Extensions.Configuration;
    using System.Threading.Tasks;

    /// <summary>
    /// The client library creates individual Linux Virtual
    /// Machines for each *Simulation Environment* using
    /// Azure Management client and installs the iot edge runtime
    /// and provisions it in iot hub.
    ///
    /// The environment is deleted once it is disposed,
    /// cleaning up all resources, including the IoT Edge
    /// device that was provisioned in IoT Hub.
    /// </summary>
    public interface ISimulation {

        /// <summary>
        /// The id of the simulation
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The device id for the edge device in the
        /// simulation environment
        /// </summary>
        string EdgeDeviceId { get; }

        /// <summary>
        /// Create simulated device in environment. The
        /// device can be started and stopped to simulate
        /// device failures.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="configuration"></param>
        /// <returns>the simulated device</returns>
        ISimulatedDevice CreateDevice(DeviceType type,
            IConfiguration configuration);

        /// <summary>
        /// Open a secure shell
        /// </summary>
        /// <returns></returns>
        Task<ISecureShell> OpenSecureShellAsync();

        /// <summary>
        /// Whether the edge device is running correctly
        /// or not.
        /// </summary>
        /// <returns>true if the gateway is running</returns>
        Task<bool> IsEdgeRunningAsync();

        /// <summary>
        /// Check connection status
        /// </summary>
        /// <returns></returns>
        Task<bool> IsEdgeConnectedAsync();

        /// <summary>
        /// Get edge device logs if possible
        /// </summary>
        /// <returns></returns>
        Task<string> GetEdgeLogAsync();

        /// <summary>
        /// Restarts the edge gateway service in the
        /// simulation
        /// </summary>
        /// <returns></returns>
        Task ResetEdgeAsync();

        /// <summary>
        /// Reset entire simulation
        /// </summary>
        /// <returns></returns>
        Task RestartAsync();
    }
}