// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.Module.Framework.Hosting {
    using Microsoft.Azure.IIoT.Module.Framework.Services;
    using Microsoft.Azure.IIoT.Module.Models;
    using Microsoft.Azure.IIoT.Module.Default;
    using Microsoft.Azure.IIoT.Module;
    using Microsoft.Azure.IIoT.Hub;
    using Microsoft.Azure.IIoT.Diagnostics;
    using Microsoft.Azure.IIoT.Exceptions;
    using Microsoft.Azure.Devices.Client;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net;

    /// <summary>
    /// Provides request routing to module controllers
    /// </summary>
    public class MethodRouter : IMethodRouter, IMethodHandler {

        /// <summary>
        /// Property Di to prevent circular dependency between host and controller
        /// </summary>
        public IEnumerable<IMethodController> Controllers {
            set {
                foreach (var controller in value) {
                    AddToCallTable(controller);
                }
            }
        }

        /// <summary>
        /// Create router
        /// </summary>
        /// <param name="logger"></param>
        public MethodRouter(ILogger logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Create chunk server
            var server = new ChunkMethodServer(this, logger);
            _calltable = new Dictionary<string, IMethodInvoker> {
                [MethodNames.Call] = new ChunkMethodServerInvoker(server)
            };
        }

        /// <inheritdoc/>
        public async Task<MethodResponse> InvokeMethodAsync(MethodRequest request) {
            const int kMaxMessageSize = 127 * 1024;
            try {
                var result = await InvokeAsync(request.Name, request.Data,
                    ContentEncodings.MimeTypeJson);
                if (result.Length > kMaxMessageSize) {
                    _logger.Error($"Result (Payload too large => {result.Length}");
                    return new MethodResponse((int)HttpStatusCode.RequestEntityTooLarge);
                }
                return new MethodResponse(result, 200);
            }
            catch (MethodCallStatusException mex) {
                var len = mex.Payload?.Length ?? 0;
                return new MethodResponse(len > kMaxMessageSize ? null : mex.Payload,
                    mex.Result);
            }
            catch (Exception) {
                return new MethodResponse((int)HttpStatusCode.InternalServerError);
            }
        }

        /// <inheritdoc/>
        public async Task<byte[]> InvokeAsync(string method, byte[] payload,
            string contentType) {
            if (!_calltable.TryGetValue(method.ToLowerInvariant(), out var invoker)) {
                throw new NotSupportedException(
                    $"Unknown controller method {method} called.");
            }
            return await invoker.InvokeAsync(payload, contentType);
        }

        /// <summary>
        /// Add target to calltable
        /// </summary>
        /// <param name="target"></param>
        private void AddToCallTable(object target) {
            var version = target.GetType().GetCustomAttribute<VersionAttribute>(true);
            foreach (var methodInfo in target.GetType().GetMethods()) {
                if (!typeof(Task).IsAssignableFrom(methodInfo.ReturnType)) {
                    // must be assignable from task
                    continue;
                }
                var tArgs = methodInfo.ReturnParameter.ParameterType
                    .GetGenericArguments();
                if (tArgs.Length != 1) {
                    // must have exactly one (serializable) type to return
                    continue;
                }
                var name = methodInfo.Name;
                if (name.EndsWith("Async", StringComparison.Ordinal)) {
                    name = name.Substring(0, name.Length - 5);
                }
                name = name.ToLowerInvariant();
                if (version != null) {
                    name = name + "_v" + version.Value;
                }
                name = name.ToLowerInvariant();
                if (!_calltable.TryGetValue(name, out var invoker)) {
                    invoker = new DynamicInvoker(_logger);
                    _calltable.Add(name, invoker);
                }
                if (invoker is DynamicInvoker dynamicInvoker) {
                    dynamicInvoker.Add(target, methodInfo);
                }
                else {
                    // Should never happen...
                    throw new InvalidOperationException(
                        $"Cannot add method {name} since invoker is private.");
                }
            }
        }

        /// <summary>
        /// Represents an invoker
        /// </summary>
        private interface IMethodInvoker : IDisposable {

            /// <summary>
            /// Invoke method and return result.
            /// </summary>
            /// <param name="payload"></param>
            /// <param name="contentType"></param>
            /// <returns></returns>
            Task<byte[]> InvokeAsync(byte[] payload, string contentType);
        }

        /// <summary>
        /// Manage chunked messages
        /// </summary>
        private class ChunkMethodServerInvoker : IMethodInvoker {

            /// <summary>
            /// Create invoker
            /// </summary>
            /// <param name="server"></param>
            public ChunkMethodServerInvoker(IChunkMethodServer server) {
                _server = server;
            }

            /// <inheritdoc/>
            public async Task<byte[]> InvokeAsync(byte[] payload, string contentType) {
                var data = JsonConvertEx.DeserializeObject<MethodChunkModel>(
                    Encoding.UTF8.GetString(payload));
                data = await _server.ProcessAsync(data);
                return Encoding.UTF8.GetBytes(JsonConvertEx.SerializeObject(data));
            }

            /// <inheritdoc/>
            public void Dispose() => _server.Dispose();

            private readonly IChunkMethodServer _server;
        }

        /// <summary>
        /// Encapsulates invoking a matching service on the controller
        /// </summary>
        private class DynamicInvoker : IMethodInvoker {

            /// <summary>
            /// Create dynamic invoker
            /// </summary>
            public DynamicInvoker(ILogger logger) {
                _logger = logger;
                _invokers = new List<JsonMethodInvoker>();
            }

            /// <summary>
            /// Add invoker
            /// </summary>
            /// <param name="controller"></param>
            /// <param name="controllerMethod"></param>
            public void Add(object controller, MethodInfo controllerMethod) {
                _logger.Verbose($"Adding {controller.GetType().Name}.{controllerMethod.Name}" +
                    " method to invoker...");
                _invokers.Add(new JsonMethodInvoker(controller, controllerMethod, _logger));
            }

            /// <inheritdoc/>
            public async Task<byte[]> InvokeAsync(byte[] payload, string contentType) {
                Exception e = null;
                foreach (var invoker in _invokers) {
                    try {
                        return await invoker.InvokeAsync(payload, contentType);
                    }
                    catch (Exception ex) {
                        // Save last, and continue
                        e = ex;
                    }
                }
                _logger.Verbose($"Exception during method invocation.", e);
                throw e;
            }

            /// <inheritdoc/>
            public void Dispose() {
                foreach (var invoker in _invokers) {
                    invoker.Dispose();
                }
            }

            private readonly ILogger _logger;
            private readonly List<JsonMethodInvoker> _invokers;
        }

        /// <summary>
        /// Invokes a method with json payload
        /// </summary>
        private class JsonMethodInvoker : IMethodInvoker {

            /// <summary>
            /// Default filter implementation if none is specified
            /// </summary>
            private class DefaultFilter : ExceptionFilterAttribute {
                public override Exception Filter(Exception exception, out int status) {
                    status = (int)MethodResposeStatusCode.BadRequest;
                    return exception;
                }
            }

            /// <summary>
            /// Create invoker
            /// </summary>
            /// <param name="controller"></param>
            /// <param name="controllerMethod"></param>
            /// <param name="logger"></param>
            public JsonMethodInvoker(object controller, MethodInfo controllerMethod,
                ILogger logger) {
                _logger = logger;
                _controller = controller;
                _controllerMethod = controllerMethod;
                _methodParams = _controllerMethod.GetParameters();
                _ef = _controllerMethod.GetCustomAttribute<ExceptionFilterAttribute>(true) ??
                    controller.GetType().GetCustomAttribute<ExceptionFilterAttribute>(true) ??
                    new DefaultFilter();
                _methodTaskContinuation = _methodResponseAsContinuation.MakeGenericMethod(
                    _controllerMethod.ReturnParameter.ParameterType
                        .GetGenericArguments()[0]);
            }

            /// <inheritdoc/>
            public Task<byte[]> InvokeAsync(byte[] payload, string contentType) {
                object task;
                try {
                    object[] inputs;
                    if (_methodParams.Length == 1) {
                        var data = JsonConvertEx.DeserializeObject(
                            Encoding.UTF8.GetString(payload), _methodParams[0].ParameterType);
                        inputs = new[] { data };
                    }
                    else {
                        var data = (JObject)JToken.Parse(Encoding.UTF8.GetString(payload));
                        inputs = _methodParams.Select(param => {
                            if (data.TryGetValue(param.Name,
                                StringComparison.InvariantCultureIgnoreCase, out var value)) {
                                return value.ToObject(param.ParameterType);
                            }
                            return param.HasDefaultValue ? param.DefaultValue : null;
                        }).ToArray();
                    }
                    task = _controllerMethod.Invoke(_controller, inputs);
                }
                catch (Exception e) {
                    task = Task.FromException(e);
                }
                return (Task<byte[]>)_methodTaskContinuation.Invoke(this, new[] {
                    task
                });
            }

            /// <inheritdoc/>
            public void Dispose() { }

            /// <summary>
            /// Helper to convert a typed response to buffer or throw appropriate
            /// exception as continuation.
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="task"></param>
            /// <returns></returns>
            public Task<byte[]> MethodResultConverterContinuation<T>(Task<T> task) {
                return task.ContinueWith(tr => {
                    if (tr.IsFaulted || tr.IsCanceled) {
                        var ex = tr.Exception?.Flatten().InnerExceptions.FirstOrDefault();
                        if (ex == null) {
                            ex = new TaskCanceledException();
                        }
                        _logger.Verbose($"Method call error", ex);
                        ex = _ef.Filter(ex, out var status);
                        throw new MethodCallStatusException(ex != null ?
                            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex)) : null, status);
                    }
                    return Encoding.UTF8.GetBytes(
                        JsonConvertEx.SerializeObject(tr.Result));
                });
            }

            private static readonly MethodInfo _methodResponseAsContinuation =
                typeof(JsonMethodInvoker).GetMethod(nameof(MethodResultConverterContinuation),
                    BindingFlags.Public | BindingFlags.Instance);
            private readonly ILogger _logger;
            private readonly object _controller;
            private readonly ParameterInfo[] _methodParams;
            private readonly ExceptionFilterAttribute _ef;
            private readonly MethodInfo _controllerMethod;
            private readonly MethodInfo _methodTaskContinuation;
        }

        private readonly ILogger _logger;
        private readonly Dictionary<string, IMethodInvoker> _calltable;
    }
}
