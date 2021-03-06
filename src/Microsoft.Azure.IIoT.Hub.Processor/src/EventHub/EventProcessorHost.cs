// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.Hub.Processor.EventHub {
    using Microsoft.Azure.IIoT.Diagnostics;
    using Microsoft.Azure.IIoT.Exceptions;
    using Microsoft.Azure.EventHubs;
    using Microsoft.Azure.EventHubs.Processor;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Autofac;

    /// <summary>
    /// Implementation of event processor host interface to host event
    /// processors.
    /// </summary>
    public class EventProcessorHost : IStartable, IDisposable, IEventProcessorHost {

        /// <summary>
        /// Create host wrapper
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="config"></param>
        /// <param name="logger"></param>
        public EventProcessorHost(IEventProcessorFactory factory,
            IEventProcessorConfig config, ILogger logger) :
            this (factory, config, null, null, logger) {
        }

        /// <summary>
        /// Create host wrapper
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="config"></param>
        /// <param name="logger"></param>
        /// <param name="checkpoint"></param>
        /// <param name="lease"></param>
        public EventProcessorHost(IEventProcessorFactory factory,
            IEventProcessorConfig config, ICheckpointManager checkpoint,
            ILeaseManager lease, ILogger logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _lease = lease;
            _checkpoint = checkpoint;
            _lock = new SemaphoreSlim(1);
        }

        /// <inheritdoc/>
        public async Task StartAsync() {
            if (_host != null) {
                return;
            }
            try {
                await _lock.WaitAsync();
                if (_host != null) {
                    return;
                }

                var consumerGroup = _config.ConsumerGroup;
                if (string.IsNullOrEmpty(consumerGroup)) {
                    consumerGroup = "$default";
                }
                if (_lease != null && _checkpoint != null) {
                    _host = new EventHubs.Processor.EventProcessorHost(
                        $"host-{Guid.NewGuid()}", _config.EventHubPath, consumerGroup,
                        GetEventHubConnectionString(), _checkpoint, _lease);
                }
                else if (_config.BlobStorageConnString != null &&
                    _config.LeaseContainerName != null) {
                    _host = new EventHubs.Processor.EventProcessorHost(
                        _config.EventHubPath, consumerGroup, GetEventHubConnectionString(),
                        _config.BlobStorageConnString, _config.LeaseContainerName);
                }
                else {
                    _logger.Error("No checkpointing storage configured or checkpoint " +
                        "manager/lease manager implementation injected.");
                    throw new InvalidConfigurationException(
                        "Invalid checkpoint configuration.");
                }
                await _host.RegisterEventProcessorFactoryAsync(
                    _factory, new EventProcessorOptions {
                        InitialOffsetProvider = s =>
                            EventPosition.FromEnqueuedTime(DateTime.UtcNow),
                        MaxBatchSize = _config.ReceiveBatchSize,
                        ReceiveTimeout = _config.ReceiveTimeout,
                        InvokeProcessorAfterReceiveTimeout = true
                    });
            }
            catch (Exception ex) {
                _logger.Error("Error starting event processor host", () => ex);
                _host = null;
                throw ex;
            }
            finally {
                _lock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync() {
            if (_host == null) {
                return;
            }
            try {
                await _lock.WaitAsync();
                if (_host != null) {
                    await _host.UnregisterEventProcessorAsync();
                    _host = null;
                }
            }
            finally {
                _lock.Release();
            }
        }

        /// <inheritdoc/>
        public void Start() => StartAsync().Wait();

        /// <inheritdoc/>
        public void Dispose() => StopAsync().Wait();

        /// <summary>
        /// Helper to get connection string and validate configuration
        /// </summary>
        private string GetEventHubConnectionString() {
            if (!string.IsNullOrEmpty(_config.EventHubConnString)) {
                try {
                    var csb = new EventHubsConnectionStringBuilder(
                        _config.EventHubConnString);
                    if (!string.IsNullOrEmpty(csb.EntityPath) ||
                        !string.IsNullOrEmpty(_config.EventHubPath)) {
                        if (_config.UseWebsockets) {
                            csb.TransportType = TransportType.AmqpWebSockets;
                        }
                        return csb.ToString();
                    }
                }
                catch {
                    throw new InvalidConfigurationException(
                        "Invalid Event hub connection string " +
                        $"{_config.EventHubConnString} configured.");
                }
            }
            throw new InvalidConfigurationException(
               "No Event hub connection string with entity path configured.");
        }

        private readonly SemaphoreSlim _lock;
        private readonly ILogger _logger;
        private readonly IEventProcessorConfig _config;
        private readonly IEventProcessorFactory _factory;
        private readonly ILeaseManager _lease;
        private readonly ICheckpointManager _checkpoint;
        private EventHubs.Processor.EventProcessorHost _host;
    }
}
